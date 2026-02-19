using System.Text.RegularExpressions;
using MTGFetchMAUI.Services;
using SkiaSharp;
using SkiaSharp.Views.Maui;
using SkiaSharp.Views.Maui.Controls;

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
    private float _measuredHeight;
    private float _lastLayoutWidth;
    private int _updateCount;
    private bool _needsRebuild;

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
        set { _cardText = value; MarkDirty(); }
    }

    public float TextSize
    {
        get => _textSize;
        set { _textSize = Math.Clamp(value, 8f, 72f); MarkDirty(); }
    }

    public SKColor TextColor
    {
        get => _textColor;
        set { _textColor = value; InvalidateSurface(); }
    }

    public SKColor KeywordColor
    {
        get => _keywordColor;
        set { _keywordColor = value; InvalidateSurface(); }
    }

    public bool KeywordBold
    {
        get => _keywordBold;
        set { _keywordBold = value; InvalidateSurface(); }
    }

    public bool EnableKeywordHighlighting
    {
        get => _enableKeywords;
        set { _enableKeywords = value; MarkDirty(); }
    }

    public float LineSpacing
    {
        get => _lineSpacing;
        set { _lineSpacing = Math.Clamp(value, 0.8f, 3f); MarkDirty(); }
    }

    public float SymbolSize
    {
        get => _symbolSize;
        set { _symbolSize = Math.Clamp(value, 8f, 48f); MarkDirty(); }
    }

    public float ShadowBlur
    {
        get => _shadowBlur;
        set { _shadowBlur = value; InvalidateSurface(); }
    }

    // ── Public API ─────────────────────────────────────────────────────

    public void BeginUpdate() => _updateCount++;

    public void EndUpdate()
    {
        _updateCount = Math.Max(0, _updateCount - 1);
        if (_updateCount == 0 && _needsRebuild)
        {
            _needsRebuild = false;
            RebuildRuns();
            InvalidateSurface();
        }
    }

    public void AddKeyword(string keyword)
    {
        if (!_keywords.Contains(keyword, StringComparer.OrdinalIgnoreCase))
        {
            _keywords.Add(keyword);
            _regexDirty = true;
            MarkDirty();
        }
    }

    public void RemoveKeyword(string keyword)
    {
        _keywords.Remove(keyword);
        _regexDirty = true;
        MarkDirty();
    }

    public void ClearKeywords()
    {
        _keywords.Clear();
        _regexDirty = true;
        MarkDirty();
    }

    // ── Internals ──────────────────────────────────────────────────────

    private void MarkDirty()
    {
        if (_updateCount > 0)
        {
            _needsRebuild = true;
            return;
        }
        RebuildRuns();

        // Ensure we have some height to trigger a paint pass
        if (HeightRequest < 0)
            HeightRequest = 40;

        InvalidateSurface();
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
    /// Symbol text is normalized using ManaSvgCache.NormalizeSymbol (replace / with _, uppercase).
    /// </summary>
    private void RebuildRuns()
    {
        _runs.Clear();
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

    // ── Painting ───────────────────────────────────────────────────────

    protected override void OnPaintSurface(SKPaintSurfaceEventArgs e)
    {
        var canvas = e.Surface.Canvas;
        var info = e.Info;
        canvas.Clear(SKColors.Transparent);

        if (_runs.Count == 0 || info.Width == 0) return;

        float viewWidth = (float)(Width > 0 ? Width : 300);
        float scale = info.Width / viewWidth;

        canvas.Save();
        canvas.Scale(scale);

        // Layout runs into positioned glyphs with word wrapping
        var glyphs = LayoutGlyphs(viewWidth);

        // Draw shadow layer first
        if (_shadowBlur > 0)
        {
            using var shadowPaint = new SKPaint
            {
                Color = _shadowColor,
                IsAntialias = true,
                MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, _shadowBlur)
            };
            using var shadowFont = new SKFont(SKTypeface.Default, _textSize);
            foreach (var g in glyphs)
            {
                if (g.Type is RunType.Normal or RunType.Keyword)
                    canvas.DrawText(g.Text, g.X + 1f, g.Y + 1f, shadowFont, shadowPaint);
            }
        }

        // Draw text and symbols
        using var normalPaint = new SKPaint
        {
            Color = _textColor,
            IsAntialias = true
        };
        using var normalFont = new SKFont(SKTypeface.Default, _textSize);

        using var keywordPaint = new SKPaint
        {
            Color = _keywordColor,
            IsAntialias = true
        };

        SKTypeface? customTypeface = _keywordBold ? SKTypeface.FromFamilyName(null, SKFontStyle.Bold) : null;
        using var keywordFont = new SKFont(customTypeface ?? SKTypeface.Default, _textSize);

        foreach (var g in glyphs)
        {
            switch (g.Type)
            {
                case RunType.Normal:
                    canvas.DrawText(g.Text, g.X, g.Y, normalFont, normalPaint);
                    break;

                case RunType.Keyword:
                    canvas.DrawText(g.Text, g.X, g.Y, keywordFont, keywordPaint);
                    break;

                case RunType.Symbol:
                    // Draw SVG mana symbol at the placeholder position
                    ManaSvgCache.DrawSymbol(canvas, g.Text, g.X, g.Y - _symbolSize * 0.8f, _symbolSize);
                    break;
            }
        }

        canvas.Restore();
        customTypeface?.Dispose();

        // Update height if measured height changed
        if (glyphs.Count > 0)
        {
            float bottom = glyphs.Max(g => g.Y + g.Height * 0.3f);
            if (Math.Abs(bottom - _measuredHeight) > 2f)
            {
                _measuredHeight = bottom;
                MainThread.BeginInvokeOnMainThread(() => HeightRequest = _measuredHeight + 8);
            }
        }
    }

    private List<LayoutGlyph> LayoutGlyphs(float maxWidth)
    {
        var result = new List<LayoutGlyph>();

        using var measureFont = new SKFont(SKTypeface.Default, _textSize);

        float lineHeight = _textSize * _lineSpacing;
        float x = 0;
        float y = lineHeight; // Start at first baseline

        foreach (var run in _runs)
        {
            if (run.Type == RunType.Newline)
            {
                x = 0;
                y += lineHeight;
                continue;
            }

            if (run.Type == RunType.Symbol)
            {
                float symW = _symbolSize + 2f;

                // Wrap if symbol doesn't fit
                if (x + symW > maxWidth && x > 0)
                {
                    x = 0;
                    y += lineHeight;
                }

                result.Add(new LayoutGlyph(x, y, symW, _symbolSize, RunType.Symbol, run.Text));
                x += symW;
                continue;
            }

            // Text run - word wrap
            var words = SplitIntoWords(run.Text);
            foreach (var word in words)
            {
                float wordWidth = measureFont.MeasureText(word);

                // Wrap if needed
                if (x + wordWidth > maxWidth && x > 0)
                {
                    x = 0;
                    y += lineHeight;
                }

                result.Add(new LayoutGlyph(x, y, wordWidth, _textSize, run.Type, word));
                x += wordWidth;
            }
        }

        return result;
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

    protected override void OnSizeAllocated(double width, double height)
    {
        base.OnSizeAllocated(width, height);
        if (width > 0 && Math.Abs((float)width - _lastLayoutWidth) > 2f)
        {
            _lastLayoutWidth = (float)width;
            InvalidateSurface();
        }
    }
}
