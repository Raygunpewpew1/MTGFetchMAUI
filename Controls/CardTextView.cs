using MTGFetchMAUI.Services;
using SkiaSharp;
using SkiaSharp.Views.Maui;
using SkiaSharp.Views.Maui.Controls;
using System.Text.RegularExpressions;

namespace MTGFetchMAUI.Controls;

/// <summary>
/// SkiaSharp-based card text renderer with keyword highlighting
/// and inline SVG mana symbol rendering.
/// Port of TMTGCardTextView from MTGCardTextView.pas.
/// Uses ManaSvgCache to render actual SVG mana symbols instead of colored circles.
/// </summary>
public class CardTextView : SKCanvasView
{
    // ── Constants ──────────────────────────────────────────────────────
    private static readonly Regex SymbolPattern = new(@"\{([^}]+)\}", RegexOptions.Compiled);

    private static readonly string[] DefaultKeywords =
    [
        "Flying", "First strike", "Double strike", "Deathtouch", "Haste",
        "Hexproof", "Indestructible", "Lifelink", "Menace", "Reach",
        "Trample", "Vigilance", "Flash", "Defender", "Ward", "Shroud",
        "Fear", "Intimidate", "Prowess", "Convoke", "Delve",
        "Equip", "Enchant", "Exile", "Transform", "Mill",
        "Scry", "Surveil", "Protection", "Flashback", "Kicker",
        "Cycling", "Cascade", "Infect", "Toxic"
    ];

    // ── State ──────────────────────────────────────────────────────────
    private string _cardText = "";
    private float _textSize = 14f;
    private SKColor _textColor = new(240, 240, 240);
    private SKColor _keywordColor = new(255, 215, 0); // Gold
    private bool _keywordBold = true;
    private bool _enableKeywords = true;
    private float _lineSpacing = 1.3f;
    private float _symbolSize = 16f;
    private float _shadowBlur = 2f;
    private SKColor _shadowColor = new(0, 0, 0, 128);

    private Regex? _keywordRegex;
    private bool _regexDirty = true;
    private readonly List<string> _keywords = new(DefaultKeywords);

    // Cached layout
    private readonly List<TextRun> _runs = [];
    private readonly List<LayoutGlyph> _layoutGlyphs = [];
    private Size _measuredSize;
    private double _lastMeasureWidth = -1;
    private bool _needsLayout = true;
    private bool _needsRebuild = true;
    private int _updateCount;

    // Cached Resources
    private SKPaint? _normalPaint;
    private SKFont? _normalFont;
    private SKPaint? _keywordPaint;
    private SKFont? _keywordFont;
    private SKTypeface? _keywordTypeface;
    private SKPaint? _shadowPaint;
    private SKMaskFilter? _shadowMaskFilter;
    private SKFont? _shadowFont;

    // ── Types ──────────────────────────────────────────────────────────

    private enum RunType { Normal, Keyword, Symbol, Newline }

    private record struct TextRun(RunType Type, string Text);

    private record struct LayoutGlyph(
        float X, float Y, float Width, float Height,
        RunType Type, string Text);

    // ── Properties ─────────────────────────────────────────────────────

    public string CardText
    {
        get => _cardText;
        set { _cardText = value; InvalidateLayout(); }
    }

    public float TextSize
    {
        get => _textSize;
        set { _textSize = Math.Clamp(value, 8f, 72f); InvalidatePaints(); InvalidateLayout(); }
    }

    public SKColor TextColor
    {
        get => _textColor;
        set { _textColor = value; InvalidatePaints(); }
    }

    public SKColor KeywordColor
    {
        get => _keywordColor;
        set { _keywordColor = value; InvalidatePaints(); }
    }

    public bool KeywordBold
    {
        get => _keywordBold;
        set { _keywordBold = value; InvalidatePaints(); }
    }

    public bool EnableKeywordHighlighting
    {
        get => _enableKeywords;
        set { _enableKeywords = value; InvalidateLayout(); }
    }

    public float LineSpacing
    {
        get => _lineSpacing;
        set { _lineSpacing = Math.Clamp(value, 0.8f, 3f); InvalidateLayout(); }
    }

    public float SymbolSize
    {
        get => _symbolSize;
        set { _symbolSize = Math.Clamp(value, 8f, 48f); InvalidateLayout(); }
    }

    public float ShadowBlur
    {
        get => _shadowBlur;
        set { _shadowBlur = value; InvalidatePaints(); }
    }

    public SKColor ShadowColor
    {
        get => _shadowColor;
        set { _shadowColor = value; InvalidatePaints(); }
    }

    // ── Public API ─────────────────────────────────────────────────────

    public void BeginUpdate() => _updateCount++;

    public void EndUpdate()
    {
        _updateCount = Math.Max(0, _updateCount - 1);
        if (_updateCount == 0 && _needsRebuild)
        {
            InvalidateLayout();
        }
    }

