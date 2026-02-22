using SkiaSharp;
using System.Collections.Immutable;

namespace MTGFetchMAUI.Core.Layout;

public abstract record RenderCommand;

public record DrawCardCommand(CardState Card, SKRect Rect, int Index) : RenderCommand;

public record RenderList(
    ImmutableArray<RenderCommand> Commands,
    float TotalHeight,
    int VisibleStart,
    int VisibleEnd,
    float CardWidth,
    float CardHeight
)
{
    public static RenderList Empty => new(
        ImmutableArray<RenderCommand>.Empty,
        0, 0, -1, 0, 0
    );
}

public static class GridLayoutEngine
{
    public static RenderList Calculate(GridState state)
    {
        var config = state.Config;
        var viewport = state.Viewport;
        var cards = state.Cards;

        float width = viewport.Width;
        if (width <= 0) width = 360f; // Fallback

        // 1. Calculate Columns
        float availWidth = width - 20f; // 10px padding each side
        int columns = Math.Max(1, (int)((availWidth - config.CardSpacing) / (config.MinCardWidth + config.CardSpacing)));

        // 2. Calculate Card Dimensions
        float cardWidth = (availWidth - config.CardSpacing * (columns + 1)) / columns;
        float cardHeight = cardWidth * config.CardImageRatio + config.LabelHeight;
        float rowHeight = cardHeight + config.CardSpacing;

        int count = cards.Length;
        if (count == 0)
        {
            return RenderList.Empty;
        }

        // 3. Calculate Total Height
        int rowCount = (int)Math.Ceiling((double)count / columns);
        float totalHeight = rowCount * rowHeight + config.CardSpacing + 50f;

        // 4. Calculate Visible Range
        // The Viewport.ScrollY is the position of the scroll view.
        // We render relative to this scroll position (sticky header pattern or just virtualized).
        // If we use the "Sticky Viewport" pattern, the SKGLView is translated by ScrollY.
        // So the visible area is from ScrollY to ScrollY + Viewport.Height.

        float effectiveOffset = Math.Max(0, viewport.ScrollY);
        float viewportHeight = viewport.Height > 0 ? viewport.Height : 1000f;

        int firstRow = Math.Max(0, (int)((effectiveOffset - config.CardSpacing) / rowHeight));
        int lastRow = (int)((effectiveOffset + viewportHeight + config.CardSpacing) / rowHeight);

        int visibleStart = Math.Max(0, Math.Min(count - 1, firstRow * columns));
        int visibleEnd = Math.Max(0, Math.Min(count - 1, (lastRow + 1) * columns - 1));

        if (visibleStart >= count)
        {
            return RenderList.Empty with { TotalHeight = totalHeight };
        }

        // 5. Generate Commands
        var commands = ImmutableArray.CreateBuilder<RenderCommand>(visibleEnd - visibleStart + 1);

        for (int i = visibleStart; i <= visibleEnd; i++)
        {
            var card = cards[i];

            int row = i / columns;
            int col = i % columns;

            float x = config.CardSpacing + col * (cardWidth + config.CardSpacing);
            float y = config.CardSpacing + row * (cardHeight + config.CardSpacing);

            var rect = new SKRect(x, y, x + cardWidth, y + cardHeight);

            commands.Add(new DrawCardCommand(card, rect, i));
        }

        return new RenderList(
            commands.ToImmutable(),
            totalHeight,
            visibleStart,
            visibleEnd,
            cardWidth,
            cardHeight
        );
    }
}
