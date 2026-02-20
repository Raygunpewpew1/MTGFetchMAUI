using Xunit;
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
}