    public void AddKeyword(string keyword)
    {
        if (!_keywords.Contains(keyword, StringComparer.OrdinalIgnoreCase))
        {
            _keywords.Add(keyword);
            _regexDirty = true;
            InvalidateLayout();
        }
    }

    public void RemoveKeyword(string keyword)
    {
        _keywords.Remove(keyword);
        _regexDirty = true;
        InvalidateLayout();
    }

    public void ClearKeywords()
    {
        _keywords.Clear();
        _regexDirty = true;
        InvalidateLayout();
    }

    // ── Internals ──────────────────────────────────────────────────────

    private void InvalidatePaints()
    {
        _normalPaint?.Dispose(); _normalPaint = null;
        _normalFont?.Dispose(); _normalFont = null;

        _keywordPaint?.Dispose(); _keywordPaint = null;
        _keywordFont?.Dispose(); _keywordFont = null;

        if (_keywordTypeface != null && _keywordTypeface != SKTypeface.Default)
        {
             _keywordTypeface.Dispose();
        }
        _keywordTypeface = null;

        _shadowPaint?.Dispose(); _shadowPaint = null;
        _shadowMaskFilter?.Dispose(); _shadowMaskFilter = null;
        _shadowFont?.Dispose(); _shadowFont = null;

        InvalidateSurface();
    }

    private void InvalidateLayout()
    {
        if (_updateCount > 0)
        {
            _needsRebuild = true;
            return;
        }

        _needsRebuild = true;
        _needsLayout = true;
        InvalidateMeasure(); // Triggers MeasureOverride
        InvalidateSurface(); // Ensures repaint
    }

