using MTGFetchMAUI.Services;
using SkiaSharp;
using SkiaSharp.Views.Maui;
using SkiaSharp.Views.Maui.Controls;
using System.Text.RegularExpressions;

namespace MTGFetchMAUI.Controls.Legacy;

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

    // ── Static Font Cache ──────────────────────────────────────────────
    private static SKTypeface? _serifRegular;
    private static SKTypeface? _serifBold;
    private static bool _fontsLoaded;
    private static bool _loadingStarted;

    // ── State ──────────────────────────────────────────────────────────
    private string _cardText = "";
    private float _textSize = 14f;
    private SKColor _textColor = new(240, 240, 240);
    private SKColor _keywordColor = new(255, 215, 0);
    private bool _keywordBold = true;
    private bool _enableKeywords = true;
    private float _lineSpacing = 1.15f;
    private float _symbolSize = 16f;
    private float _shadowBlur = 2f;
    private SKColor _shadowColor = new(0, 0, 0, 128);

    private Regex? _keywordRegex;
    private bool _regexDirty = true;
    private readonly HashSet<string> _keywords = new(DefaultKeywords, StringComparer.OrdinalIgnoreCase);

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
    private SKPaint? _shadowPaint;
    private SKMaskFilter? _shadowMaskFilter;
    private SKFont? _shadowFont;

    private enum RunType { Normal, Keyword, Symbol, Newline }
    private record struct TextRun(RunType Type, string Text);
    private record struct LayoutGlyph(float X, float Y, RunType Type, string Text);

    public CardTextView()
    {
        if (DeviceInfo.Idiom == DeviceIdiom.Tablet || DeviceInfo.Idiom == DeviceIdiom.Desktop)
        {
            _textSize = 18f;
        }

        InitializeFonts();
    }

    private void InitializeFonts()
    {
        if (_fontsLoaded || _loadingStarted) return;
        _loadingStarted = true;

        Task.Run(async () =>
        {
            try
            {
                using var streamReg = await FileSystem.OpenAppPackageFileAsync("Fonts/CrimsonText-Regular.ttf");
                _serifRegular = SKTypeface.FromStream(streamReg);

                using var streamBold = await FileSystem.OpenAppPackageFileAsync("Fonts/CrimsonText-Bold.ttf");
                _serifBold = SKTypeface.FromStream(streamBold);

                _fontsLoaded = true;

                Dispatcher.Dispatch(() =>
                {
                    InvalidatePaints();
                    InvalidateLayout();
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[CardTextView] Font load failed: {ex}");
                _fontsLoaded = true;
            }
        });
    }

    // ── Properties ─────────────────────────────────────────────────────

    public string CardText { get => _cardText; set { _cardText = value; InvalidateLayout(); } }
    public float TextSize { get => _textSize; set { _textSize = Math.Clamp(value, 8f, 72f); InvalidatePaints(); InvalidateLayout(); } }
    public SKColor TextColor { get => _textColor; set { _textColor = value; InvalidatePaints(); } }
    public SKColor KeywordColor { get => _keywordColor; set { _keywordColor = value; InvalidatePaints(); } }
    public bool KeywordBold { get => _keywordBold; set { _keywordBold = value; InvalidatePaints(); } }
    public bool EnableKeywordHighlighting { get => _enableKeywords; set { _enableKeywords = value; InvalidateLayout(); } }
    public float LineSpacing { get => _lineSpacing; set { _lineSpacing = Math.Clamp(value, 0.8f, 3f); InvalidateLayout(); } }
    public float SymbolSize { get => _symbolSize; set { _symbolSize = Math.Clamp(value, 8f, 48f); InvalidateLayout(); } }
    public float ShadowBlur { get => _shadowBlur; set { _shadowBlur = value; InvalidatePaints(); } }
    public SKColor ShadowColor { get => _shadowColor; set { _shadowColor = value; InvalidatePaints(); } }

    // ── Public API ─────────────────────────────────────────────────────

    public void BeginUpdate() => _updateCount++;
    public void EndUpdate()
    {
        _updateCount = Math.Max(0, _updateCount - 1);
        if (_updateCount == 0 && _needsRebuild) InvalidateLayout();
    }

    public void AddKeyword(string keyword)
    {
        if (_keywords.Add(keyword))
        {
            _regexDirty = true;
            InvalidateLayout();
        }
    }

    public void RemoveKeyword(string keyword)
    {
        if (_keywords.Remove(keyword))
        {
            _regexDirty = true;
            InvalidateLayout();
        }
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
        if (_normalPaint != null)
        {
            _normalPaint.Color = _textColor;
            _normalFont?.Dispose(); _normalFont = null;
        }

        if (_keywordPaint != null)
        {
            _keywordPaint.Color = _keywordColor;
            _keywordFont?.Dispose(); _keywordFont = null;
        }

        if (_shadowPaint != null)
        {
            _shadowPaint.Color = _shadowColor;
            _shadowMaskFilter?.Dispose(); _shadowMaskFilter = null;
            _shadowFont?.Dispose(); _shadowFont = null;
        }

        // If paints are null, they will be recreated in EnsurePaints
        InvalidateSurface();
    }

    private void InvalidateLayout()
    {
        if (_updateCount > 0) { _needsRebuild = true; return; }
        _needsRebuild = true;
        _needsLayout = true;
        InvalidateMeasure();
        InvalidateSurface();
    }

    private void EnsureRegex()
    {
        if (!_regexDirty && _keywordRegex != null) return;
        if (_keywords.Count == 0) { _keywordRegex = null; _regexDirty = false; return; }

        var sorted = _keywords.OrderByDescending(k => k.Length);
        var pattern = @"\b(" + string.Join("|", sorted.Select(Regex.Escape)) + @")\b";
        _keywordRegex = new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.Compiled);
        _regexDirty = false;
    }

    private void RebuildRuns()
    {
        _runs.Clear();
        _needsRebuild = false;
        if (string.IsNullOrEmpty(_cardText)) return;

        EnsureRegex();

        var symbolMatches = SymbolPattern.Matches(_cardText);
        int pos = 0;

        foreach (Match m in symbolMatches)
        {
            if (m.Index > pos) AddTextRuns(_cardText[pos..m.Index]);
            string sym = ManaSvgCache.NormalizeSymbol(m.Groups[1].Value);
            _runs.Add(new TextRun(RunType.Symbol, sym));
            pos = m.Index + m.Length;
        }
        if (pos < _cardText.Length) AddTextRuns(_cardText[pos..]);
    }

    private void AddTextRuns(string text)
    {
        int start = 0;
        while (start < text.Length)
        {
            int newline = text.IndexOf('\n', start);
            int end = (newline == -1) ? text.Length : newline;

            if (end > start)
            {
                string line = text[start..end];
                if (_enableKeywords && _keywordRegex != null)
                {
                    var matches = _keywordRegex.Matches(line);
                    int linePos = 0;
                    foreach (Match km in matches)
                    {
                        if (km.Index > linePos) _runs.Add(new TextRun(RunType.Normal, line[linePos..km.Index]));
                        _runs.Add(new TextRun(RunType.Keyword, km.Value));
                        linePos = km.Index + km.Length;
                    }
                    if (linePos < line.Length) _runs.Add(new TextRun(RunType.Normal, line[linePos..]));
                }
                else
                {
                    _runs.Add(new TextRun(RunType.Normal, line));
                }
            }

            if (newline != -1)
            {
                _runs.Add(new TextRun(RunType.Newline, ""));
                start = newline + 1;
            }
            else break;
        }
    }

    protected override Size MeasureOverride(double widthConstraint, double heightConstraint)
    {
        float width = float.IsPositiveInfinity((float)widthConstraint) ? 300f : (float)widthConstraint;
        if (width <= 0) width = 300f;

        if (_needsLayout || _needsRebuild || Math.Abs(width - _lastMeasureWidth) > 1f)
            ComputeLayout(width);

        return _measuredSize;
    }

    private void ComputeLayout(float maxWidth)
    {
        if (_needsRebuild) RebuildRuns();
        _layoutGlyphs.Clear();
        _lastMeasureWidth = maxWidth;
        _needsLayout = false;

        var measureTypeface = _serifRegular ?? SKTypeface.Default;
        using var measureFont = new SKFont(measureTypeface, _textSize);
        float lineHeight = _textSize * _lineSpacing;
        float x = 0, y = lineHeight;
        float spaceWidth = measureFont.MeasureText(" ");

        // Padding?
        float availableWidth = Math.Max(10, maxWidth);

        for (int ri = 0; ri < _runs.Count; ri++)
        {
            var run = _runs[ri];
            if (run.Type == RunType.Newline) { x = 0; y += lineHeight; continue; }

            if (run.Type == RunType.Symbol)
            {
                float symW = _symbolSize + 2f;
                if (x + symW > availableWidth && x > 0) { x = 0; y += lineHeight; }
                _layoutGlyphs.Add(new LayoutGlyph(x, y, RunType.Symbol, run.Text));
                bool nextIsSymbol = ri + 1 < _runs.Count && _runs[ri + 1].Type == RunType.Symbol;
                x += symW + (nextIsSymbol ? 0 : spaceWidth);
                continue;
            }

            // Word wrap
            int wStart = 0;
            string text = run.Text;
            while (wStart < text.Length)
            {
                int wEnd = text.IndexOf(' ', wStart);
                bool hasSpace = wEnd != -1;
                if (!hasSpace) wEnd = text.Length;
                else wEnd++; // Include space

                string word = text[wStart..wEnd];
                float wordWidth = measureFont.MeasureText(word.TrimEnd()); // Measure only visible chars for fit check?
                // Actually include the space in measurement for placement, but check fit carefully.
                wordWidth = measureFont.MeasureText(word);

                // If word is simply too long for a single line, force break?
                // For now, let's just wrap.

                if (x + wordWidth > availableWidth && x > 0)
                {
                    x = 0;
                    y += lineHeight;
                    // If moving to new line, strip leading space if present?
                    if (word.StartsWith(' '))
                    {
                        word = word.TrimStart();
                        wordWidth = measureFont.MeasureText(word);
                    }
                }

                // Safety check: if word is still wider than availableWidth, it will overflow.
                // We could character-wrap here, but that's complex.

                _layoutGlyphs.Add(new LayoutGlyph(x, y, run.Type, word));
                x += wordWidth;
                wStart = wEnd;
            }
        }

        // Final height calculation:
        // y is the baseline of the current line.
        // We need to add descent or bottom padding.

        // If x > 0, we are on a line. If x == 0, we just finished a newline.
        float finalHeight = y + (_textSize * 0.5f);

        _measuredSize = new Size(maxWidth, finalHeight + 8);
    }

    private void EnsurePaints()
    {
        if (_normalPaint == null)
        {
            _normalPaint = new SKPaint { Color = _textColor, IsAntialias = true };
            _normalFont = new SKFont(_serifRegular ?? SKTypeface.Default, _textSize);
        }
        else if (_normalFont == null)
        {
            _normalFont = new SKFont(_serifRegular ?? SKTypeface.Default, _textSize);
        }

        if (_keywordPaint == null)
        {
            _keywordPaint = new SKPaint { Color = _keywordColor, IsAntialias = true };
            var kTypeface = _keywordBold ? (_serifBold ?? SKTypeface.Default) : (_serifRegular ?? SKTypeface.Default);
            _keywordFont = new SKFont(kTypeface, _textSize);
        }
        else if (_keywordFont == null)
        {
            var kTypeface = _keywordBold ? (_serifBold ?? SKTypeface.Default) : (_serifRegular ?? SKTypeface.Default);
            _keywordFont = new SKFont(kTypeface, _textSize);
        }

        if (_shadowBlur > 0)
        {
            if (_shadowPaint == null)
            {
                _shadowMaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, _shadowBlur);
                _shadowPaint = new SKPaint { Color = _shadowColor, IsAntialias = true, MaskFilter = _shadowMaskFilter };
                _shadowFont = new SKFont(_serifRegular ?? SKTypeface.Default, _textSize);
            }
            else if (_shadowFont == null)
            {
                _shadowFont = new SKFont(_serifRegular ?? SKTypeface.Default, _textSize);
            }
        }
    }

    protected override void OnPaintSurface(SKPaintSurfaceEventArgs e)
    {
        var canvas = e.Surface.Canvas;
        canvas.Clear(SKColors.Transparent);
        if (_layoutGlyphs.Count == 0) return;

        EnsurePaints();
        float scale = e.Info.Width / (float)(Width > 0 ? Width : 1);
        canvas.Save();
        canvas.Scale(scale);

        if (_shadowBlur > 0 && _shadowPaint != null && _shadowFont != null)
        {
            foreach (var g in _layoutGlyphs)
                if (g.Type != RunType.Symbol) canvas.DrawText(g.Text, g.X + 1f, g.Y + 1f, _shadowFont, _shadowPaint);
        }

        foreach (var g in _layoutGlyphs)
        {
            switch (g.Type)
            {
                case RunType.Normal: canvas.DrawText(g.Text, g.X, g.Y, _normalFont, _normalPaint); break;
                case RunType.Keyword: canvas.DrawText(g.Text, g.X, g.Y, _keywordFont, _keywordPaint); break;
                case RunType.Symbol: ManaSvgCache.DrawSymbol(canvas, g.Text, g.X, g.Y - _symbolSize * 0.8f, _symbolSize); break;
            }
        }
        canvas.Restore();
    }
}