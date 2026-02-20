using System.Collections.Immutable;
using System.Threading.Channels;
using MTGFetchMAUI.Core.Layout;
using MTGFetchMAUI.Services;
using MTGFetchMAUI.Models;
using SkiaSharp;
using SkiaSharp.Views.Maui;
using SkiaSharp.Views.Maui.Controls;
using System.Diagnostics;

namespace MTGFetchMAUI.Controls;

public class MTGCardGrid : ContentView, IDisposable
{
    private readonly SKGLView _canvas;
    private readonly ScrollView _scrollView;
    private readonly SKCanvasView _inputLayer;
    private readonly Channel<GridState> _stateChannel;
    private readonly CancellationTokenSource _cts = new();

    private ImageCacheService? _imageCache;
    private ImageDownloadService? _imageDownloadService;

    // State
    private GridState _lastState = GridState.Empty;
    private RenderList _currentRenderList = RenderList.Empty;
    private readonly HashSet<string> _loadingImages = [];
    private readonly object _loadingLock = new();

    // Animation
    private readonly Stopwatch _animationStopwatch = new();
    private IDispatcherTimer? _animationTimer;
    private float _shimmerPhase;
    private long _lastFrameTime;

    // Input
    private int _pressedIndex = -1;
    private SKPoint _pressPoint;
    private IDispatcherTimer? _longPressTimer;
    private bool _isLongPress;

    // Events
    public event Action<string>? CardClicked;
    public event Action<string>? CardLongPressed;
    public event Action<int, int>? VisibleRangeChanged;
    public event Action<float, float, float>? Scrolled; // scrollY, viewportH, contentH

    // Paints (Cached)
    private SKPaint? _bgPaint;
    private SKPaint? _cardBgPaint;
    private SKPaint? _textPaint;
    private SKPaint? _subTextPaint;
    private SKPaint? _priceTextPaint;
    private SKPaint? _priceBgPaint;
    private SKPaint? _badgeBgPaint;
    private SKPaint? _badgeTextPaint;
    private SKPaint? _shimmerBasePaint;
    private SKPaint? _shimmerGradientPaint;
    private SKPaint? _hoverPaint;
    private SKFont? _textFont;
    private SKFont? _subFont;
    private SKFont? _priceFont;
    private SKFont? _badgeFont;

    public int CardCount => _lastState.Cards.Length;
    public float TotalContentHeight => _currentRenderList.TotalHeight;

    public MTGCardGrid()
    {
        _stateChannel = Channel.CreateBounded<GridState>(new BoundedChannelOptions(1)
        {
            FullMode = BoundedChannelFullMode.DropOldest
        });

        // 1. The Rendering Layer (Sticky)
        _canvas = new SKGLView
        {
            HorizontalOptions = LayoutOptions.Fill,
            VerticalOptions = LayoutOptions.Fill,
            IgnorePixelScaling = true,
            EnableTouchEvents = false,
            InputTransparent = true // Let input pass through to ScrollView/InputLayer
        };
        _canvas.PaintSurface += OnPaintSurface;

        // 2. The Input/Spacer Layer
        // This view sits inside the ScrollView, sizing the content and capturing touches.
        _inputLayer = new SKCanvasView
        {
            BackgroundColor = Colors.Transparent,
            EnableTouchEvents = true,
            HorizontalOptions = LayoutOptions.Fill,
            VerticalOptions = LayoutOptions.Start // Height will be set explicitly
        };
        _inputLayer.PaintSurface += (s, e) => e.Surface.Canvas.Clear(SKColors.Transparent);
        _inputLayer.Touch += OnTouch;

        // 3. The Scroll Container
        _scrollView = new ScrollView
        {
            Content = _inputLayer,
            BackgroundColor = Colors.Transparent,
            HorizontalOptions = LayoutOptions.Fill,
            VerticalOptions = LayoutOptions.Fill
        };
        _scrollView.Scrolled += OnScrollViewScrolled;

        // Composition
        var grid = new Grid();
        grid.Add(_canvas);      // Layer 0: Renderer (Behind)
        grid.Add(_scrollView);  // Layer 1: Scroller + Input (Top)

        Content = grid;

        // Start processing loops
        Task.Run(ProcessStateUpdates);
        StartAnimationLoop();

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object? sender, EventArgs e)
    {
        if (Handler?.MauiContext != null)
        {
            _imageCache = Handler.MauiContext.Services.GetService<ImageCacheService>();
            _imageDownloadService = Handler.MauiContext.Services.GetService<ImageDownloadService>();
        }
        InitializePaints();
    }

