using System.Collections.Immutable;
using System.Diagnostics;
using System.Threading.Channels;
using MTGFetchMAUI.Core.Layout;
using MTGFetchMAUI.Services;
using MTGFetchMAUI.Models;
using SkiaSharp;
using SkiaSharp.Views.Maui;
using SkiaSharp.Views.Maui.Controls;

namespace MTGFetchMAUI.Controls;

public class CardGrid : ContentView
{
    private readonly SKCanvasView _canvas;
    private readonly ScrollView _scrollView;
    private readonly BoxView _spacer;
    private readonly Channel<GridState> _stateChannel;
    private CancellationTokenSource? _cts;

    private ImageCacheService? _imageCache;
    private ImageDownloadService? _downloadService;

    // State
    private GridState _lastState = GridState.Empty;
    private RenderList _currentRenderList = RenderList.Empty;
    private readonly HashSet<string> _loadingImages = new();
    private readonly object _loadingLock = new();

    // Bounded download queue
    private readonly SemaphoreSlim _downloadSemaphore = new(4, 4);

    // Animation
    private float _shimmerPhase;
    private readonly Stopwatch _animationStopwatch = new();
    private bool _isLoaded;
    private long _lastFrameTime;

    // Cached Paints
    private SKPaint? _bgPaint;
    private SKPaint? _textPaint;
    private SKFont? _textFont;
    private SKPaint? _pricePaint;
    private SKFont? _priceFont;
    private SKPaint? _shimmerBasePaint;
    private SKPaint? _badgeBgPaint;
    private SKPaint? _badgeTextPaint;
    private SKFont? _badgeFont;
    private SKRoundRect? _cardRoundRect;
    private SKRoundRect? _imageRoundRect;

    // Events
    public event Action<string>? CardClicked;
    public event Action<string>? CardLongPressed;
    public event EventHandler<ScrolledEventArgs>? Scrolled;
    public event Action<int, int>? VisibleRangeChanged;

    public float ContentHeight => _currentRenderList?.TotalHeight ?? 0;

