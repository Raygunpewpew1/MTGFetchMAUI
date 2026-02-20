# Modernizing MTGCardGrid for MAUI 10 & C# Next

If tasked with rewriting the `MTGCardGrid` for a future-proof, high-performance .NET MAUI environment (conceptually "MAUI 10"), I would move away from the **Monolithic Control** pattern towards a **Composed Rendering System**.

The current implementation is performant but fragile: it mixes data management, layout logic, input handling, and rendering into a single class (`MTGCardGrid.cs`) protected by complex locks.

Here is how I would re-architect it.

---

## 1. Core Philosophy: "State -> Layout -> Render"

Instead of mutating state in place and hoping the render loop catches it, the new system uses a **unidirectional data flow**.

1.  **State**: An immutable snapshot of the data (Cards, Prices).
2.  **Layout**: A pure function that calculates where everything goes.
3.  **Render**: A dumb system that draws exactly what it's told.

### The Benefits
*   **Thread Safety**: No more `lock (_cardsLock)`. The render loop always operates on a "stable snapshot" of the data.
*   **Testability**: You can unit test the layout logic without creating a UI control.
*   **Performance**: Background threads can prepare the "Render List" (the scene graph) without blocking the UI thread.

---

## 2. Architecture Breakdown

### A. The State (Immutable Data)

Instead of a `List<GridCardData>` that is modified in place, we use immutable records.

```csharp
// 1. The Data Model (Immutable)
public readonly record struct CardId(string Value);

public record CardState(
    CardId Id,
    string Name,
    string SetCode,
    string ImageUrl,
    CardPrice? Price, // Immutable price data
    int Quantity
);

// 2. The Grid State (Snapshot)
public record GridState(
    ImmutableArray<CardState> Cards,
    GridConfig Config,
    Viewport Viewport
);
```

### B. The Layout Engine (Pure Logic)

This class knows nothing about Skia or MAUI. It just does math. It can be run on a background thread.

```csharp
public static class GridLayoutEngine
{
    public static RenderList Calculate(GridState state, float width)
    {
        // 1. Calculate columns based on width
        int columns = Math.Max(1, (int)((width - Padding) / (MinCardWidth + Spacing)));
        float cardWidth = ...;

        // 2. Calculate visible range (virtualization)
        var visibleRange = CalculateVisibleIndices(state.Cards.Length, columns, state.Viewport);

        // 3. Create lightweight render commands
        var commands = new List<RenderCommand>(visibleRange.Length);
        foreach (var index in visibleRange)
        {
            var card = state.Cards[index];
            var rect = CalculateCardRect(index, columns, cardWidth);
            commands.Add(new RenderCommand.DrawCard(card, rect));
        }

        return new RenderList(commands, TotalHeight: ...);
    }
}
```

### C. The Rendering Pipeline (The "Scene Graph")

The `SKGLView` becomes a dumb player. It holds a reference to the *current* `RenderList`.

```csharp
public class ModernCardGrid : SKGLView
{
    private RenderList _currentFrame;
    private readonly Channel<GridState> _stateChannel = Channel.CreateBounded<GridState>(1);

    public void UpdateState(GridState newState)
    {
        // Offload layout calculation to a worker thread!
        ThreadPool.QueueUserWorkItem(() => {
            var layout = GridLayoutEngine.Calculate(newState, this.Width);
            MainThread.BeginInvokeOnMainThread(() => {
                _currentFrame = layout;
                InvalidateSurface();
            });
        });
    }

    protected override void OnPaintSurface(SKPaintGLSurfaceEventArgs e)
    {
        // No locks needed! _currentFrame is an atomic reference swap.
        var frame = _currentFrame;
        if (frame == null) return;

        var canvas = e.Surface.Canvas;
        canvas.Clear(SKColors.Black);

        // Draw the pre-calculated commands
        foreach (var cmd in frame.Commands)
        {
            _renderer.Execute(canvas, cmd);
        }
    }
}
```

---

## 3. Key Modernizations

### 1. Lock-Free Concurrency with Channels
Instead of `lock(_cardsLock)`, we use `System.Threading.Channels` to queue updates. If the UI is slow, we can drop intermediate frames (backpressure) automatically.

### 2. Image Memory Management (The `ImageCacheService`)
The grid should **never** own `SKImage` objects. They are heavy unmanaged resources.
*   **Old Way**: `GridCardData` has a `public SKImage Image { get; set; }`.
*   **New Way**: The `Renderer` asks a service: `ImageCache.Get(card.Id)`.
    *   The service uses `IMemoryOwner<byte>` and strictly manages the GPU texture budget.
    *   It returns a strictly scoped `LeasedImage` that is disposed after the frame is drawn.

### 3. Native Scroll Integration
The current implementation manually handles scrolling via `TranslationY` and touch tracking. This feels "non-native" (no bounce, no snap).

**Hybrid Approach:**
*   Wrap the `SKGLView` in a native `ScrollView`.
*   Make the `SKGLView` extremely tall (the full height of the content).
*   **BUT** (Critical Optimization): Do not actually allocate a texture that big.
    *   Use `SKGLView`'s `IgnorePixelScaling` or similar to only draw the *viewport*.
    *   Or, use the **Sticky Header** pattern: The `SKGLView` is screen-sized, but we listen to `ScrollView.Scrolled`. When the user scrolls, we update the `Viewport` in our `GridState` and redraw.

This gives you native scroll physics (rubber banding on iOS) with Skia rendering speed.

### 4. Vectorization
For layout calculations of 10,000+ cards, use `System.Numerics.Vector<T>` (SIMD) to calculate positions in batches.
```csharp
// Example: Calculate Y positions for 4 cards at once
Vector<float> rowIndices = ...;
Vector<float> yPositions = rowIndices * rowHeight + padding;
```

---

## 4. Migration Plan

If you were to adopt this, I would recommend:

1.  **Phase 1: Extract the Layout Logic.**
    *   Take the `CalculateLayout` method from `MTGCardGrid.cs` and move it to a static pure function.
    *   Unit test it to ensure it handles resizing correctly.

2.  **Phase 2: Introduce the State Record.**
    *   Create `GridState` and replace the `List<GridCardData>` with `ImmutableArray<GridCardData>`.
    *   Update the setter to just swap the reference.

3.  **Phase 3: The Image Cache.**
    *   Create a dedicated `ImageCache` service.
    *   Make the `DrawCard` method request the image from the cache instead of the card object.

4.  **Phase 4: Full Reactive Rewrite.**
    *   Switch to the Channel-based architecture described above.

This approach prepares you for the future of .NET UI, where **Immutability** and **Composition** are king.
