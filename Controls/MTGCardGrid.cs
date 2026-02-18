using MTGFetchMAUI.Models;
using MTGFetchMAUI.Services;
using SkiaSharp;
using SkiaSharp.Views.Maui;
using SkiaSharp.Views.Maui.Controls;

namespace MTGFetchMAUI.Controls;

/// <summary>
/// GPU-accelerated card grid using SkiaSharp.
/// Port of TCustomCardGrid from CustomCardGrid.pas.
/// Renders cards directly on an SKCanvas for maximum performance.
/// </summary>
public class MTGCardGrid : SKCanvasView
{
    // ── Constants ──────────────────────────────────────────────────────
    private const float CardImageRatio = 1.3968f;
    private const float LabelHeight = 42f;
    private const float MinCardWidth = 100f;
    private const float CardSpacing = 8f;
    private const int OffscreenBuffer = 15;
    private const int CleanupIntervalMs = 2000;
    private const float ShimmerSpeed = 1.5f;

    // ── State ──────────────────────────────────────────────────────────
    private readonly List<GridCardData> _cards = [];
    private readonly object _cardsLock = new();

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

    // Animation
    private float _shimmerPhase;
    private IDispatcherTimer? _cleanupTimer;
    private IDispatcherTimer? _shimmerTimer;

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
        EnableTouchEvents = true;
        Touch += OnTouch;
    }

    public void StartTimers()
    {
        if (_cleanupTimer != null) return;

        _cleanupTimer = Dispatcher.CreateTimer();
        _cleanupTimer.Interval = TimeSpan.FromMilliseconds(CleanupIntervalMs);
        _cleanupTimer.Tick += (_, _) => CleanupOffScreenBitmaps();
        _cleanupTimer.Start();

        _shimmerTimer = Dispatcher.CreateTimer();
        _shimmerTimer.Interval = TimeSpan.FromMilliseconds(50);
        _shimmerTimer.Tick += (_, _) =>
        {
            _shimmerPhase += 0.03f;
            if (_shimmerPhase > 1f) _shimmerPhase = 0f;

            // Only invalidate if there are loading cards or animations in progress
            bool needsInvalidate;
            lock (_cardsLock)
            {
                needsInvalidate = false;
                for (int i = _visibleStart; i <= _visibleEnd && i < _cards.Count; i++)
                {
                    var card = _cards[i];
                    if (card.Image == null || (card.FirstVisible && card.AnimationProgress < 1f))
                    {
                        needsInvalidate = true;
                        break;
                    }
                }
            }
            if (needsInvalidate) InvalidateSurface();
        };
        _shimmerTimer.Start();
    }

    public void StopTimers()
    {
        _cleanupTimer?.Stop();
        _cleanupTimer = null;
        _shimmerTimer?.Stop();
        _shimmerTimer = null;
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
        CalculateLayout();
        InvalidateSurface();
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
        CalculateLayout();
        InvalidateSurface();
    }

    public void AddCards(Card[] cards)
    {
        lock (_cardsLock)
        {
            foreach (var c in cards)
                _cards.Add(GridCardData.FromCard(c));
        }
        CalculateLayout();
        InvalidateSurface();
    }

    public void ClearCards()
    {
        lock (_cardsLock)
        {
            DisposeAllImages();
            _cards.Clear();
        }
        _scrollOffset = 0;
        _visibleStart = 0;
        _visibleEnd = -1;
        CalculateLayout();
        InvalidateSurface();
    }

    public void SetScrollOffset(float offset)
    {
        _scrollOffset = offset;
        var oldStart = _visibleStart;
        var oldEnd = _visibleEnd;
        CalculateVisibleRange();
        if (oldStart != _visibleStart || oldEnd != _visibleEnd)
            VisibleRangeChanged?.Invoke(_visibleStart, _visibleEnd);
        InvalidateSurface();
    }

    public void UpdateCardImage(string uuid, SKBitmap image, ImageQuality quality)
    {
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
                    }
                    break;
                }
            }
        }
        InvalidateSurface();
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
        InvalidateSurface();
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

        HeightRequest = _totalHeight;
        CalculateVisibleRange();
    }

    private void CalculateVisibleRange()
    {
        float viewportHeight = (float)(Parent is View v ? v.Height : Height);
        if (viewportHeight <= 0) viewportHeight = 800f;

        float rowHeight = _cardHeight + CardSpacing;
        if (rowHeight <= 0) return;

        int firstRow = Math.Max(0, (int)((_scrollOffset - CardSpacing) / rowHeight) - 1);
        int lastRow = (int)((_scrollOffset + viewportHeight + CardSpacing) / rowHeight) + 1;

        int count;
        lock (_cardsLock) count = _cards.Count;

        _visibleStart = firstRow * _columnCount;
        _visibleEnd = Math.Min(count - 1, (lastRow + 1) * _columnCount - 1);
    }

    // ── Painting ───────────────────────────────────────────────────────

    protected override void OnPaintSurface(SKPaintSurfaceEventArgs e)
    {
        var canvas = e.Surface.Canvas;
        var info = e.Info;
        canvas.Clear(new SKColor(18, 18, 18)); // Dark background

        if (_cardWidth <= 0) CalculateLayout();

        float scale = info.Width / (float)(Width > 0 ? Width : 360);

        canvas.Save();
        canvas.Scale(scale);

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

    private SKRect GetCardRect(int index)
    {
        int row = index / _columnCount;
        int col = index % _columnCount;
        float x = CardSpacing + col * (_cardWidth + CardSpacing) + 10f; // 10px left padding
        float y = CardSpacing + row * (_cardHeight + CardSpacing);
        return new SKRect(x, y, x + _cardWidth, y + _cardHeight);
    }

    private void DrawCard(SKCanvas canvas, GridCardData card, SKRect rect, int index)
    {
        // Fade-in animation
        float alpha = card.FirstVisible ? Math.Min(1f, card.AnimationProgress + 0.05f) : 1f;
        if (card.FirstVisible && card.AnimationProgress < 1f)
            card.AnimationProgress = alpha;

        byte alphaByte = (byte)(alpha * 255);

        // Card background
        using var bgPaint = new SKPaint
        {
            Color = new SKColor(30, 30, 30, alphaByte),
            IsAntialias = true
        };
        canvas.DrawRoundRect(rect, 8f, 8f, bgPaint);

        // Image area
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

        // Label area
        float labelY = rect.Top + imageHeight;
        var labelRect = new SKRect(rect.Left, labelY, rect.Right, rect.Bottom);

        using var labelBgPaint = new SKPaint
        {
            Color = new SKColor(25, 25, 25, alphaByte),
            IsAntialias = true
        };
        var labelRR = new SKRoundRect();
        labelRR.SetRectRadii(labelRect,
        [
            new SKPoint(0, 0), new SKPoint(0, 0),   // top-left, top-right
            new SKPoint(8, 8), new SKPoint(8, 8)     // bottom-right, bottom-left
        ]);
        canvas.DrawRoundRect(labelRR, labelBgPaint);

        // Card name
        using var namePaint = new SKPaint
        {
            Color = new SKColor(240, 240, 240, alphaByte),
            TextSize = 12f,
            IsAntialias = true,
            Typeface = SKTypeface.Default
        };
        string displayName = TruncateText(card.Name, namePaint, _cardWidth - 8f);
        canvas.DrawText(displayName, rect.Left + 4f, labelY + 16f, namePaint);

        // Set code + number
        using var setPaint = new SKPaint
        {
            Color = new SKColor(160, 160, 160, alphaByte),
            TextSize = 10f,
            IsAntialias = true
        };
        string setInfo = $"{card.SetCode} #{card.Number}";
        canvas.DrawText(setInfo, rect.Left + 4f, labelY + 32f, setPaint);

        // Hover highlight
        if (index == _hoveredIndex)
        {
            using var hoverPaint = new SKPaint
            {
                Color = new SKColor(100, 149, 237, 40),
                Style = SKPaintStyle.Stroke,
                StrokeWidth = 2f,
                IsAntialias = true
            };
            canvas.DrawRoundRect(rect, 8f, 8f, hoverPaint);
        }
    }

    private void DrawCardImage(SKCanvas canvas, GridCardData card, SKRect imageRect, byte alpha, int index)
    {
        // Clip to rounded top corners
        canvas.Save();
        var clipPath = new SKPath();
        var topRR = new SKRoundRect();
        topRR.SetRectRadii(imageRect,
        [
            new SKPoint(8, 8), new SKPoint(8, 8),   // top-left, top-right
            new SKPoint(0, 0), new SKPoint(0, 0)     // bottom-right, bottom-left
        ]);
        clipPath.AddRoundRect(topRR);
        canvas.ClipPath(clipPath);

        using var imgPaint = new SKPaint
        {
            Color = new SKColor(255, 255, 255, alpha),
            FilterQuality = SKFilterQuality.Medium,
            IsAntialias = true
        };
        canvas.DrawBitmap(card.Image!, imageRect, imgPaint);

        canvas.Restore();
        clipPath.Dispose();

        // Price chip
        string price = card.GetDisplayPrice();
        if (!string.IsNullOrEmpty(price))
        {
            using var chipBg = new SKPaint { Color = new SKColor(0, 0, 0, 180), IsAntialias = true };
            using var chipText = new SKPaint { Color = SKColors.White, TextSize = 10f, IsAntialias = true };
            float tw = chipText.MeasureText(price);
            float chipX = imageRect.MidX - (tw + 12f) / 2f;
            float chipY = imageRect.Bottom - 22f;
            canvas.DrawRoundRect(chipX, chipY, tw + 12f, 18f, 9f, 9f, chipBg);
            canvas.DrawText(price, chipX + 6f, chipY + 13f, chipText);
        }

        // Quantity badge
        if (card.Quantity > 0)
        {
            using var badgePaint = new SKPaint { Color = new SKColor(220, 50, 50), IsAntialias = true };
            using var badgeText = new SKPaint
            {
                Color = SKColors.White, TextSize = 11f, IsAntialias = true,
                FakeBoldText = true
            };
            string qtyStr = card.Quantity.ToString();
            float bw = Math.Max(20f, badgeText.MeasureText(qtyStr) + 10f);
            float bx = imageRect.Right - bw - 4f;
            float by = imageRect.Top + 4f;
            canvas.DrawRoundRect(bx, by, bw, 20f, 10f, 10f, badgePaint);
            canvas.DrawText(qtyStr, bx + (bw - badgeText.MeasureText(qtyStr)) / 2f, by + 15f, badgeText);
        }

        // Pressed ripple effect
        if (index == _pressedIndex)
        {
            using var ripplePaint = new SKPaint
            {
                Color = new SKColor(255, 255, 255, 30),
                IsAntialias = true
            };
            canvas.DrawRoundRect(imageRect, 8f, 8f, ripplePaint);
        }
    }

    private void DrawShimmer(SKCanvas canvas, SKRect rect, byte alpha)
    {
        canvas.Save();
        var clipPath = new SKPath();
        var shimmerRR = new SKRoundRect();
        shimmerRR.SetRectRadii(rect,
        [
            new SKPoint(8, 8), new SKPoint(8, 8),
            new SKPoint(0, 0), new SKPoint(0, 0)
        ]);
        clipPath.AddRoundRect(shimmerRR);
        canvas.ClipPath(clipPath);

        // Base dark background
        using var basePaint = new SKPaint { Color = new SKColor(40, 40, 40, alpha) };
        canvas.DrawRect(rect, basePaint);

        // Shimmer gradient sweep
        float shimmerX = rect.Left + (rect.Width * 2f) * _shimmerPhase - rect.Width * 0.5f;
        using var shimmerPaint = new SKPaint { IsAntialias = true };
        shimmerPaint.Shader = SKShader.CreateLinearGradient(
            new SKPoint(shimmerX - rect.Width * 0.3f, rect.Top),
            new SKPoint(shimmerX + rect.Width * 0.3f, rect.Top),
            [new SKColor(40, 40, 40, alpha), new SKColor(60, 60, 60, alpha), new SKColor(40, 40, 40, alpha)],
            [0f, 0.5f, 1f],
            SKShaderTileMode.Clamp);
        canvas.DrawRect(rect, shimmerPaint);

        canvas.Restore();
        clipPath.Dispose();
    }

    // ── Touch Handling ─────────────────────────────────────────────────

    private void OnTouch(object? sender, SKTouchEventArgs e)
    {
        float scale = (float)(CanvasSize.Width / (Width > 0 ? Width : 360));
        float x = e.Location.X / scale;
        float y = e.Location.Y / scale + _scrollOffset;

        switch (e.ActionType)
        {
            case SKTouchAction.Pressed:
                _pressPoint = new SKPoint(x, y);
                _pointerMoved = false;
                _pressedIndex = HitTestCard(x, y);
                InvalidateSurface();
                e.Handled = true;
                break;

            case SKTouchAction.Moved:
                float dx = x - _pressPoint.X;
                float dy = y - _pressPoint.Y;
                if (Math.Sqrt(dx * dx + dy * dy) > 10)
                    _pointerMoved = true;
                e.Handled = true;
                break;

            case SKTouchAction.Released:
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
                InvalidateSurface();
                e.Handled = true;
                break;

            case SKTouchAction.Cancelled:
                _pressedIndex = -1;
                InvalidateSurface();
                e.Handled = true;
                break;
        }
    }

    private int HitTestCard(float x, float y)
    {
        lock (_cardsLock)
        {
            for (int i = _visibleStart; i <= _visibleEnd && i < _cards.Count; i++)
            {
                var rect = GetCardRect(i);
                if (rect.Contains(x, y))
                    return i;
            }
        }
        return -1;
    }

    // ── Cleanup ────────────────────────────────────────────────────────

    private void CleanupOffScreenBitmaps()
    {
        lock (_cardsLock)
        {
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

    // ── Helpers ─────────────────────────────────────────────────────────

    private static string TruncateText(string text, SKPaint paint, float maxWidth)
    {
        if (paint.MeasureText(text) <= maxWidth) return text;
        for (int len = text.Length - 1; len > 0; len--)
        {
            string truncated = text[..len] + "...";
            if (paint.MeasureText(truncated) <= maxWidth)
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
}
