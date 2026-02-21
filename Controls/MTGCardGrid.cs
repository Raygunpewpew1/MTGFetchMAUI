using MTGFetchMAUI.Models;
using MTGFetchMAUI.Services;
using SkiaSharp;
using SkiaSharp.Views.Maui;
using SkiaSharp.Views.Maui.Controls;
using System.Diagnostics;

namespace MTGFetchMAUI.Controls;

/// <summary>
/// GPU-accelerated card grid using SkiaSharp.
/// Virtualized using a sticky viewport for maximum performance.
/// </summary>
public class MTGCardGrid : Grid
{
    // ── Constants ──────────────────────────────────────────────────────
    private const float CardImageRatio = 1.3968f;
    private const float LabelHeight = 42f;
    private const float MinCardWidth = 100f;
    private const float CardSpacing = 8f;
    private const int OffscreenBuffer = 40;
    private const int CleanupIntervalMs = 2000;

    private static readonly SKPoint[] LabelRadii = [
        new SKPoint(0, 0), new SKPoint(0, 0),
        new SKPoint(8, 8), new SKPoint(8, 8)
    ];

    private SKPath? _borderPath;
    private readonly float[] _dashArray = new float[2];

    // ── Controls ───────────────────────────────────────────────────────
    private readonly SKCanvasView _canvas;
    private readonly BoxView _scrollSpacer;

    // ── State ──────────────────────────────────────────────────────────
    private readonly List<GridCardData> _cards = [];
    private readonly object _cardsLock = new();
    private readonly object _renderLock = new();

    // Layout metrics
    private int _columnCount;
    private float _cardWidth;
    private float _cardHeight;
    private float _totalHeight;
    private float _scrollOffset;

    // Visible range
    private int _visibleStart;
    private int _visibleEnd;

    // Interaction
    private int _hoveredIndex = -1;
    private int _pressedIndex = -1;
    private SKPoint _pressPoint;
    private bool _pointerMoved;
    private IDispatcherTimer? _longPressTimer;
    private float _longPressProgress; // 0 to 1

    // Animation
    private float _shimmerPhase;
    private readonly Stopwatch _animationStopwatch = new();
    private long _lastFrameTime;
    private IDispatcherTimer? _cleanupTimer;
    private IDispatcherTimer? _animationTimer;

    // ── Cached Skia Objects ────────────────────────────────────────────
    private SKPaint? _bgPaint;
    private SKPaint? _labelBgPaint;
    private SKPaint? _namePaint;
    private SKPaint? _setPaint;
    private SKPaint? _hoverPaint;
    private SKPaint? _chipBgPaint;
    private SKPaint? _chipTextPaint;
    private SKPaint? _badgeBgPaint;
    private SKPaint? _badgeTextPaint;
    private SKPaint? _shimmerBasePaint;
    private SKPaint? _shimmerGradientPaint;
    private SKPaint? _ripplePaint;
    private SKPaint? _imgPaint;
    private SKFont? _nameFont;
    private SKFont? _setFont;
    private SKFont? _chipFont;
    private SKFont? _badgeFont;
    private SKTypeface? _badgeTypeface;
    private SKRoundRect? _cardRoundRect;
    private SKRoundRect? _imageRoundRect;
    private SKRoundRect? _labelRoundRect;
    private readonly SKSamplingOptions _sampling = new(SKFilterMode.Linear, SKMipmapMode.Linear);

    // ── Events ─────────────────────────────────────────────────────────
    public event Action<string>? CardClicked;
    public event Action<string>? CardLongPressed;
    public event Action<int, int>? VisibleRangeChanged;

    // ── Properties ─────────────────────────────────────────────────────
    public int CardCount { get { lock (_cardsLock) return _cards.Count; } }
    public float TotalContentHeight => _totalHeight;
    public int ColumnCount => _columnCount;
    public float CardCellHeight => _cardHeight + CardSpacing;


    // ── Constructor ────────────────────────────────────────────────────
    public MTGCardGrid()
    {
        _scrollSpacer = new BoxView { Color = Colors.Transparent, WidthRequest = 100 };

        // Use SKCanvasView for stability on Android (prevents GL context loss)
        _canvas = new SKCanvasView
        {
            EnableTouchEvents = true,
            HorizontalOptions = LayoutOptions.Fill,
            VerticalOptions = LayoutOptions.Start
        };
        _canvas.PaintSurface += OnPaintSurface;
        _canvas.Touch += OnTouch;

        Children.Add(_scrollSpacer);
        Children.Add(_canvas);

        _animationStopwatch.Start();

        Loaded += (s, e) => SyncLayout();
    }

