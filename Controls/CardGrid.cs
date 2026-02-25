using AppoMobi.Maui.Gestures;
using MTGFetchMAUI.Core.Layout;
using MTGFetchMAUI.Models;
using MTGFetchMAUI.Services;
using SkiaSharp.Views.Maui.Controls;
using System.Collections.Immutable;
using System.Threading.Channels;

namespace MTGFetchMAUI.Controls;

public class CardGrid : ContentView
{
    private readonly SKCanvasView _canvas;
    private readonly ScrollView _scrollView;
    private readonly GestureSpacerView _spacer;
    private readonly Channel<GridState> _stateChannel;
    private CancellationTokenSource? _cts;

    private ImageCacheService? _imageCache;
    private ImageDownloadService? _downloadService;

    // State
    private GridState _lastState = GridState.Empty;
    private RenderList _currentRenderList = RenderList.Empty;

    // Image loading
    private readonly HashSet<string> _loadingImages = new();
    private readonly object _loadingLock = new();
    private readonly SemaphoreSlim _downloadSemaphore = new(4, 4);

    // Subsystems
    private readonly CardGridRenderer _renderer;
    private readonly CardGridGestureHandler _gestures;

    private bool _isLoaded;
    private bool _isProcessingUpdates;

    // Drag state (managed separately from GridState to avoid pipeline overhead)
    private DragState? _dragState;

    // Events
    public event Action<string>? CardClicked;
    public event Action<string>? CardLongPressed;
    public event Action<int, int>? CardReorderRequested;  // (fromIndex, toIndex)
    public event EventHandler<ScrolledEventArgs>? Scrolled;
    public event Action<int, int>? VisibleRangeChanged;

    public float ContentHeight => _currentRenderList?.TotalHeight ?? 0;

    public static readonly BindableProperty IsDragEnabledProperty = BindableProperty.Create(
        nameof(IsDragEnabled), typeof(bool), typeof(CardGrid), true,
        propertyChanged: (bindable, oldVal, newVal) =>
        {
            if (bindable is CardGrid grid)
            {
                grid._gestures.IsDragEnabled = (bool)newVal;
            }
        });

    public bool IsDragEnabled
    {
        get => (bool)GetValue(IsDragEnabledProperty);
        set => SetValue(IsDragEnabledProperty, value);
    }

    public ViewMode ViewMode
    {
        get => _lastState.Config.ViewMode;
        set => UpdateState(s => s with { Config = s.Config with { ViewMode = value } });
    }

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

        // _gestures must be created before _spacer because GestureSpacerView
        // takes the handler as a constructor argument and wires scroll callbacks.
        _gestures = new CardGridGestureHandler(Dispatcher, HitTest);
        _spacer = new GestureSpacerView(_gestures);

        _scrollView = new ScrollView
        {
            Content = _spacer,
            BackgroundColor = Colors.Transparent,
            HorizontalOptions = LayoutOptions.Fill,
            VerticalOptions = LayoutOptions.Fill
        };
        _scrollView.Scrolled += OnScrolled;

        var grid = new Grid();
        grid.Add(_canvas);
        grid.Add(_scrollView);
        Content = grid;

        _renderer = new CardGridRenderer(_canvas, cacheKey => _imageCache?.GetMemoryImage(cacheKey));
        _gestures.Tapped += id => CardClicked?.Invoke(id);
        _gestures.LongPressed += id => CardLongPressed?.Invoke(id);
        _gestures.DragStarted += OnDragStarted;
        _gestures.DragMoved += OnDragMoved;
        _gestures.DragEnded += OnDragEnded;
        _gestures.DragCancelled += OnDragCancelled;

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

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