    private void OnUnloaded(object? sender, EventArgs e)
    {
        Dispose();
    }

    private bool _disposed;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        try { _cts.Cancel(); } catch { }
        _stateChannel.Writer.TryComplete();
        StopAnimationLoop();
        DisposePaints();
        GC.SuppressFinalize(this);
    }

    // ── Public API ─────────────────────────────────────────────────────

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

    public void ClearCards()
    {
        UpdateState(s => s with { Cards = ImmutableArray<CardState>.Empty });
    }

    public void UpdateCardPrices(string uuid, CardPriceData prices)
    {
        UpdateState(s =>
        {
            int index = -1;
            for (int i = 0; i < s.Cards.Length; i++)
            {
                if (s.Cards[i].Id.Value == uuid)
                {
                    index = i;
                    break;
                }
            }

            if (index >= 0)
            {
                var card = s.Cards[index];
                var newCard = card with { PriceData = prices, CachedDisplayPrice = "" };
                // Recalculate display price
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

    public void SetScrollOffset(float offset)
    {
        MainThread.BeginInvokeOnMainThread(async () =>
        {
            await _scrollView.ScrollToAsync(0, offset, false);
        });
    }

    public (int start, int end) GetVisibleRange()
    {
        return (_currentRenderList.VisibleStart, _currentRenderList.VisibleEnd);
    }

    public GridCardData? GetCardAt(int index)
    {
        if (index < 0 || index >= _lastState.Cards.Length) return null;
        var state = _lastState.Cards[index];
        // Create a snapshot GridCardData for compatibility
        return new GridCardData
        {
            UUID = state.Id.Value,
            Name = state.Name,
            SetCode = state.SetCode,
            Number = state.Number,
            ScryfallId = state.ScryfallId,
            Quantity = state.Quantity,
            IsOnlineOnly = state.IsOnlineOnly,
            PriceData = state.PriceData,
            CachedDisplayPrice = state.CachedDisplayPrice
        };
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
        try
        {
            await foreach (var state in _stateChannel.Reader.ReadAllAsync(_cts.Token))
            {
                var renderList = GridLayoutEngine.Calculate(state);

                MainThread.BeginInvokeOnMainThread(() =>
                {
                    var oldStart = _currentRenderList.VisibleStart;
                    var oldEnd = _currentRenderList.VisibleEnd;

                    _currentRenderList = renderList;

                    // Update Spacer Height
                    if (Math.Abs(_inputLayer.HeightRequest - renderList.TotalHeight) > 1)
                    {
                        _inputLayer.HeightRequest = renderList.TotalHeight;
                    }

                    if (oldStart != renderList.VisibleStart || oldEnd != renderList.VisibleEnd)
                    {
                        VisibleRangeChanged?.Invoke(renderList.VisibleStart, renderList.VisibleEnd);
                    }

                    _canvas.InvalidateSurface();
                });
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Console.WriteLine($"Grid Error: {ex}");
        }
    }

    // ── Animation Loop ─────────────────────────────────────────────────

    public void StartTimers() => StartAnimationLoop();
    public void StopTimers() => StopAnimationLoop();

    private void StartAnimationLoop()
    {
        if (_animationTimer != null) return;
        _animationStopwatch.Start();
        _animationTimer = Dispatcher.CreateTimer();
        _animationTimer.Interval = TimeSpan.FromMilliseconds(16);
        _animationTimer.Tick += (_, _) =>
        {
            long now = _animationStopwatch.ElapsedMilliseconds;
            float dt = (now - _lastFrameTime) / 1000f;
            _lastFrameTime = now;
            if (dt > 0.1f) dt = 0.016f;

            _shimmerPhase += dt * 0.8f;
            if (_shimmerPhase > 1f) _shimmerPhase -= 1f;

            // Only invalidate if we have missing images in visible range
            // Optimization: check if any visible card needs shimmer
            // For now, simple invalidate if list not empty
            if (_currentRenderList.Commands.Length > 0)
                _canvas.InvalidateSurface();
        };
        _animationTimer.Start();
    }

    private void StopAnimationLoop()
    {
        _animationTimer?.Stop();
        _animationTimer = null;
        _animationStopwatch.Stop();
    }

    // ── Event Handlers ─────────────────────────────────────────────────

    private void OnScrollViewScrolled(object? sender, ScrolledEventArgs e)
    {
        float scrollY = (float)e.ScrollY;
        UpdateState(s => s with {
            Viewport = s.Viewport with { ScrollY = scrollY }
        });

        float viewportH = (float)(Height > 0 ? Height : _scrollView.Height);
        float contentH = (float)_inputLayer.HeightRequest;
        Scrolled?.Invoke(scrollY, viewportH, contentH);
    }

    protected override void OnSizeAllocated(double width, double height)
    {
        base.OnSizeAllocated(width, height);
        if (width > 0 && height > 0)
        {
            UpdateState(s => s with {
                Viewport = s.Viewport with { Width = (float)width, Height = (float)height }
            });
        }
    }

    private void OnTouch(object? sender, SKTouchEventArgs e)
    {
        float y = e.Location.Y; // Relative to content top
        float x = e.Location.X;

        switch (e.ActionType)
        {
            case SKTouchAction.Pressed:
                _pressPoint = e.Location;
                _pressedIndex = HitTest(x, y);
                _isLongPress = false;

                if (_pressedIndex >= 0)
                {
                    _longPressTimer?.Stop();
                    _longPressTimer = Dispatcher.CreateTimer();
                    _longPressTimer.Interval = TimeSpan.FromMilliseconds(500);
                    _longPressTimer.Tick += (s, args) =>
                    {
                        _isLongPress = true;
                        _longPressTimer?.Stop();
                        if (_pressedIndex >= 0)
                        {
                             var card = GetCardAt(_pressedIndex);
                             if (card != null) CardLongPressed?.Invoke(card.UUID);
                             _pressedIndex = -1; // Reset
                             _canvas.InvalidateSurface();
                        }
                    };
                    _longPressTimer.Start();
                    _canvas.InvalidateSurface();
                }

                // Allow ScrollView to handle touch if we didn't consume?
                // Actually if we want ScrollView to scroll, we should probably set Handled = false?
                // But we want to capture long press.
                // Standard behavior: ScrollView intercepts 'Moved' if it exceeds threshold.
                e.Handled = false;
                break;

            case SKTouchAction.Moved:
                if (Math.Abs(y - _pressPoint.Y) > 10 || Math.Abs(x - _pressPoint.X) > 10)
                {
                    _longPressTimer?.Stop();
                    if (_pressedIndex != -1)
                    {
                        _pressedIndex = -1;
                        _canvas.InvalidateSurface();
                    }
                }
                e.Handled = false;
                break;

            case SKTouchAction.Released:
                _longPressTimer?.Stop();
                if (!_isLongPress && _pressedIndex >= 0)
                {
                    var card = GetCardAt(_pressedIndex);
                    if (card != null) CardClicked?.Invoke(card.UUID);
                }
                _pressedIndex = -1;
                _canvas.InvalidateSurface();
                e.Handled = false;
                break;

            case SKTouchAction.Cancelled:
                _longPressTimer?.Stop();
                _pressedIndex = -1;
                _canvas.InvalidateSurface();
                e.Handled = false;
                break;
        }
    }

    private int HitTest(float x, float y)
    {
        // Search in current render commands
        foreach (var cmd in _currentRenderList.Commands)
        {
            if (cmd is DrawCardCommand draw && draw.Rect.Contains(x, y))
            {
                return draw.Index;
            }
        }
        return -1;
    }

    // ── Rendering ──────────────────────────────────────────────────────

    private void OnPaintSurface(object? sender, SKPaintGLSurfaceEventArgs e)
    {
        if (_cardBgPaint == null) InitializePaints();

        var canvas = e.Surface.Canvas;
        var info = e.Info;

        // Background
        canvas.Clear(new SKColor(18, 18, 18));

        var list = _currentRenderList;
        if (list == null || list.Commands.IsEmpty) return;

        // Scale
        float scale = info.Width / (float)(Width > 0 ? Width : 360);
        canvas.Scale(scale);

        // Sticky Translation
        // Render commands are absolute (0 to TotalHeight).
        // Canvas is viewport size.
        // We need to shift UP by ScrollY.
        canvas.Translate(0, -_lastState.Viewport.ScrollY);

        foreach (var cmd in list.Commands)
        {
            if (cmd is DrawCardCommand draw)
            {
                RenderCard(canvas, draw);
            }
        }
    }

    private void RenderCard(SKCanvas canvas, DrawCardCommand cmd)
    {
        var rect = cmd.Rect;
        var card = cmd.Card;
        var index = cmd.Index;

        // Card Background
        canvas.DrawRoundRect(rect, 8f, 8f, _cardBgPaint);

        // Hover/Press State
        if (index == _pressedIndex)
        {
            canvas.DrawRoundRect(rect, 8f, 8f, _hoverPaint);
        }

        // Image Area
        var imageRect = new SKRect(rect.Left, rect.Top, rect.Right, rect.Top + rect.Width * 1.3968f);

        // Image Loading/Drawing
        SKImage? image = null;
        if (_imageCache != null)
        {
             image = _imageCache.GetMemoryImage(card.ScryfallId);

             if (image == null && !string.IsNullOrEmpty(card.ScryfallId))
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
                     _imageDownloadService?.DownloadImageAsync(card.ScryfallId, (img, success) =>
                     {
                         if (success && img != null)
                         {
                             _imageCache?.AddToMemoryCache(card.ScryfallId, img);
                             MainThread.BeginInvokeOnMainThread(() => _canvas.InvalidateSurface());
                         }
                         lock (_loadingLock) _loadingImages.Remove(card.ScryfallId);
                     }, "small");
                 }
             }
        }

        if (image != null)
        {
            canvas.Save();
            canvas.ClipRoundRect(new SKRoundRect(imageRect, 8f, 8f), antialias: true);
            canvas.DrawImage(image, imageRect);
            canvas.Restore();
        }
        else
        {
            DrawShimmer(canvas, imageRect);
        }

        // Text Content
        float textY = imageRect.Bottom + 16f;

        // Name
        // Simple truncation
        string name = card.Name;
        if (_textFont!.MeasureText(name) > rect.Width - 8)
        {
            // rudimentary truncate
            while (name.Length > 0 && _textFont.MeasureText(name + "...") > rect.Width - 8)
                name = name[..^1];
            name += "...";
        }
        canvas.DrawText(name, rect.Left + 4f, textY, _textFont, _textPaint);

        // Set info
        string setInfo = $"{card.SetCode} #{card.Number}";
        canvas.DrawText(setInfo, rect.Left + 4f, textY + 16f, _subFont, _subTextPaint);

        // Price Chip
        if (!string.IsNullOrEmpty(card.CachedDisplayPrice))
        {
            string price = card.CachedDisplayPrice;
            float tw = _priceFont!.MeasureText(price);
            float pad = 12f;
            float chipW = tw + pad;
            float chipH = 18f;
            float chipX = imageRect.MidX - chipW / 2f;
            float chipY = imageRect.Bottom - 24f;

            canvas.DrawRoundRect(chipX, chipY, chipW, chipH, 9f, 9f, _priceBgPaint);
            canvas.DrawText(price, chipX + 6f, chipY + 13f, _priceFont, _priceTextPaint);
        }

        // Quantity Badge
        if (card.Quantity > 0)
        {
            string qty = card.Quantity.ToString();
            float tw = _badgeFont!.MeasureText(qty);
            float bw = Math.Max(20f, tw + 10f);
            float bx = imageRect.Right - bw - 4f;
            float by = imageRect.Top + 4f;

            canvas.DrawRoundRect(bx, by, bw, 20f, 10f, 10f, _badgeBgPaint);
            canvas.DrawText(qty, bx + (bw - tw) / 2f, by + 15f, _badgeFont, _badgeTextPaint);
        }
    }

    private void DrawShimmer(SKCanvas canvas, SKRect rect)
    {
        canvas.Save();
        canvas.ClipRoundRect(new SKRoundRect(rect, 8f, 8f), antialias: true);

        canvas.DrawRect(rect, _shimmerBasePaint);

        float shimmerWidth = rect.Width * 1.5f;
        float shimmerX = rect.Left - rect.Width + (rect.Width * 3f) * _shimmerPhase;

        // Recreate shader each frame? Can optimize but skia handles it okay
        using var shader = SKShader.CreateLinearGradient(
            new SKPoint(shimmerX, rect.Top),
            new SKPoint(shimmerX + shimmerWidth, rect.Top),
            [SKColors.Transparent, new SKColor(255, 255, 255, 50), SKColors.Transparent],
            [0f, 0.5f, 1f],
            SKShaderTileMode.Clamp);

        _shimmerGradientPaint!.Shader = shader;
        canvas.DrawRect(rect, _shimmerGradientPaint);
        _shimmerGradientPaint.Shader = null;

        canvas.Restore();
    }

    private void InitializePaints()
    {
        DisposePaints();

        _bgPaint = new SKPaint { Color = new SKColor(18, 18, 18) };
        _cardBgPaint = new SKPaint { Color = new SKColor(30, 30, 30), IsAntialias = true };
        _textPaint = new SKPaint { Color = new SKColor(240, 240, 240), IsAntialias = true };
        _subTextPaint = new SKPaint { Color = new SKColor(160, 160, 160), IsAntialias = true };
        _priceTextPaint = new SKPaint { Color = SKColors.White, IsAntialias = true };
        _priceBgPaint = new SKPaint { Color = new SKColor(0, 0, 0, 180), IsAntialias = true };
        _badgeBgPaint = new SKPaint { Color = new SKColor(220, 50, 50), IsAntialias = true };
        _badgeTextPaint = new SKPaint { Color = SKColors.White, IsAntialias = true };
        _shimmerBasePaint = new SKPaint { Color = new SKColor(40, 40, 40) };
        _shimmerGradientPaint = new SKPaint { IsAntialias = true };
        _hoverPaint = new SKPaint {
            Color = new SKColor(100, 149, 237, 40),
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 2f,
            IsAntialias = true
        };

        _textFont = new SKFont(SKTypeface.Default, 12f);
        _subFont = new SKFont(SKTypeface.Default, 10f);
        _priceFont = new SKFont(SKTypeface.Default, 10f);
        _badgeFont = new SKFont(SKTypeface.FromFamilyName(null, SKFontStyle.Bold), 11f);
    }

    private void DisposePaints()
    {
        _bgPaint?.Dispose();
        _cardBgPaint?.Dispose();
        _textPaint?.Dispose();
        _subTextPaint?.Dispose();
        _priceTextPaint?.Dispose();
        _priceBgPaint?.Dispose();
        _badgeBgPaint?.Dispose();
        _badgeTextPaint?.Dispose();
        _shimmerBasePaint?.Dispose();
        _shimmerGradientPaint?.Dispose();
        _hoverPaint?.Dispose();
        _textFont?.Dispose();
        _subFont?.Dispose();
        _priceFont?.Dispose();
        _badgeFont?.Dispose();
    }
}
