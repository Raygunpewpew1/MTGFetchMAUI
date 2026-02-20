using System.Collections.Immutable;
using System.Threading.Channels;
using MTGFetchMAUI.Core.Layout;
using MTGFetchMAUI.Services;
using MTGFetchMAUI.Models;
using SkiaSharp;
using SkiaSharp.Views.Maui;
using SkiaSharp.Views.Maui.Controls;

namespace MTGFetchMAUI.Controls;

public class ModernCardGrid : ContentView
{
    private readonly SKGLView _canvas;
    private readonly ScrollView _scrollView;
    private readonly BoxView _spacer;
    private readonly Channel<GridState> _stateChannel;
    private readonly CancellationTokenSource _cts = new();

    private ImageCacheService? _imageCache;
    private ImageDownloadService? _imageDownloadService;

    // State
    private GridState _lastState = GridState.Empty;
    private RenderList _currentRenderList = RenderList.Empty;
    private readonly HashSet<string> _loadingImages = new();
    private readonly object _loadingLock = new();

    // Events
    public event Action<string>? CardClicked;

    public ModernCardGrid()
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
            InputTransparent = true
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

        // Handle taps
        var tapGesture = new TapGestureRecognizer();
        tapGesture.Tapped += OnTapped;
        _spacer.GestureRecognizers.Add(tapGesture);

        var grid = new Grid();
        grid.Add(_canvas);
        grid.Add(_scrollView);

        Content = grid;

        // Start processing loop
        Task.Run(ProcessStateUpdates);

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object? sender, EventArgs e)
    {
        // Resolve services
        if (Handler?.MauiContext != null)
        {
            _imageCache = Handler.MauiContext.Services.GetService<ImageCacheService>();
            _imageDownloadService = Handler.MauiContext.Services.GetService<ImageDownloadService>();
        }
    }

    private void OnUnloaded(object? sender, EventArgs e)
    {
        _cts.Cancel();
        _stateChannel.Writer.Complete();
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
                // Calculate Layout (Pure)
                // We use MainThread width if possible, or state's viewport width
                // Note: GridLayoutEngine uses State.Viewport.Width

                var renderList = GridLayoutEngine.Calculate(state);

                // Update UI on Main Thread
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    _currentRenderList = renderList;

                    // Update Spacer Height
                    if (Math.Abs(_spacer.HeightRequest - renderList.TotalHeight) > 1)
                    {
                        _spacer.HeightRequest = renderList.TotalHeight;
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

    // ── Event Handlers ─────────────────────────────────────────────────

    private void OnScrolled(object? sender, ScrolledEventArgs e)
    {
        UpdateState(s => s with {
            Viewport = s.Viewport with { ScrollY = (float)e.ScrollY }
        });
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

        // e.GetPosition returns position relative to the element (_spacer)
        // _spacer is inside ScrollView.
        // If we tap, coordinates are relative to _spacer (0,0 is top-left of content).
        // This effectively includes ScrollY!

        var point = e.GetPosition(_spacer);
        if (point == null) return;

        float x = (float)point.Value.X;
        float y = (float)point.Value.Y;

        // Hit test
        foreach (var cmd in _currentRenderList.Commands)
        {
            if (cmd is DrawCardCommand draw && draw.Rect.Contains(x, y))
            {
                CardClicked?.Invoke(draw.Card.Id.Value);
                break;
            }
        }
    }

    // ── Rendering ──────────────────────────────────────────────────────

    private void OnPaintSurface(object? sender, SKPaintGLSurfaceEventArgs e)
    {
        var canvas = e.Surface.Canvas;
        var info = e.Info;

        canvas.Clear(new SKColor(18, 18, 18));

        var list = _currentRenderList;
        if (list == null || list.Commands.IsEmpty) return;

        // Scale
        float scale = info.Width / (float)(Width > 0 ? Width : 360);
        canvas.Scale(scale);

        // Translate
        // We are drawing "Sticky". The Canvas is fixed.
        // The Viewport.ScrollY tells us where we are.
        // The DrawCardCommand coordinates are absolute (0 to TotalHeight).
        // So we need to translate up by ScrollY.
        canvas.Translate(0, -_lastState.Viewport.ScrollY);

        // Draw
        using var paint = new SKPaint { IsAntialias = true };

        // Cache reuse? We should probably cache paints in a field like the old grid.
        // For brevity in this iteration, creating paints.
        // Optimization: Create fields.

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
        using var bgPaint = new SKPaint { Color = new SKColor(30, 30, 30), IsAntialias = true };
        canvas.DrawRoundRect(rect, 8f, 8f, bgPaint);

        // Image
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
                         try
                         {
                             // Use DownloadService to get from file/DB/network
                             SKImage? img = null;
                             if (_imageDownloadService != null)
                             {
                                 img = await _imageDownloadService.DownloadImageDirectAsync(card.ScryfallId);
                             }

                             if (img != null)
                             {
                                 _imageCache.AddToMemoryCache(card.ScryfallId, img);
                                 MainThread.BeginInvokeOnMainThread(() => _canvas.InvalidateSurface());
                             }
                         }
                         catch (Exception ex)
                         {
                             Console.WriteLine($"Image load failed: {ex.Message}");
                         }
                         finally
                         {
                             lock (_loadingLock) _loadingImages.Remove(card.ScryfallId);
                         }
                     });
                 }
             }
        }

        if (image != null)
        {
            var imageRect = new SKRect(rect.Left, rect.Top, rect.Right, rect.Top + rect.Width * 1.3968f);
            canvas.Save();
            canvas.ClipRoundRect(new SKRoundRect(imageRect, 8f, 8f), antialias: true);
            canvas.DrawImage(image, imageRect);
            canvas.Restore();
        }
        else
        {
            // Placeholder / Shimmer
        }

        // Text
        using var textPaint = new SKPaint { Color = SKColors.White, IsAntialias = true };
        using var textFont = new SKFont(SKTypeface.Default, 12f);
        float textY = rect.Top + rect.Width * 1.3968f + 16f;
        canvas.DrawText(card.Name, rect.Left + 4f, textY, SKTextAlign.Left, textFont, textPaint);

        // Price
        if (!string.IsNullOrEmpty(card.CachedDisplayPrice))
        {
            using var pricePaint = new SKPaint { Color = SKColors.LightGreen, IsAntialias = true };
            using var priceFont = new SKFont(SKTypeface.Default, 10f);
            canvas.DrawText(card.CachedDisplayPrice, rect.Right - 40f, rect.Bottom - 6f, SKTextAlign.Left, priceFont, pricePaint);
        }
    }
}
