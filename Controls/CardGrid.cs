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
    private readonly SKGLView _canvas;
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

    // Animation
    private float _shimmerPhase;
    private readonly Stopwatch _animationStopwatch = new();
    private IDispatcherTimer? _animationTimer;
    private long _lastFrameTime;

    // GL State
    private GRRecordingContext? _lastGLContext;
    private int _glGeneration = 0;

    // Cached Paints
    private SKPaint? _bgPaint;
    private SKPaint? _textPaint;
    private SKFont? _textFont;
    private SKPaint? _pricePaint;
    private SKFont? _priceFont;
    private SKPaint? _shimmerBasePaint;
    private SKPaint? _shimmerGradientPaint;
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

        _canvas = new SKGLView
        {
            HorizontalOptions = LayoutOptions.Fill,
            VerticalOptions = LayoutOptions.Fill,
            IgnorePixelScaling = true,
            EnableTouchEvents = false,
            InputTransparent = true,
            HasRenderLoop = false // We control the loop
        };
        _canvas.PaintSurface += OnPaintSurface;

        _spacer = new BoxView
        {
            Color = Colors.Transparent,
            WidthRequest = 100,
            HeightRequest = 100,
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

        // Handle taps and long presses
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

    private void OnLoaded(object? sender, EventArgs e)
    {
        // Resolve services
        if (Handler?.MauiContext != null)
        {
            _imageCache = Handler.MauiContext.Services.GetService<ImageCacheService>();
            _downloadService = Handler.MauiContext.Services.GetService<ImageDownloadService>();
        }

        _cts = new CancellationTokenSource();
        Task.Run(ProcessStateUpdates);

        StartAnimationTimer();
    }

    private void OnUnloaded(object? sender, EventArgs e)
    {
        StopAnimationTimer();
        _cts?.Cancel();
        DisposeResources();
    }

    private void StartAnimationTimer()
    {
        if (_animationTimer != null) return;
        _animationTimer = Dispatcher.CreateTimer();
        _animationTimer.Interval = TimeSpan.FromMilliseconds(16); // ~60 FPS
        _animationTimer.Tick += (_, _) => UpdateAnimations();
        _animationTimer.Start();
    }

    private void StopAnimationTimer()
    {
        _animationTimer?.Stop();
        _animationTimer = null;
    }

    private void UpdateAnimations()
    {
        long currentTime = _animationStopwatch.ElapsedMilliseconds;
        float deltaTime = (currentTime - _lastFrameTime) / 1000f;
        _lastFrameTime = currentTime;

        if (deltaTime > 0.1f) deltaTime = 0.016f;

        _shimmerPhase += deltaTime * 0.8f;
        if (_shimmerPhase > 1f) _shimmerPhase -= 1f;

        // Only invalidate if we have active shimmers (loading images)
        bool hasLoading = false;
        lock (_loadingLock) { hasLoading = _loadingImages.Count > 0; }

        if (hasLoading)
        {
            _canvas.InvalidateSurface();
        }
    }

    // ── Public API ─────────────────────────────────────────────────────

    public async Task ScrollToAsync(double y, bool animated = false)
    {
        await _scrollView.ScrollToAsync(0, y, animated);
    }

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
        // Offload conversion to thread pool
        var newStates = await Task.Run(() => newCards.Select(c => CardState.FromCard(c)).ToImmutableArray());
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
            // Find index manually (ImmutableArray doesn't have FindIndex)
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
                var newCard = s.Cards[index] with { PriceData = prices, CachedDisplayPrice = "" };
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

    public void ForceRedraw() => _canvas.InvalidateSurface();

    public void OnSleep()
    {
        _lastGLContext = null; // Force resource reload on resume
    }

    public void OnResume()
    {
        _canvas.InvalidateSurface();
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
                // Calculate Layout (Pure)
                var renderList = GridLayoutEngine.Calculate(state);

                // Update UI on Main Thread
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    bool rangeChanged = _currentRenderList == null ||
                        _currentRenderList.VisibleStart != renderList.VisibleStart ||
                        _currentRenderList.VisibleEnd != renderList.VisibleEnd;

                    _currentRenderList = renderList;

                    // Update Spacer Height
                    if (Math.Abs(_spacer.HeightRequest - renderList.TotalHeight) > 1)
                    {
                        _spacer.HeightRequest = renderList.TotalHeight;
                    }

                    _canvas.InvalidateSurface();

                    if (rangeChanged)
                    {
                        VisibleRangeChanged?.Invoke(renderList.VisibleStart, renderList.VisibleEnd);
                    }
                });
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Console.WriteLine($"Grid Error: {ex}");
        }
    }

    // ── Event Handlers ─────────────────────────────────────────────────

    private void OnScrolled(object? sender, ScrolledEventArgs e)
    {
        UpdateState(s => s with {
            Viewport = s.Viewport with { ScrollY = (float)e.ScrollY }
        });
        Scrolled?.Invoke(this, e);
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

    private void OnTapped(object? sender, TappedEventArgs e)
    {
        if (_currentRenderList == null) return;

        var point = e.GetPosition(_spacer);
        if (point == null) return;

        var id = HitTest((float)point.Value.X, (float)point.Value.Y);
        if (id != null) CardClicked?.Invoke(id);
    }

    // Long Press Logic
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
        _longPressTimer.Tick += (s, args) =>
        {
            _longPressTimer?.Stop();
            if (_isLongPressing)
            {
                var id = HitTest((float)_pressPoint.X, (float)_pressPoint.Y);
                if (id != null)
                {
                    MainThread.BeginInvokeOnMainThread(() => CardLongPressed?.Invoke(id));
                    // Haptic feedback?
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

        if (Math.Abs(point.Value.X - _pressPoint.X) > 10 || Math.Abs(point.Value.Y - _pressPoint.Y) > 10)
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

    private string? HitTest(float x, float y)
    {
        if (_currentRenderList == null) return null;
        foreach (var cmd in _currentRenderList.Commands)
        {
            if (cmd is DrawCardCommand draw && draw.Rect.Contains(x, y))
            {
                return draw.Card.Id.Value;
            }
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
        _priceFont ??= new SKFont { Size = 10f };
        _badgeBgPaint ??= new SKPaint { IsAntialias = true, Color = new SKColor(220, 50, 50) };
        _badgeTextPaint ??= new SKPaint { IsAntialias = true, Color = SKColors.White };
        _badgeFont ??= new SKFont(SKTypeface.FromFamilyName(null, SKFontStyle.Bold), 11f);

        _shimmerBasePaint ??= new SKPaint { Color = new SKColor(40, 40, 40) };
        _shimmerGradientPaint ??= new SKPaint { IsAntialias = true };

        if (_shimmerGradientPaint.Shader == null)
        {
            _shimmerGradientPaint.Shader = SKShader.CreateLinearGradient(
                new SKPoint(0, 0),
                new SKPoint(100, 0),
                [SKColors.Transparent, new SKColor(255, 255, 255, 60), SKColors.Transparent],
                [0f, 0.5f, 1f],
                SKShaderTileMode.Clamp);
        }

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
        _shimmerGradientPaint?.Dispose(); _shimmerGradientPaint = null;
        _cardRoundRect?.Dispose(); _cardRoundRect = null;
        _imageRoundRect?.Dispose(); _imageRoundRect = null;
    }

    private void OnPaintSurface(object? sender, SKPaintGLSurfaceEventArgs e)
    {
        var currentContext = e.Surface.Context;
        if (_lastGLContext != currentContext)
        {
            _glGeneration++;
            _lastGLContext = currentContext;
            // Clear any GL-dependent resources if we had them (like cached SKImages backed by textures)
            // Our ImageCacheService returns Raster images usually, so this might be fine.
        }

        var canvas = e.Surface.Canvas;
        var info = e.Info;

        canvas.Clear(new SKColor(18, 18, 18));

        var list = _currentRenderList;
        if (list == null || list.Commands.IsEmpty) return;

        EnsureResources();

        float scale = info.Width / (float)(Width > 0 ? Width : 360);
        canvas.Scale(scale);
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

        // Background
        _cardRoundRect!.SetRect(rect, 8f, 8f);
        canvas.DrawRoundRect(_cardRoundRect, _bgPaint);

        // Image Logic
        SKImage? image = null;
        if (_imageCache != null)
        {
             // 1. Try Memory (L1)
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
                     // Trigger Async Load
                     Task.Run(async () => {
                         try
                         {
                             // 2. Try Disk/DB (L2/L3)
                             var img = await _imageCache.GetImageAsync(card.ScryfallId);

                             // 3. Try Network (L4)
                             if (img == null && _downloadService != null)
                             {
                                 img = await _downloadService.DownloadImageDirectAsync(card.ScryfallId);
                                 if (img != null)
                                 {
                                     _imageCache.AddToMemoryCache(card.ScryfallId, img);
                                 }
                             }

                             if (img != null)
                             {
                                 MainThread.BeginInvokeOnMainThread(() => _canvas.InvalidateSurface());
                             }
                         }
                         finally
                         {
                             lock (_loadingLock) _loadingImages.Remove(card.ScryfallId);
                         }
                     });
                 }
             }
        }

        var imageRect = new SKRect(rect.Left, rect.Top, rect.Right, rect.Top + rect.Width * 1.3968f);

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

        // Text
        float textY = imageRect.Bottom + 16f;
        canvas.DrawText(card.Name, rect.Left + 4f, textY, SKTextAlign.Left, _textFont, _textPaint);

        // Price
        if (!string.IsNullOrEmpty(card.CachedDisplayPrice))
        {
            canvas.DrawText(card.CachedDisplayPrice, rect.Right - 40f, rect.Bottom - 6f, SKTextAlign.Left, _priceFont, _pricePaint);
        }

        // Quantity Badge
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

    private void DrawShimmer(SKCanvas canvas, SKRect rect)
    {
        canvas.Save();
        _imageRoundRect!.SetRect(rect, 8f, 8f);
        canvas.ClipRoundRect(_imageRoundRect, antialias: true);

        // Base
        canvas.DrawRect(rect, _shimmerBasePaint);

        // Shimmer Gradient
        float shimmerWidth = rect.Width * 1.5f;
        float shimmerX = rect.Left - rect.Width + (rect.Width * 3f) * _shimmerPhase;

        canvas.Translate(shimmerX, rect.Top);
        canvas.Scale(shimmerWidth / 100f, 1f);
        canvas.DrawRect(0, 0, 100, rect.Height, _shimmerGradientPaint);

        canvas.Restore();
    }
}