    private void EnsureRegex()
    {
        if (!_regexDirty && _keywordRegex != null) return;

        if (_keywords.Count == 0)
        {
            _keywordRegex = null;
            _regexDirty = false;
            return;
        }

        // Sort by length descending so longer keywords match first
        var sorted = _keywords.OrderByDescending(k => k.Length).ToList();
        var escaped = sorted.Select(Regex.Escape);
        var pattern = @"\b(" + string.Join("|", escaped) + @")\b";
        _keywordRegex = new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.Compiled);
        _regexDirty = false;
    }

    /// <summary>
    /// Parse card text into a sequence of runs (normal text, keywords, symbols, newlines).
    /// </summary>
    private void RebuildRuns()
    {
        _runs.Clear();
        _needsRebuild = false;

        if (string.IsNullOrEmpty(_cardText)) return;

        EnsureRegex();

        // Split text on symbol pattern, interleaving text and symbols
        var symbolMatches = SymbolPattern.Matches(_cardText);
        int pos = 0;

        foreach (Match m in symbolMatches)
        {
            // Process text before symbol
            if (m.Index > pos)
            {
                string before = _cardText[pos..m.Index];
                AddTextRuns(before);
            }

            // Add symbol run - normalize using the same logic as the original Delphi code
            string sym = ManaSvgCache.NormalizeSymbol(m.Groups[1].Value);
            _runs.Add(new TextRun(RunType.Symbol, sym));
            pos = m.Index + m.Length;
        }

        // Process remaining text
        if (pos < _cardText.Length)
            AddTextRuns(_cardText[pos..]);
    }

    private void AddTextRuns(string text)
    {
        // Split on newlines first
        var lines = text.Split('\n');
        for (int li = 0; li < lines.Length; li++)
        {
            if (li > 0)
                _runs.Add(new TextRun(RunType.Newline, ""));

            var line = lines[li];
            if (string.IsNullOrEmpty(line)) continue;

            if (_enableKeywords && _keywordRegex != null)
            {
                var matches = _keywordRegex.Matches(line);
                int linePos = 0;

                foreach (Match km in matches)
                {
                    if (km.Index > linePos)
                        _runs.Add(new TextRun(RunType.Normal, line[linePos..km.Index]));

                    _runs.Add(new TextRun(RunType.Keyword, km.Value));
                    linePos = km.Index + km.Length;
                }

                if (linePos < line.Length)
                    _runs.Add(new TextRun(RunType.Normal, line[linePos..]));
            }
            else
            {
                _runs.Add(new TextRun(RunType.Normal, line));
            }
        }
    }

    // ── Layout & Measurement ───────────────────────────────────────────

    protected override Size MeasureOverride(double widthConstraint, double heightConstraint)
    {
        float width = float.IsPositiveInfinity((float)widthConstraint) ? 300f : (float)widthConstraint;
        if (width <= 0) width = 300f; // Minimal width fallback

        if (_needsLayout || _needsRebuild || Math.Abs(width - _lastMeasureWidth) > 1f)
        {
            ComputeLayout(width);
        }

        return _measuredSize;
    }

    private void ComputeLayout(float maxWidth)
    {
        if (_needsRebuild)
            RebuildRuns();

        _layoutGlyphs.Clear();
        _lastMeasureWidth = maxWidth;
        _needsLayout = false;

        using var measureFont = new SKFont(SKTypeface.Default, _textSize);

        float lineHeight = _textSize * _lineSpacing;
        float x = 0;
        float y = lineHeight;
        float spaceWidth = measureFont.MeasureText(" ");

        for (int ri = 0; ri < _runs.Count; ri++)
        {
            var run = _runs[ri];

            if (run.Type == RunType.Newline)
            {
                x = 0;
                y += lineHeight;
                continue;
            }

            if (run.Type == RunType.Symbol)
            {
                float symW = _symbolSize + 2f;

                if (x + symW > maxWidth && x > 0)
                {
                    x = 0;
                    y += lineHeight;
                }

                _layoutGlyphs.Add(new LayoutGlyph(x, y, symW, _symbolSize, RunType.Symbol, run.Text));

                bool nextIsSymbol = ri + 1 < _runs.Count && _runs[ri + 1].Type == RunType.Symbol;
                x += symW + (nextIsSymbol ? 0 : spaceWidth);
                continue;
            }

            // Text run (Normal or Keyword) - word wrap
            var words = SplitIntoWords(run.Text);
            foreach (var word in words)
            {
                float wordWidth = measureFont.MeasureText(word);

                if (x + wordWidth > maxWidth && x > 0)
                {
                    x = 0;
                    y += lineHeight;
                }

                _layoutGlyphs.Add(new LayoutGlyph(x, y, wordWidth, _textSize, run.Type, word));
                x += wordWidth;

                if (run.Type == RunType.Keyword && !word.EndsWith(' '))
                    x += spaceWidth;
            }
        }

        // Calculate final size
        float finalHeight = y + (_textSize * 0.3f); // Padding
        if (x > 0 && y == lineHeight && _layoutGlyphs.Count == 0) finalHeight = 0; // Empty

        // If we have content, ensure minimum height
        if (_layoutGlyphs.Count > 0)
             finalHeight = Math.Max(finalHeight, lineHeight);

        _measuredSize = new Size(maxWidth, finalHeight + 8); // Add some padding
    }

    private static List<string> SplitIntoWords(string text)
    {
        // Split keeping spaces attached to the word after them
        var words = new List<string>();
        int start = 0;
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] == ' ' && i > start)
            {
                words.Add(text[start..(i + 1)]); // Include trailing space
                start = i + 1;
            }
        }
        if (start < text.Length)
            words.Add(text[start..]);
        return words;
    }

    // ── Painting ───────────────────────────────────────────────────────

    private void EnsurePaints()
    {
        if (_normalPaint == null)
        {
            _normalPaint = new SKPaint { Color = _textColor, IsAntialias = true };
            _normalFont = new SKFont(SKTypeface.Default, _textSize);

            _keywordPaint = new SKPaint { Color = _keywordColor, IsAntialias = true };

            if (_keywordBold)
                _keywordTypeface = SKTypeface.FromFamilyName(null, SKFontStyle.Bold);
            else
                _keywordTypeface = SKTypeface.Default;

            _keywordFont = new SKFont(_keywordTypeface, _textSize);

            if (_shadowBlur > 0)
            {
                 _shadowMaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, _shadowBlur);
                 _shadowPaint = new SKPaint
                 {
                    Color = _shadowColor,
                    IsAntialias = true,
                    MaskFilter = _shadowMaskFilter
                 };
                 _shadowFont = new SKFont(SKTypeface.Default, _textSize);
            }
        }
    }

    protected override void OnPaintSurface(SKPaintSurfaceEventArgs e)
    {
        var canvas = e.Surface.Canvas;
        var info = e.Info;
        canvas.Clear(SKColors.Transparent);

        if (_layoutGlyphs.Count == 0) return;

        EnsurePaints();

        float scale = info.Width / (float)(Width > 0 ? Width : 1);
        if (scale <= 0) scale = 1;

        canvas.Save();
        canvas.Scale(scale);

        // Draw shadow layer first
        if (_shadowBlur > 0 && _shadowPaint != null && _shadowFont != null)
        {
            foreach (var g in _layoutGlyphs)
            {
                if (g.Type is RunType.Normal or RunType.Keyword)
                    canvas.DrawText(g.Text, g.X + 1f, g.Y + 1f, _shadowFont, _shadowPaint);
            }
        }

        // Draw text and symbols
        // We assume _normalPaint, _normalFont, _keywordPaint, _keywordFont are not null after EnsurePaints

        foreach (var g in _layoutGlyphs)
        {
            switch (g.Type)
            {
                case RunType.Normal:
                    if (_normalFont != null && _normalPaint != null)
                        canvas.DrawText(g.Text, g.X, g.Y, _normalFont, _normalPaint);
                    break;

                case RunType.Keyword:
                    if (_keywordFont != null && _keywordPaint != null)
                        canvas.DrawText(g.Text, g.X, g.Y, _keywordFont, _keywordPaint);
                    break;

                case RunType.Symbol:
                    // Draw SVG mana symbol at the placeholder position
                    ManaSvgCache.DrawSymbol(canvas, g.Text, g.X, g.Y - _symbolSize * 0.8f, _symbolSize);
                    break;
            }
        }

        canvas.Restore();
    }
}