    private void SyncLayout()
    {
        CalculateLayout();
        _canvas.TranslationY = _scrollOffset;
        _canvas.InvalidateSurface();
    }

    /// <summary>
    /// Forces a redraw of the grid surface. Useful when returning from background/tab switch.
    /// </summary>
    public void ForceRedraw()
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            _canvas.InvalidateSurface();
        });
    }

    public void StartTimers()
    {
        if (_cleanupTimer != null) return;

        _cleanupTimer = Dispatcher.CreateTimer();
        _cleanupTimer.Interval = TimeSpan.FromMilliseconds(CleanupIntervalMs);
        _cleanupTimer.Tick += (_, _) => CleanupOffScreenBitmaps();
        _cleanupTimer.Start();

        _animationTimer = Dispatcher.CreateTimer();
        _animationTimer.Interval = TimeSpan.FromMilliseconds(16); // ~60 FPS
        _animationTimer.Tick += (_, _) => UpdateAnimations();
        _animationTimer.Start();
    }

    public void StopTimers()
    {
        _cleanupTimer?.Stop();
        _cleanupTimer = null;
        _animationTimer?.Stop();
        _animationTimer = null;
        _longPressTimer?.Stop();
        _longPressTimer = null;
    }

    private void UpdateAnimations()
    {
        if (CardCount == 0 || _visibleEnd < 0) return;

        long currentTime = _animationStopwatch.ElapsedMilliseconds;
        float deltaTime = (currentTime - _lastFrameTime) / 1000f;
        _lastFrameTime = currentTime;

        if (deltaTime > 0.1f) deltaTime = 0.016f; // Cap delta to avoid jumps

        _shimmerPhase += deltaTime * 0.8f; // Slightly faster shimmer
        if (_shimmerPhase > 1f) _shimmerPhase -= 1f;

        bool needsInvalidate = false;

        if (_pressedIndex >= 0 && _longPressTimer != null)
        {
            _longPressProgress = Math.Min(1f, _longPressProgress + deltaTime * 2f);
            needsInvalidate = true;
        }

        lock (_cardsLock)
        {
            int start = _visibleStart;
            int end = _visibleEnd;
            int count = _cards.Count;

            for (int i = start; i <= end && i < count; i++)
            {
                var card = _cards[i];

                // Shimmer always needs redraw if images missing in visible range
                if (card.Image == null && !string.IsNullOrEmpty(card.ScryfallId))
                {
                    needsInvalidate = true;
                }

                // Fade animation
                if (card.FirstVisible && card.AnimationProgress < 1f)
                {
                    card.AnimationProgress = Math.Min(1f, card.AnimationProgress + deltaTime * 4f);
                    needsInvalidate = true;
                }
            }
        }

        if (needsInvalidate)
            _canvas.InvalidateSurface();
    }

    // ── Public API ─────────────────────────────────────────────────────

    public void SetCards(Card[] cards)
    {
        lock (_cardsLock)
        {
            DisposeAllImages();
            _cards.Clear();
            foreach (var c in cards)
                _cards.Add(GridCardData.FromCard(c));
        }
        _scrollOffset = 0;
        _canvas.TranslationY = 0;
        CalculateLayout();
        _canvas.InvalidateSurface();
    }

    public void SetCollection(CollectionItem[] items)
    {
        lock (_cardsLock)
        {
            DisposeAllImages();
            _cards.Clear();
            foreach (var item in items)
                _cards.Add(GridCardData.FromCard(item.Card, item.Quantity));
        }
        _scrollOffset = 0;
        _canvas.TranslationY = 0;
        CalculateLayout();
        _canvas.InvalidateSurface();
    }

    public void AddCards(Card[] cards)
    {
        lock (_cardsLock)
        {
            foreach (var c in cards)
                _cards.Add(GridCardData.FromCard(c));
        }
        CalculateLayout();
        _canvas.InvalidateSurface();
    }

    public async Task AddCardsAsync(IEnumerable<Card> newCards, int chunkSize = 50)
    {
        // Convert to an array to avoid multiple enumerations
        var cardArray = newCards.ToArray();
        bool layoutNeedsUpdate = false;

        for (int i = 0; i < cardArray.Length; i += chunkSize)
        {
            // 1. Process the expensive mapping outside the lock
            var chunk = cardArray
                .Skip(i)
                .Take(chunkSize)
                .Select(c => GridCardData.FromCard(c))
                .ToList();

            // 2. Briefly lock to append the chunk, then immediately release
            lock (_cardsLock)
            {
                _cards.AddRange(chunk);
                layoutNeedsUpdate = true;
            }

            // 3. Yield control. This is the magic line. 
            // It tells the thread pool: "Let the UI thread do its 16ms draw loop right now."
            await Task.Yield();
        }

        // 4. After everything is loaded, recalculate the layout on the main thread
        if (layoutNeedsUpdate)
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                CalculateLayout();
                _canvas.InvalidateSurface();
            });
        }
    }

    public void ClearCards()
    {
        lock (_cardsLock)
        {
            DisposeAllImages();
            _cards.Clear();
        }
        _scrollOffset = 0;
        _canvas.TranslationY = 0;
        _visibleStart = 0;
        _visibleEnd = -1;
        CalculateLayout();
        _canvas.InvalidateSurface();
    }

    public void SetScrollOffset(float offset)
    {
        if (float.IsNaN(offset) || float.IsInfinity(offset)) return;

        _scrollOffset = offset;
        _canvas.TranslationY = offset;

        var oldStart = _visibleStart;
        var oldEnd = _visibleEnd;
        CalculateVisibleRange();

        if (oldStart != _visibleStart || oldEnd != _visibleEnd)
            VisibleRangeChanged?.Invoke(_visibleStart, _visibleEnd);

        _canvas.InvalidateSurface();
    }

    public void UpdateCardImage(string uuid, SKImage image, ImageQuality quality)
    {
        bool consumed = false;
        lock (_cardsLock)
        {
            for (int i = 0; i < _cards.Count; i++)
            {
                if (_cards[i].UUID == uuid)
                {
                    if (_cards[i].Quality < quality || _cards[i].Image == null)
                    {
                        _cards[i].Image?.Dispose();
                        _cards[i].Image = image;
                        _cards[i].Quality = quality;
                        _cards[i].IsLoading = false;
                        _cards[i].IsUpgrading = false;
                        if (!_cards[i].FirstVisible)
                        {
                            _cards[i].FirstVisible = true;
                            _cards[i].AnimationProgress = 0f;
                        }
                        consumed = true;
                    }
                    break;
                }
            }
        }

        if (!consumed)
            image.Dispose();

        _canvas.InvalidateSurface();
    }

    public void UpdateCardPrices(string uuid, CardPriceData prices)
    {
        lock (_cardsLock)
        {
            for (int i = 0; i < _cards.Count; i++)
            {
                if (_cards[i].UUID == uuid)
                {
                    _cards[i].PriceData = prices;
                    _cards[i].CachedDisplayPrice = "";
                    break;
                }
            }
        }
        _canvas.InvalidateSurface();
    }

    public void UpdateCardPricesBulk(Dictionary<string, CardPriceData> pricesMap)
    {
        bool anyUpdated = false;
        lock (_cardsLock)
        {
            foreach (var card in _cards)
            {
                if (pricesMap.TryGetValue(card.UUID, out var prices))
                {
                    card.PriceData = prices;
                    card.CachedDisplayPrice = "";
                    anyUpdated = true;
                }
            }
        }
        if (anyUpdated)
            _canvas.InvalidateSurface();
    }

    public GridCardData? GetCardAt(int index)
    {
        lock (_cardsLock)
        {
            if (index >= 0 && index < _cards.Count)
                return _cards[index];
        }
        return null;
    }

    public (int start, int end) GetVisibleRange() => (_visibleStart, _visibleEnd);

    public List<(int index, GridCardData card)> GetCardsNeedingImages(ImageQuality minQuality = ImageQuality.Small)
    {
        var result = new List<(int, GridCardData)>();
        lock (_cardsLock)
        {
            int start = Math.Max(0, _visibleStart);
            int end = Math.Min(_cards.Count - 1, _visibleEnd);

            if (end < start || _cards.Count == 0) return result;

            // Center-outward ordering
            int mid = (start + end) / 2;
            int left = mid, right = mid + 1;
            while (left >= start || right <= end)
            {
                if (left >= start)
                {
                    var c = _cards[left];
                    if (c.NeedsImage || c.NeedsUpgrade(minQuality))
                        result.Add((left, c));
                    left--;
                }
                if (right <= end)
                {
                    var c = _cards[right];
                    if (c.NeedsImage || c.NeedsUpgrade(minQuality))
                        result.Add((right, c));
                    right++;
                }
            }
        }
        return result;
    }

    public void MarkLoading(string uuid)
    {
        lock (_cardsLock)
        {
            for (int i = 0; i < _cards.Count; i++)
            {
                if (_cards[i].UUID == uuid) { _cards[i].IsLoading = true; break; }
            }
        }
    }

    public void MarkUpgrading(string uuid)
    {
        lock (_cardsLock)
        {
            for (int i = 0; i < _cards.Count; i++)
            {
                if (_cards[i].UUID == uuid) { _cards[i].IsUpgrading = true; break; }
            }
        }
    }

    // ── Layout ─────────────────────────────────────────────────────────

    private void CalculateLayout()
    {
        float parentWidth = (float)Width;
        if (parentWidth <= 0 && Parent is View v) parentWidth = (float)v.Width;
        if (parentWidth <= 0) parentWidth = 360f;

        float availWidth = parentWidth - 20f; // 10px padding each side
        _columnCount = Math.Max(1, (int)((availWidth - CardSpacing) / (MinCardWidth + CardSpacing)));
        _cardWidth = (availWidth - CardSpacing * (_columnCount + 1)) / _columnCount;
        _cardHeight = _cardWidth * CardImageRatio + LabelHeight;

        int count;
        lock (_cardsLock) count = _cards.Count;
        int rowCount = (int)Math.Ceiling((double)count / _columnCount);
        _totalHeight = rowCount * (_cardHeight + CardSpacing) + CardSpacing + 50f;

        _scrollSpacer.HeightRequest = _totalHeight;

        float viewportHeight = (float)(Parent is View vp ? vp.Height : Height);
        if (viewportHeight <= 0) viewportHeight = 1000f;
        _canvas.HeightRequest = viewportHeight;

        CalculateVisibleRange();
    }

    private void CalculateVisibleRange()
    {
        float viewportHeight = (float)(Parent is View v ? v.Height : Height);
        if (viewportHeight <= 0) viewportHeight = 1000f;

        float rowHeight = _cardHeight + CardSpacing;
        if (rowHeight <= 0)
        {
            _visibleStart = 0;
            _visibleEnd = -1;
            return;
        }

        int count;
        lock (_cardsLock) count = _cards.Count;

        if (count == 0)
        {
            _visibleStart = 0;
            _visibleEnd = -1;
            return;
        }

        float effectiveOffset = Math.Max(0, _scrollOffset);

        int firstRow = Math.Max(0, (int)((effectiveOffset - CardSpacing) / rowHeight));
        int lastRow = (int)((effectiveOffset + viewportHeight + CardSpacing) / rowHeight) + 1;

        _visibleStart = Math.Max(0, Math.Min(count - 1, firstRow * _columnCount));
        _visibleEnd = Math.Max(0, Math.Min(count - 1, (lastRow + 1) * _columnCount - 1));

        if (_visibleStart >= count)
        {
            _visibleStart = 0;
            _visibleEnd = -1;
        }
    }

    // ── Painting ───────────────────────────────────────────────────────

    private void EnsureResources()
    {
        if (_bgPaint != null) return;

        _bgPaint = new SKPaint { IsAntialias = true };
        _labelBgPaint = new SKPaint { IsAntialias = true, Color = new SKColor(25, 25, 25) };
        _namePaint = new SKPaint { IsAntialias = true, Color = new SKColor(240, 240, 240) };
        _setPaint = new SKPaint { IsAntialias = true, Color = new SKColor(160, 160, 160) };
        _hoverPaint = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 2f,
            Color = new SKColor(100, 149, 237, 40)
        };
        _chipBgPaint = new SKPaint { IsAntialias = true, Color = new SKColor(0, 0, 0, 180) };
        _chipTextPaint = new SKPaint { IsAntialias = true, Color = SKColors.White };
        _badgeBgPaint = new SKPaint { IsAntialias = true, Color = new SKColor(220, 50, 50) };
        _badgeTextPaint = new SKPaint { IsAntialias = true, Color = SKColors.White };
        _shimmerBasePaint = new SKPaint { Color = new SKColor(40, 40, 40) };
        _shimmerGradientPaint = new SKPaint { IsAntialias = true };
        _ripplePaint = new SKPaint { IsAntialias = true, Color = new SKColor(255, 255, 255, 30) };
        _imgPaint = new SKPaint { IsAntialias = true, Color = SKColors.White };

        _nameFont = new SKFont(SKTypeface.Default, 12f);
        _setFont = new SKFont(SKTypeface.Default, 10f);
        _chipFont = new SKFont(SKTypeface.Default, 10f);

        _badgeTypeface ??= SKTypeface.FromFamilyName(null, SKFontStyle.Bold);
        _badgeFont = new SKFont(_badgeTypeface, 11f);

        _cardRoundRect ??= new SKRoundRect();
        _imageRoundRect ??= new SKRoundRect();
        _labelRoundRect ??= new SKRoundRect();

        if (_shimmerGradientPaint!.Shader == null)
        {
            // Create a normalized shader (0 to 100 on the X axis) ONCE.
            _shimmerGradientPaint.Shader = SKShader.CreateLinearGradient(
                new SKPoint(0, 0),
                new SKPoint(100, 0),
                [SKColors.Transparent, new SKColor(255, 255, 255, 60), SKColors.Transparent],
                [0f, 0.5f, 1f],
                SKShaderTileMode.Clamp);
        }
    }

    private void OnPaintSurface(object? sender, SKPaintSurfaceEventArgs e)
    {
        lock (_renderLock)
        {
            var canvas = e.Surface.Canvas;
            var info = e.Info;
            canvas.Clear(new SKColor(18, 18, 18));

            if (_cardWidth <= 0) CalculateLayout();
            EnsureResources();

            float scale = info.Width / (float)(Width > 0 ? Width : 360);

            canvas.Save();
            canvas.Scale(scale);

            // Offset by scroll to draw the correct slice
            canvas.Translate(0, -_scrollOffset);

            lock (_cardsLock)
            {
                for (int i = _visibleStart; i <= _visibleEnd && i < _cards.Count; i++)
                {
                    var card = _cards[i];
                    var rect = GetCardRect(i);
                    DrawCard(canvas, card, rect, i);
                }
            }

            canvas.Restore();
        }
    }

    private SKRect GetCardRect(int index)
    {
        int row = index / _columnCount;
        int col = index % _columnCount;
        float x = CardSpacing + col * (_cardWidth + CardSpacing) + 10f;
        float y = CardSpacing + row * (_cardHeight + CardSpacing);
        return new SKRect(x, y, x + _cardWidth, y + _cardHeight);
    }

    private void DrawCard(SKCanvas canvas, GridCardData card, SKRect rect, int index)
    {
        byte alphaByte = (byte)(card.AnimationProgress * 255);
        if (!card.FirstVisible) alphaByte = 255;

        // Card background
        _bgPaint!.Color = new SKColor(30, 30, 30, alphaByte);
        _cardRoundRect!.SetRect(rect, 8f, 8f);
        canvas.DrawRoundRect(_cardRoundRect, _bgPaint);

        float imageHeight = _cardWidth * CardImageRatio;
        var imageRect = new SKRect(rect.Left, rect.Top, rect.Right, rect.Top + imageHeight);

        if (card.Image != null)
        {
            DrawCardImage(canvas, card, imageRect, alphaByte, index);
        }
        else
        {
            DrawShimmer(canvas, imageRect, alphaByte);
        }

        // Draw price chip if available (always, even if image is null/shimmering)
        string price = card.GetDisplayPrice();
        if (!string.IsNullOrEmpty(price))
        {
            float tw = _chipFont!.MeasureText(price);
            float chipX = imageRect.MidX - (tw + 12f) / 2f;
            float chipY = imageRect.Bottom - 22f;
            _chipBgPaint!.Color = new SKColor(0, 0, 0, (byte)(180 * alphaByte / 255));
            _chipTextPaint!.Color = new SKColor(255, 255, 255, alphaByte);
            canvas.DrawRoundRect(chipX, chipY, tw + 12f, 18f, 9f, 9f, _chipBgPaint!);
            canvas.DrawText(price, chipX + 6f, chipY + 13f, _chipFont, _chipTextPaint!);
        }

        // Label area
        float labelY = rect.Top + imageHeight;
        var labelRect = new SKRect(rect.Left, labelY, rect.Right, rect.Bottom);

        _labelBgPaint!.Color = new SKColor(25, 25, 25, alphaByte);
        _labelRoundRect!.SetRectRadii(labelRect, LabelRadii);
        canvas.DrawRoundRect(_labelRoundRect, _labelBgPaint);

        // Card name
        _namePaint!.Color = new SKColor(240, 240, 240, alphaByte);
        if (card.TruncatedName == "" || card.LastKnownCardWidth != _cardWidth)
        {
            card.TruncatedName = TruncateText(card.Name, _nameFont!, _cardWidth - 8f);
            card.LastKnownCardWidth = _cardWidth;
        }
        canvas.DrawText(card.TruncatedName, rect.Left + 4f, labelY + 16f, _nameFont!, _namePaint);

        // Set info
        _setPaint!.Color = new SKColor(160, 160, 160, alphaByte);
        //  string setInfo = $"{card.SetCode} #{card.Number}";
        canvas.DrawText(card.DisplaySetInfo, rect.Left + 4f, labelY + 32f, _setFont!, _setPaint);

        if (index == _hoveredIndex)
            canvas.DrawRoundRect(_cardRoundRect, _hoverPaint!);
    }

    private void DrawCardImage(SKCanvas canvas, GridCardData card, SKRect imageRect, byte alpha, int index)
    {
        canvas.Save();

        if (index == _pressedIndex && _longPressProgress > 0)
        {
            float grow = 1f + (_longPressProgress * 0.05f);
            canvas.Scale(grow, grow, imageRect.MidX, imageRect.MidY);
        }

        // Use a simpler clip if possible, or at least avoid recreating path every frame
        // For now, we keep the round corners but we could optimize this by caching the path per card size
        _imageRoundRect!.SetRect(imageRect, 8f, 8f);
        canvas.ClipRoundRect(_imageRoundRect, antialias: true);

        _imgPaint!.Color = new SKColor(255, 255, 255, alpha);
        canvas.DrawImage(card.Image!, imageRect, _sampling, _imgPaint);

        canvas.Restore();

        if (card.Quantity > 0)
        {
            string qtyStr = card.Quantity.ToString();
            float tw = _badgeFont!.MeasureText(qtyStr);
            float bw = Math.Max(20f, tw + 10f);
            float bx = imageRect.Right - bw - 4f;
            float by = imageRect.Top + 4f;
            canvas.DrawRoundRect(bx, by, bw, 20f, 10f, 10f, _badgeBgPaint!);
            canvas.DrawText(qtyStr, bx + (bw - tw) / 2f, by + 15f, _badgeFont, _badgeTextPaint!);
        }

        if (index == _pressedIndex)
        {
            if (_longPressProgress > 0)
            {
                using var progressPaint = new SKPaint
                {
                    IsAntialias = true,
                    Style = SKPaintStyle.Stroke,
                    StrokeWidth = 4f,
                    Color = new SKColor(255, 255, 255, (byte)(100 + _longPressProgress * 155))
                };

                // Reuse the cached path instead of creating a new one
                _borderPath ??= new SKPath();
                _borderPath.Rewind();
                _borderPath.AddRoundRect(_imageRoundRect);

                canvas.Save();
                canvas.ClipPath(_borderPath);

                // Overlay ripple
                _ripplePaint!.Color = new SKColor(255, 255, 255, (byte)(30 + _longPressProgress * 40));
                canvas.DrawRect(imageRect, _ripplePaint);

                canvas.Restore();

                // Animated border
                if (_longPressProgress < 1f)
                {
                    // Mathematically calculate the perimeter of the rounded rectangle
                    // Formula: 2*W + 2*H - 8*Radius + 2*PI*Radius
                    float radius = 8f;
                    float length = (2 * imageRect.Width) + (2 * imageRect.Height) - (8 * radius) + (float)(2 * Math.PI * radius);

                    // Update the pre-allocated array (zero allocations)
                    _dashArray[0] = length * _longPressProgress;
                    _dashArray[1] = length;

                    // Apply dash effect using the cached array
                    using var dash = SKPathEffect.CreateDash(_dashArray, 0);
                    progressPaint.PathEffect = dash;
                    canvas.DrawPath(_borderPath, progressPaint);
                }
                else
                {
                    canvas.DrawPath(_borderPath, progressPaint);
                }
            }
            else
            {
                canvas.DrawRoundRect(_imageRoundRect, _ripplePaint!);
            }
        }
    }

    private void DrawShimmer(SKCanvas canvas, SKRect rect, byte alpha)
    {
        canvas.Save();
        _imageRoundRect!.SetRect(rect, 8f, 8f);
        canvas.ClipRoundRect(_imageRoundRect, antialias: true);

        // 1. Draw solid base background
        _shimmerBasePaint!.Color = new SKColor(40, 40, 40, alpha);
        canvas.DrawRect(rect, _shimmerBasePaint);

        // 2. Calculate animation translation based on phase
        float shimmerWidth = rect.Width * 1.5f;
        float shimmerX = rect.Left - rect.Width + (rect.Width * 3f) * _shimmerPhase;

        // 3. Translate and scale the canvas so our 100px static shader fits the card
        canvas.Translate(shimmerX, rect.Top);
        canvas.Scale(shimmerWidth / 100f, 1f);

        // 4. Draw a rect over the local normalized coordinates (0 to 100)
        canvas.DrawRect(0, 0, 100, rect.Height, _shimmerGradientPaint);

        canvas.Restore();
    }

    // ── Touch Handling ─────────────────────────────────────────────────

    private void OnTouch(object? sender, SKTouchEventArgs e)
    {
        float scale = (float)(_canvas.CanvasSize.Width / (Width > 0 ? Width : 360));

        // Touch coordinates are relative to the view.
        // Our view is sticky at TranslateY = _scrollOffset.
        // But Skia is translated by -_scrollOffset internally.
        // So we need to add _scrollOffset to the Y touch coordinate to match the drawn cards.
        float x = e.Location.X / scale;
        float y = (e.Location.Y / scale) + _scrollOffset;

        switch (e.ActionType)
        {
            case SKTouchAction.Pressed:
                _pressPoint = new SKPoint(x, y);
                _pointerMoved = false;
                _pressedIndex = HitTestCard(x, y);
                _longPressProgress = 0;

                if (_pressedIndex >= 0)
                {
                    _longPressTimer?.Stop();
                    _longPressTimer = Dispatcher.CreateTimer();
                    _longPressTimer.Interval = TimeSpan.FromMilliseconds(500);
                    _longPressTimer.Tick += (s, ev) =>
                    {
                        if (!_pointerMoved && _pressedIndex >= 0)
                        {
                            string? uuid = null;
                            lock (_cardsLock)
                            {
                                if (_pressedIndex < _cards.Count)
                                    uuid = _cards[_pressedIndex].UUID;
                            }
                            if (uuid != null)
                            {
                                CardLongPressed?.Invoke(uuid);
                                _pressedIndex = -1; // Prevent click on release
                                _longPressProgress = 0;
                            }
                        }
                        _longPressTimer?.Stop();
                        _longPressTimer = null;
                        _canvas.InvalidateSurface();
                    };
                    _longPressTimer.Start();
                }

                _canvas.InvalidateSurface();
                e.Handled = true;
                break;

            case SKTouchAction.Moved:
                float dx = x - _pressPoint.X;
                float dy = y - _pressPoint.Y;
                if (Math.Sqrt(dx * dx + dy * dy) > 15)
                {
                    if (!_pointerMoved)
                    {
                        _pointerMoved = true;
                        _longPressTimer?.Stop();
                        _longPressTimer = null;
                        _longPressProgress = 0;
                    }
                }
                e.Handled = true;
                break;

            case SKTouchAction.Released:
                _longPressTimer?.Stop();
                _longPressTimer = null;

                if (!_pointerMoved && _pressedIndex >= 0)
                {
                    GridCardData? card;
                    lock (_cardsLock)
                    {
                        card = _pressedIndex < _cards.Count ? _cards[_pressedIndex] : null;
                    }
                    if (card != null)
                        CardClicked?.Invoke(card.UUID);
                }
                _pressedIndex = -1;
                _longPressProgress = 0;
                _canvas.InvalidateSurface();
                e.Handled = true;
                break;

            case SKTouchAction.Cancelled:
                _longPressTimer?.Stop();
                _longPressTimer = null;
                _pressedIndex = -1;
                _longPressProgress = 0;
                _canvas.InvalidateSurface();
                e.Handled = true;
                break;
        }
    }

    private int HitTestCard(float x, float y)
    {
        // Adjust for the left/top padding
        float adjustedX = x - 10f;
        float adjustedY = y - CardSpacing;

        if (adjustedX < 0 || adjustedY < 0) return -1;

        // Calculate row and column using integer division
        int col = (int)(adjustedX / (_cardWidth + CardSpacing));
        int row = (int)(adjustedY / (_cardHeight + CardSpacing));

        if (col < 0 || col >= _columnCount) return -1;

        int index = (row * _columnCount) + col;

        lock (_cardsLock)
        {
            if (index >= 0 && index < _cards.Count)
                return index;
        }

        return -1;
    }

    // ── Cleanup ────────────────────────────────────────────────────────

    public void Dispose()
    {
        StopTimers();
        DisposeAllImages();
        DisposeResources();
        _canvas.PaintSurface -= OnPaintSurface;
        _canvas.Touch -= OnTouch;
    }

    private void CleanupOffScreenBitmaps()
    {
        lock (_cardsLock)
        {
            if (_visibleEnd < 0 || _cards.Count == 0) return;

            int bufferStart = Math.Max(0, _visibleStart - OffscreenBuffer);
            int bufferEnd = Math.Min(_cards.Count - 1, _visibleEnd + OffscreenBuffer);

            for (int i = 0; i < _cards.Count; i++)
            {
                if (i < bufferStart || i > bufferEnd)
                {
                    if (_cards[i].Image != null)
                    {
                        _cards[i].Image?.Dispose();
                        _cards[i].Image = null;
                        _cards[i].Quality = ImageQuality.None;
                        _cards[i].IsLoading = false;
                        _cards[i].IsUpgrading = false;
                        _cards[i].FirstVisible = false;
                    }
                }
            }
        }
    }

    private void DisposeAllImages()
    {
        foreach (var card in _cards)
        {
            card.Image?.Dispose();
            card.Image = null;
        }
    }

    private void DisposeResources()
    {
        StopTimers();
        lock (_renderLock)
        {
            _bgPaint?.Dispose();
            _bgPaint = null;
            _labelBgPaint?.Dispose();
            _labelBgPaint = null;
            _namePaint?.Dispose();
            _namePaint = null;
            _setPaint?.Dispose();
            _setPaint = null;
            _hoverPaint?.Dispose();
            _hoverPaint = null;
            _chipBgPaint?.Dispose();
            _chipBgPaint = null;
            _chipTextPaint?.Dispose();
            _chipTextPaint = null;
            _badgeBgPaint?.Dispose();
            _badgeBgPaint = null;
            _badgeTextPaint?.Dispose();
            _badgeTextPaint = null;
            _shimmerBasePaint?.Dispose();
            _shimmerBasePaint = null;
            _shimmerGradientPaint?.Dispose();
            _shimmerGradientPaint = null;
            _ripplePaint?.Dispose();
            _ripplePaint = null;
            _imgPaint?.Dispose();
            _imgPaint = null;

            _nameFont?.Dispose();
            _nameFont = null;
            _setFont?.Dispose();
            _setFont = null;
            _chipFont?.Dispose();
            _chipFont = null;
            _badgeFont?.Dispose();
            _badgeFont = null;
            _badgeTypeface?.Dispose();
            _badgeTypeface = null;

            _cardRoundRect?.Dispose();
            _cardRoundRect = null;
            _imageRoundRect?.Dispose();
            _imageRoundRect = null;
            _labelRoundRect?.Dispose();
            _labelRoundRect = null;
        }
    }

    // ── Helpers ─────────────────────────────────────────────────────────

    private static string TruncateText(string text, SKFont font, float maxWidth)
    {
        if (font.MeasureText(text) <= maxWidth) return text;
        for (int len = text.Length - 1; len > 0; len--)
        {
            string truncated = text[..len] + "...";
            if (font.MeasureText(truncated) <= maxWidth)
                return truncated;
        }
        return "...";
    }

    protected override void OnSizeAllocated(double width, double height)
    {
        base.OnSizeAllocated(width, height);
        if (width > 0)
            CalculateLayout();
    }

    public void InvalidateSurface() => _canvas.InvalidateSurface();
}
