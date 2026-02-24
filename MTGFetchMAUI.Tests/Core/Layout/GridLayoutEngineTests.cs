using MTGFetchMAUI.Core.Layout;
using System.Collections.Immutable;

namespace MTGFetchMAUI.Tests.Core.Layout;

public class GridLayoutEngineTests
{
    [Fact]
    public void Calculate_EmptyState_ReturnsEmpty()
    {
        var result = GridLayoutEngine.Calculate(GridState.Empty);
        Assert.Empty(result.Commands);
    }

    [Fact]
    public void Calculate_SingleCard_ReturnsCorrectMetrics()
    {
        // Viewport: 360 (default mobile width)
        var viewport = new Viewport(360f, 800f, 0f);
        var config = new GridConfig
        {
            MinCardWidth = 100f,
            CardSpacing = 8f,
            LabelHeight = 42f
        };

        // Setup state
        var card = new CardState(new CardId("1"), "Test Card", "TST", "1", "scry1", 0, false);
        var state = new GridState(
            ImmutableArray.Create(card),
            config,
            viewport
        );

        var result = GridLayoutEngine.Calculate(state);

        // Expected:
        // Avail width: 360 - 20 = 340
        // Max Columns: (340 - 8) / (100 + 8) = 332 / 108 = 3.07 -> 3 columns
        // Card Width: (340 - 8 * (3 + 1)) / 3 = (340 - 32) / 3 = 308 / 3 = 102.66

        // Assert.Equal(3, result.VisibleEnd - result.VisibleStart + 1); // Only 1 card, so 1 command?
        // Wait, VisibleStart=0, VisibleEnd=0 -> 1 item.

        Assert.Single(result.Commands);
        Assert.Equal(102.66f, result.CardWidth, 0.1f);

        var cmd = result.Commands[0] as DrawCardCommand;
        Assert.NotNull(cmd);
        Assert.Equal("1", cmd.Card.Id.Value);
    }

    [Fact]
    public void Calculate_NarrowSmallScreen_Returns3Columns()
    {
        // Samsung S24 can report ~332dp logical width when One UI applies display size scaling.
        // With MinCardWidth=100 the old threshold required >=352dp for 3 columns; 332dp gave 2.
        // With MinCardWidth=85 the threshold drops to ~292dp, so 332dp gives 3 columns.
        var viewport = new Viewport(332f, 800f, 0f);
        var config = new GridConfig
        {
            MinCardWidth = 85f,   // matches CardGrid.OnSizeAllocated for small screens
            CardSpacing = 8f,
            LabelHeight = 42f
        };

        var cards = Enumerable.Range(1, 6)
            .Select(i => new CardState(new CardId(i.ToString()), $"Card {i}", "TST", $"{i}", $"scry{i}", 0, false))
            .ToImmutableArray();
        var state = new GridState(cards, config, viewport);

        var result = GridLayoutEngine.Calculate(state);

        // availWidth = 332 - 20 = 312
        // columns = floor((312 - 8) / (85 + 8)) = floor(304 / 93) = 3
        // cardWidth = (312 - 8 * 4) / 3 = 280 / 3 = 93.33
        Assert.Equal(6, result.Commands.Length);
        Assert.Equal(93.33f, result.CardWidth, 0.1f);

        // Verify layout: cards 0,1,2 on row 0; cards 3,4,5 on row 1
        var cmds = result.Commands.OfType<DrawCardCommand>().ToList();
        float col0X = cmds[0].Rect.Left;
        float col1X = cmds[1].Rect.Left;
        float col2X = cmds[2].Rect.Left;
        float col3X = cmds[3].Rect.Left;

        Assert.True(col1X > col0X, "Card 1 should be right of card 0");
        Assert.True(col2X > col1X, "Card 2 should be right of card 1");
        Assert.Equal(col0X, col3X, 0.1f); // Card 3 wraps to row 2, same column as card 0
    }
}