        _renderer.EnsureResources();
        MainThread.BeginInvokeOnMainThread(() => _canvas.InvalidateSurface());
    }

    private void OnUnloaded(object? sender, EventArgs e)
    {
        _isLoaded = false;
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
        _isProcessingUpdates = false;
        _renderer.Dispose();
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

    public IReadOnlyList<string> GetAllUuids()
    {
        if (_lastState.Cards.IsDefaultOrEmpty) return [];
        return _lastState.Cards.Select(c => c.Id.Value).ToList();
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

    public void OnSleep() { }

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
                var cacheKey = ImageDownloadService.GetCacheKey(id, "normal", "");
                if (_imageCache.GetMemoryImage(cacheKey) == null)
                    await _imageCache.GetImageAsync(cacheKey);
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

                // Trigger image loads for newly visible cards (outside the render path)
                foreach (var cmd in renderList.Commands.OfType<DrawCardCommand>())
                    EnqueueImageLoad(cmd.Card.ScryfallId);

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

    // ── Image Loading ──────────────────────────────────────────────────

    private void EnqueueImageLoad(string scryfallId)
    {
        if (_imageCache == null) return;

        var cacheKey = ImageDownloadService.GetCacheKey(scryfallId, "normal", "");

        bool shouldLoad = false;
        lock (_loadingLock)
        {
            if (_imageCache.GetMemoryImage(cacheKey) == null && !_loadingImages.Contains(cacheKey))
            {
                _loadingImages.Add(cacheKey);
                shouldLoad = true;
            }
        }

        if (!shouldLoad) return;

        Task.Run(async () =>
        {
            await _downloadSemaphore.WaitAsync();
            try
            {
                var img = await _imageCache.GetImageAsync(cacheKey);

                if (img == null && _downloadService != null)
                {
                    img = await _downloadService.DownloadImageDirectAsync(scryfallId);
                    if (img != null)
                        _imageCache.AddToMemoryCache(cacheKey, img);
                }

                if (img != null)
                    MainThread.BeginInvokeOnMainThread(() => _canvas.InvalidateSurface());
            }
            finally
            {
                lock (_loadingLock) _loadingImages.Remove(cacheKey);
                _downloadSemaphore.Release();
            }
        });
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
            float w = (float)width;
            float h = (float)height;
            bool isLargeScreen = w >= 600;

            UpdateState(s =>
            {
                var newConfig = s.Config with
                {
                    MinCardWidth = isLargeScreen ? 160f : 85f,
                    LabelHeight = isLargeScreen ? 52f : 42f
                };
                return s with
                {
                    Config = newConfig,
                    Viewport = s.Viewport with { Width = w, Height = h }
                };
            });

            MainThread.BeginInvokeOnMainThread(() => _renderer.UpdateSizing(isLargeScreen));
        }
    }

    private void OnPaintSurface(object? sender, SkiaSharp.Views.Maui.SKPaintSurfaceEventArgs e)
    {
        _renderer.Paint(e, _currentRenderList, _lastState.Viewport.ScrollY, (float)(Width > 0 ? Width : 360), _dragState);

        bool hasLoading;
        lock (_loadingLock) { hasLoading = _loadingImages.Count > 0; }
        if ((hasLoading || _dragState != null) && _isLoaded)
            _canvas.InvalidateSurface();
    }

    // ── Hit Testing ────────────────────────────────────────────────────

    private (string? uuid, int index) HitTest(float spacerX, float spacerY)
    {
        if (_currentRenderList == null) return (null, -1);
        foreach (var cmd in _currentRenderList.Commands)
        {
            if (cmd is DrawCardCommand draw && draw.Rect.Contains(spacerX, spacerY))
                return (draw.Card.Id.Value, draw.Index);
        }
        return (null, -1);
    }

    // ── Drag and Drop ──────────────────────────────────────────────────

    private void OnDragStarted(string uuid, int sourceIndex)
    {
        if (_lastState.Cards.IsDefaultOrEmpty || sourceIndex < 0 || sourceIndex >= _lastState.Cards.Length)
            return;

        var draggedCard = _lastState.Cards[sourceIndex];
        _dragState = new DragState(sourceIndex, sourceIndex, 0, 0, draggedCard);
        _canvas.InvalidateSurface();
    }

    private void OnDragMoved(float canvasX, float canvasY)
    {
        if (_dragState == null) return;

        int targetIndex = CalculateDragTargetIndex(canvasX, canvasY);
        _dragState = _dragState with { CanvasX = canvasX, CanvasY = canvasY, TargetIndex = targetIndex };
        _canvas.InvalidateSurface();
    }

    private void OnDragEnded()
    {
        if (_dragState == null) return;

        int from = _dragState.SourceIndex;
        int to = _dragState.TargetIndex;
        _dragState = null;

        if (from != to)
        {
            ApplyInMemoryReorder(from, to);
            CardReorderRequested?.Invoke(from, to);
        }

        _canvas.InvalidateSurface();
    }

    private void OnDragCancelled()
    {
        _dragState = null;
        _canvas.InvalidateSurface();
    }

    private int CalculateDragTargetIndex(float canvasX, float canvasY)
    {
        int count = _lastState.Cards.Length;
        if (count == 0) return 0;
        if (_currentRenderList.ViewMode == ViewMode.List || _currentRenderList.ViewMode == ViewMode.TextOnly)
        {
            float rowHeight = _currentRenderList.CardHeight > 0 ? _currentRenderList.CardHeight : 96f;
            int listRow = Math.Max(0, (int)(canvasY / rowHeight));
            return Math.Min(count - 1, listRow);
        }
        // Grid mode: reverse the layout formula
        var config = _lastState.Config;
        float width = _lastState.Viewport.Width > 0 ? _lastState.Viewport.Width : 360f;
        float availWidth = width - 20f;
        int columns = Math.Max(1, (int)((availWidth - config.CardSpacing) / (config.MinCardWidth + config.CardSpacing)));
        float cardWidth = (availWidth - config.CardSpacing * (columns + 1)) / columns;
        float cardHeight = cardWidth * config.CardImageRatio + config.LabelHeight;
        float rowHeight = cardHeight + config.CardSpacing;
        int row = Math.Max(0, (int)((canvasY - config.CardSpacing) / rowHeight));
        int col = Math.Max(0, (int)((canvasX - 10f - config.CardSpacing) / (cardWidth + config.CardSpacing)));
        col = Math.Min(col, columns - 1);
        return Math.Min(count - 1, row * columns + col);
    }

    private void ApplyInMemoryReorder(int fromIndex, int toIndex)
    {
        if (_lastState.Cards.IsDefaultOrEmpty) return;
        if (fromIndex < 0 || fromIndex >= _lastState.Cards.Length) return;
        if (toIndex < 0 || toIndex >= _lastState.Cards.Length) return;

        var builder = _lastState.Cards.ToBuilder();
        var card = builder[fromIndex];
        builder.RemoveAt(fromIndex);
        builder.Insert(toIndex, card);
        _lastState = _lastState with { Cards = builder.ToImmutable() };
        _stateChannel.Writer.TryWrite(_lastState);
    }

    // ── Cross-platform touch via AppoMobi.Maui.Gestures ──────────────────────
    // GestureSpacerView replaces both the Android SpacerTouchListener and the
    // non-Android PointerGestureRecognizer path.  It receives unified events on
    // all platforms from the AppoMobi library.
    //
    // Coordinate note: args.Location is in physical pixels (library docs).
    // Divide by TouchEffect.Density to get DIPs, matching GridLayoutEngine rects.
    private sealed class GestureSpacerView : BoxView, IGestureListener
    {
        private readonly CardGridGestureHandler _handler;

        public GestureSpacerView(CardGridGestureHandler handler)
        {
            _handler = handler;
            Color = Colors.Transparent;
            HeightRequest = 100;
            HorizontalOptions = LayoutOptions.Fill;
            VerticalOptions = LayoutOptions.Start;
            InputTransparent = false;

            // Force-attach the gesture effect and use Manual mode so WIllLock
            // can dynamically block/release the parent ScrollView during drag.
            TouchEffect.SetForceAttach(this, true);
            TouchEffect.SetShareTouch(this, TouchHandlingStyle.Manual);

            // Wire scroll-interception callbacks. These are called by the state
            // machine when the drag arms (Locked) and when it ends (Unlocked).
            _handler.DisallowScrollIntercept = () =>
            {
                var effect = TouchEffect.GetFrom(this);
                if (effect != null) effect.WIllLock = ShareLockState.Locked;
            };
            _handler.AllowScrollIntercept = () =>
            {
                var effect = TouchEffect.GetFrom(this);
                if (effect != null) effect.WIllLock = ShareLockState.Unlocked;
            };
        }

        // Explicit implementation avoids ambiguity with BoxView.InputTransparent.
        bool IGestureListener.InputTransparent => false;

        public void OnGestureEvent(
            TouchActionType type,
            TouchActionEventArgs args,
            TouchActionResult action)
        {
            float density = TouchEffect.Density > 0 ? TouchEffect.Density : 1f;
            float x = args.Location.X / density;
            float y = args.Location.Y / density;

            switch (action)
            {
                case TouchActionResult.Down:
                    _handler.HandleDown(x, y);
                    break;
                case TouchActionResult.Panning:
                    _handler.HandleMove(x, y);
                    break;
                case TouchActionResult.Up:
                    _handler.HandleUp();
                    break;
                    // Tapped: not used — PressTracking→HandleUp() fires Tapped to
                    //   avoid double-fire with the library's own Tapped result.
                    // LongPressing: not used — the handler's 500ms timer manages
                    //   DragArmed state and integrates with the drag-and-drop flow.
            }
        }
    }
}