    public CardGrid()
    {
        _stateChannel = Channel.CreateBounded<GridState>(new BoundedChannelOptions(1)
        {
            FullMode = BoundedChannelFullMode.DropOldest
        });

        _canvas = new SKCanvasView
        {
            HorizontalOptions = LayoutOptions.Fill,
            VerticalOptions = LayoutOptions.Fill,
            IgnorePixelScaling = true,
            EnableTouchEvents = false,
            InputTransparent = true
        };
        _canvas.PaintSurface += OnPaintSurface;

        _spacer = new BoxView
        {
            Color = Colors.Transparent,
            HeightRequest = 100,
            HorizontalOptions = LayoutOptions.Fill,
            InputTransparent = false
        };

        _scrollView = new ScrollView
        {
            Content = _spacer,
            BackgroundColor = Colors.Transparent,
            HorizontalOptions = LayoutOptions.Fill,
            VerticalOptions = LayoutOptions.Fill
        };
        _scrollView.Scrolled += OnScrolled;

        var tapGesture = new TapGestureRecognizer();
        tapGesture.Tapped += OnTapped;
        _spacer.GestureRecognizers.Add(tapGesture);

        var pointerGesture = new PointerGestureRecognizer();
        pointerGesture.PointerPressed += OnPointerPressed;
        pointerGesture.PointerReleased += OnPointerReleased;
        pointerGesture.PointerMoved += OnPointerMoved;
        pointerGesture.PointerExited += OnPointerReleased;
        _spacer.GestureRecognizers.Add(pointerGesture);

        var grid = new Grid();
        grid.Add(_canvas);
        grid.Add(_scrollView);
        Content = grid;

        _animationStopwatch.Start();

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private bool _isProcessingUpdates;

    private void OnLoaded(object? sender, EventArgs e)
    {
        _isLoaded = true;

        if (Handler?.MauiContext != null)
        {
            _imageCache = Handler.MauiContext.Services.GetService<ImageCacheService>();
            _downloadService = Handler.MauiContext.Services.GetService<ImageDownloadService>();
        }

        if (!_isProcessingUpdates)
        {
            _cts = new CancellationTokenSource();
            _isProcessingUpdates = true;
            Task.Run(ProcessStateUpdates);
        }

        EnsureResources();
        // Reset timing for smooth start
        _lastFrameTime = _animationStopwatch.ElapsedMilliseconds;
        MainThread.BeginInvokeOnMainThread(() => _canvas.InvalidateSurface());
    }

    private void OnUnloaded(object? sender, EventArgs e)
    {
        _isLoaded = false;
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
        _isProcessingUpdates = false;
        DisposeResources();
    }

    // ── Public API ─────────────────────────────────────────────────────

    public async Task ScrollToAsync(double y, bool animated = false)
        => await _scrollView.ScrollToAsync(0, y, animated);

    public (int start, int end) GetVisibleRange()
    {
        if (_currentRenderList == null) return (0, -1);
        return (_currentRenderList.VisibleStart, _currentRenderList.VisibleEnd);
    }

    public CardState? GetCardStateAt(int index)
    {
        if (_lastState.Cards.IsDefaultOrEmpty || index < 0 || index >= _lastState.Cards.Length)
            return null;
        return _lastState.Cards[index];
    }

    public void SetCards(Card[] cards)
    {
        var cardStates = cards.Select(c => CardState.FromCard(c)).ToImmutableArray();
        UpdateState(s => s with { Cards = cardStates });
    }

    public void SetCollection(CollectionItem[] items)
    {
        var cardStates = items.Select(i => CardState.FromCard(i.Card, i.Quantity)).ToImmutableArray();
        UpdateState(s => s with { Cards = cardStates });
    }

    public void AddCards(Card[] cards)
    {
        var newStates = cards.Select(c => CardState.FromCard(c));
        UpdateState(s => s with { Cards = s.Cards.AddRange(newStates) });
    }

    public async Task AddCardsAsync(IEnumerable<Card> newCards)
    {
        var newStates = await Task.Run(() => newCards.Select(c => CardState.FromCard(c)).ToImmutableArray());
        UpdateState(s => s with { Cards = s.Cards.AddRange(newStates) });
    }

    public void ClearCards()
        => UpdateState(s => s with { Cards = ImmutableArray<CardState>.Empty });

    public void UpdateCardPrices(string uuid, CardPriceData prices)
    {
        UpdateState(s =>
        {
            int index = -1;
            for (int i = 0; i < s.Cards.Length; i++)
                if (s.Cards[i].Id.Value == uuid) { index = i; break; }

            if (index >= 0)
            {
                var newCard = s.Cards[index] with { PriceData = prices, CachedDisplayPrice = "" };
                newCard = newCard with { CachedDisplayPrice = newCard.GetDisplayPrice() };
                return s with { Cards = s.Cards.SetItem(index, newCard) };
            }
            return s;
        });
    }

    public void UpdateCardPricesBulk(Dictionary<string, CardPriceData> pricesMap)
    {
        UpdateState(s =>
        {
            var builder = s.Cards.ToBuilder();
            bool changed = false;
            for (int i = 0; i < builder.Count; i++)
            {
                var card = builder[i];
                if (pricesMap.TryGetValue(card.Id.Value, out var prices))
                {
                    var newCard = card with { PriceData = prices, CachedDisplayPrice = "" };
                    newCard = newCard with { CachedDisplayPrice = newCard.GetDisplayPrice() };
                    builder[i] = newCard;
                    changed = true;
                }
            }
            return changed ? s with { Cards = builder.ToImmutable() } : s;
        });
    }

    public void ForceRedraw() => _canvas.InvalidateSurface();
    public void OnSleep()
    {
        // No GL context to clear for SKCanvasView
    }
    public void OnResume()
    {
        Task.Run(async () =>
        {
            if (_imageCache == null) return;

            var visible = _currentRenderList?.Commands
                .OfType<DrawCardCommand>()
                .Select(c => c.Card.ScryfallId)
                .ToList();

            if (visible == null || visible.Count == 0) return;

            foreach (var id in visible)
            {
                if (_imageCache.GetMemoryImage(id) == null)
                    await _imageCache.GetImageAsync(id);
            }

            MainThread.BeginInvokeOnMainThread(() => _canvas.InvalidateSurface());
        });
    }

    // ── State Management ───────────────────────────────────────────────

    private void UpdateState(Func<GridState, GridState> updateFn)
    {
        var newState = updateFn(_lastState);
        _lastState = newState;
        _stateChannel.Writer.TryWrite(newState);
    }

    private async Task ProcessStateUpdates()
    {
        if (_cts == null) return;
        try
        {
            await foreach (var state in _stateChannel.Reader.ReadAllAsync(_cts.Token))
            {
                var renderList = GridLayoutEngine.Calculate(state);

                MainThread.BeginInvokeOnMainThread(() =>
                {
                    bool rangeChanged = _currentRenderList == null ||
                        _currentRenderList.VisibleStart != renderList.VisibleStart ||
                        _currentRenderList.VisibleEnd != renderList.VisibleEnd;

                    _currentRenderList = renderList;

                    if (Math.Abs(_spacer.HeightRequest - renderList.TotalHeight) > 1)
                        _spacer.HeightRequest = Math.Max(0, renderList.TotalHeight);

                    _canvas.InvalidateSurface();

                    if (rangeChanged)
                        VisibleRangeChanged?.Invoke(renderList.VisibleStart, renderList.VisibleEnd);
                });
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { Console.WriteLine($"Grid Error: {ex}"); }
        finally { _isProcessingUpdates = false; }
    }

    // ── Event Handlers ─────────────────────────────────────────────────

    private void OnScrolled(object? sender, ScrolledEventArgs e)
    {
        UpdateState(s => s with
        {
            Viewport = s.Viewport with { ScrollY = (float)e.ScrollY }
        });
        Scrolled?.Invoke(this, e);
    }

    protected override void OnSizeAllocated(double width, double height)
    {
        base.OnSizeAllocated(width, height);
        if (width > 0 && height > 0)
        {
            UpdateState(s => s with
            {
                Viewport = s.Viewport with { Width = (float)width, Height = (float)height }
            });
        }
    }

    private void OnTapped(object? sender, TappedEventArgs e)
    {
        if (_currentRenderList == null) return;
        var point = e.GetPosition(_spacer);
        if (point == null) return;

        var id = HitTest((float)point.Value.X, (float)point.Value.Y);
        if (id != null) CardClicked?.Invoke(id);
    }

    // Long Press
    private IDispatcherTimer? _longPressTimer;
    private Point _pressPoint;
    private bool _isLongPressing;

    private void OnPointerPressed(object? sender, PointerEventArgs e)
    {
        var point = e.GetPosition(_spacer);
        if (point == null) return;

        _pressPoint = point.Value;
        _isLongPressing = true;

        _longPressTimer?.Stop();
        _longPressTimer = Dispatcher.CreateTimer();
        _longPressTimer.Interval = TimeSpan.FromMilliseconds(500);
        _longPressTimer.IsRepeating = false;
        _longPressTimer.Tick += (s, args) =>
        {
            if (_isLongPressing)
            {
                var id = HitTest((float)_pressPoint.X, (float)_pressPoint.Y);
                if (id != null)
                {
                    MainThread.BeginInvokeOnMainThread(() => CardLongPressed?.Invoke(id));
                    try { HapticFeedback.Perform(HapticFeedbackType.LongPress); } catch { }
                }
            }
            _isLongPressing = false;
        };
        _longPressTimer.Start();
    }

    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (!_isLongPressing) return;
        var point = e.GetPosition(_spacer);
        if (point == null) return;

        if (Math.Abs(point.Value.X - _pressPoint.X) > 10 ||
            Math.Abs(point.Value.Y - _pressPoint.Y) > 10)
        {
            _isLongPressing = false;
            _longPressTimer?.Stop();
        }
    }

    private void OnPointerReleased(object? sender, PointerEventArgs e)
    {
        _isLongPressing = false;
        _longPressTimer?.Stop();
    }

    private string? HitTest(float spacerX, float spacerY)
    {
        if (_currentRenderList == null) return null;
        foreach (var cmd in _currentRenderList.Commands)
        {
            if (cmd is DrawCardCommand draw && draw.Rect.Contains(spacerX, spacerY))
                return draw.Card.Id.Value;
        }
        return null;
    }

    // ── Rendering ──────────────────────────────────────────────────────

    private void EnsureResources()
    {
        _bgPaint ??= new SKPaint { Color = new SKColor(30, 30, 30), IsAntialias = true };
        _textPaint ??= new SKPaint { Color = SKColors.White, IsAntialias = true };
        _textFont ??= new SKFont { Size = 12f };
        _pricePaint ??= new SKPaint { Color = SKColors.LightGreen, IsAntialias = true };
        _priceFont ??= new SKFont { Size = 8.5f };
        _badgeBgPaint ??= new SKPaint { IsAntialias = true, Color = new SKColor(220, 50, 50) };
        _badgeTextPaint ??= new SKPaint { IsAntialias = true, Color = SKColors.White };
        _badgeFont ??= new SKFont(SKTypeface.FromFamilyName(null, SKFontStyle.Bold), 11f);
        _shimmerBasePaint ??= new SKPaint { Color = new SKColor(40, 40, 40) };
        _cardRoundRect ??= new SKRoundRect();
        _imageRoundRect ??= new SKRoundRect();
    }

    private void DisposeResources()
    {
        _bgPaint?.Dispose(); _bgPaint = null;
        _textPaint?.Dispose(); _textPaint = null;
        _textFont?.Dispose(); _textFont = null;
        _pricePaint?.Dispose(); _pricePaint = null;
        _priceFont?.Dispose(); _priceFont = null;
        _badgeBgPaint?.Dispose(); _badgeBgPaint = null;
        _badgeTextPaint?.Dispose(); _badgeTextPaint = null;
        _badgeFont?.Dispose(); _badgeFont = null;
        _shimmerBasePaint?.Dispose(); _shimmerBasePaint = null;
        _cardRoundRect?.Dispose(); _cardRoundRect = null;
        _imageRoundRect?.Dispose(); _imageRoundRect = null;
    }

    private void OnPaintSurface(object? sender, SKPaintSurfaceEventArgs e)
    {
        // Calculate animation frame
        long currentTime = _animationStopwatch.ElapsedMilliseconds;
        float deltaTime = (currentTime - _lastFrameTime) / 1000f;
        _lastFrameTime = currentTime;

        if (deltaTime > 0.1f) deltaTime = 0.016f;

        _shimmerPhase += deltaTime * 0.8f;
        if (_shimmerPhase > 1f) _shimmerPhase -= 1f;

        var canvas = e.Surface.Canvas;
        var info = e.Info;

        canvas.Clear(new SKColor(18, 18, 18));

        var list = _currentRenderList;
        if (list == null || list.Commands.IsEmpty) return;

        if (_bgPaint == null) EnsureResources();

        float scale = info.Width / (float)(Width > 0 ? Width : 360);
        canvas.Scale(scale);
        canvas.Translate(0, -_lastState.Viewport.ScrollY);

        foreach (var cmd in list.Commands)
        {
            if (cmd is DrawCardCommand draw)
                RenderCard(canvas, draw);
        }

        bool hasLoading = false;
        lock (_loadingLock) { hasLoading = _loadingImages.Count > 0; }

        if (hasLoading && _isLoaded)
        {
            _canvas.InvalidateSurface();
        }
    }

    private void RenderCard(SKCanvas canvas, DrawCardCommand cmd)
    {
        var rect = cmd.Rect;
        var card = cmd.Card;

        // 1. Card background
        _cardRoundRect!.SetRect(rect, 8f, 8f);
        canvas.DrawRoundRect(_cardRoundRect, _bgPaint);

        // 2. Card image
        var imageRect = new SKRect(rect.Left, rect.Top, rect.Right, rect.Top + rect.Width * 1.3968f);

        SKImage? image = null;
        if (_imageCache != null)
        {
            image = _imageCache.GetMemoryImage(card.ScryfallId);

            if (image == null)
            {
                bool shouldLoad = false;
                lock (_loadingLock)
                {
                    if (!_loadingImages.Contains(card.ScryfallId))
                    {
                        _loadingImages.Add(card.ScryfallId);
                        shouldLoad = true;
                    }
                }

                if (shouldLoad)
                {
                    Task.Run(async () => {
                        await _downloadSemaphore.WaitAsync();
                        try
                        {
                            var img = await _imageCache.GetImageAsync(card.ScryfallId);

                            if (img == null && _downloadService != null)
                            {
                                img = await _downloadService.DownloadImageDirectAsync(card.ScryfallId);
                                if (img != null)
                                    _imageCache.AddToMemoryCache(card.ScryfallId, img);
                            }

                            if (img != null)
                                MainThread.BeginInvokeOnMainThread(() => _canvas.InvalidateSurface());
                        }
                        finally
                        {
                            lock (_loadingLock) _loadingImages.Remove(card.ScryfallId);
                            _downloadSemaphore.Release();
                        }
                    });
                }
            }
        }

        if (image != null && image.Handle != IntPtr.Zero)
        {
            canvas.Save();
            _imageRoundRect!.SetRect(imageRect, 8f, 8f);
            canvas.ClipRoundRect(_imageRoundRect, antialias: true);
            canvas.DrawImage(image, imageRect);
            canvas.Restore();
        }
        else
        {
            DrawShimmer(canvas, imageRect);
        }

        // 3. Name text
        float textY = imageRect.Bottom + 16f;
        DrawWrappedText(canvas, card.Name, rect.Left + 4f, textY, rect.Width - 8f, _textFont, _textPaint);

        // 4. Price chip — bottom-left corner of the image, overlaid
        if (!string.IsNullOrEmpty(card.CachedDisplayPrice))
        {
            const float chipPadX = 5f;
            const float chipPadY = 3f;
            const float chipRadius = 4f;
            const float chipMargin = 6f;

            float priceTextWidth = _priceFont!.MeasureText(card.CachedDisplayPrice);
            float chipW = priceTextWidth + chipPadX * 2f;
            float chipH = _priceFont.Size + chipPadY * 2f;

            float chipY = imageRect.Bottom - (imageRect.Height * 0.15f) - chipH;
            float chipX = imageRect.Left + chipMargin;

            var chipRect = new SKRect(chipX, chipY, chipX + chipW, chipY + chipH);

            using var chipBgPaint = new SKPaint
            {
                IsAntialias = true,
                Color = new SKColor(0, 0, 0, 185)
            };
            canvas.DrawRoundRect(chipRect, chipRadius, chipRadius, chipBgPaint);

            float chipTextX = chipRect.Left + chipPadX;
            float chipTextY = chipRect.Bottom - chipPadY - 1f;
            canvas.DrawText(card.CachedDisplayPrice, chipTextX, chipTextY, SKTextAlign.Left, _priceFont, _pricePaint);
        }

        // 5. Quantity badge
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
    }

    private void DrawWrappedText(SKCanvas canvas, string text, float x, float y,
        float maxWidth, SKFont? font, SKPaint? paint, int maxLines = 2)
    {
        if (string.IsNullOrEmpty(text) || font == null || paint == null) return;

        var words = text.Split(' ');
        var currentLine = "";
        float lineHeight = font.Size * 1.2f;
        float currentY = y;
        int lineCount = 0;

        foreach (var word in words)
        {
            if (lineCount >= maxLines) break;

            string testLine = string.IsNullOrEmpty(currentLine) ? word : currentLine + " " + word;
            float width = font.MeasureText(testLine);

            if (width > maxWidth)
            {
                if (!string.IsNullOrEmpty(currentLine))
                {
                    string toDraw = (lineCount == maxLines - 1)
                        ? TruncateWithEllipsis(currentLine, maxWidth, font)
                        : currentLine;

                    canvas.DrawText(toDraw, x, currentY, SKTextAlign.Left, font, paint);
                    lineCount++;
                    currentY += lineHeight;
                    currentLine = word;
                }
                else
                {
                    canvas.DrawText(TruncateWithEllipsis(word, maxWidth, font), x, currentY, SKTextAlign.Left, font, paint);
                    lineCount++;
                    currentY += lineHeight;
                    currentLine = "";
                }
            }
            else
            {
                currentLine = testLine;
            }
        }

        if (!string.IsNullOrEmpty(currentLine) && lineCount < maxLines)
        {
            string toDraw = (lineCount == maxLines - 1)
                ? TruncateWithEllipsis(currentLine, maxWidth, font)
                : currentLine;
            canvas.DrawText(toDraw, x, currentY, SKTextAlign.Left, font, paint);
        }
    }

    private string TruncateWithEllipsis(string text, float maxWidth, SKFont font)
    {
        if (font.MeasureText(text) <= maxWidth) return text;
        while (text.Length > 0 && font.MeasureText(text + "…") > maxWidth)
            text = text[..^1];
        return text + "…";
    }

    private void DrawShimmer(SKCanvas canvas, SKRect rect)
    {
        canvas.Save();
        _imageRoundRect!.SetRect(rect, 8f, 8f);
        canvas.ClipRoundRect(_imageRoundRect, antialias: true);

        canvas.DrawRect(rect, _shimmerBasePaint);

        float sweepWidth = rect.Width * 0.6f;
        float travelRange = rect.Width + sweepWidth;
        float shimmerX = rect.Left - sweepWidth + travelRange * _shimmerPhase;

        using var shimmerPaint = new SKPaint { IsAntialias = true };
        shimmerPaint.Shader = SKShader.CreateLinearGradient(
            new SKPoint(shimmerX, rect.Top),
            new SKPoint(shimmerX + sweepWidth, rect.Top),
            new[] { SKColors.Transparent, new SKColor(255, 255, 255, 55), SKColors.Transparent },
            new[] { 0f, 0.5f, 1f },
            SKShaderTileMode.Clamp);

        canvas.DrawRect(rect, shimmerPaint);
        canvas.Restore();
    }
}