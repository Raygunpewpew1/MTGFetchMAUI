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

    [Fact]
    public void Calculate_ZeroViewportWidth_DefaultsTo360()
    {
        // Zero width should fall back to 360f, producing the same layout as an explicit 360 viewport
        var viewportZero = new Viewport(0f, 800f, 0f);
        var viewport360 = new Viewport(360f, 800f, 0f);
        var config = new GridConfig { MinCardWidth = 100f, CardSpacing = 8f, LabelHeight = 42f };

        var card = new CardState(new CardId("1"), "Card", "TST", "1", "scry1", 0, false);
        var cards = ImmutableArray.Create(card);

        var resultZero = GridLayoutEngine.Calculate(new GridState(cards, config, viewportZero));
        var result360 = GridLayoutEngine.Calculate(new GridState(cards, config, viewport360));

        Assert.Equal(result360.CardWidth, resultZero.CardWidth, 0.1f);
        Assert.Equal(result360.Commands.Length, resultZero.Commands.Length);
    }

    [Fact]
    public void Calculate_NegativeScrollY_ClampsToZero()
    {
        // Negative scroll should behave exactly like scroll=0 (clamped by Math.Max)
        var config = new GridConfig { MinCardWidth = 100f, CardSpacing = 8f, LabelHeight = 42f };

        var cards = Enumerable.Range(1, 6)
            .Select(i => new CardState(new CardId(i.ToString()), $"Card {i}", "TST", $"{i}", $"scry{i}", 0, false))
            .ToImmutableArray();

        var stateNoScroll = new GridState(cards, config, new Viewport(360f, 800f, 0f));
        var stateNegScroll = new GridState(cards, config, new Viewport(360f, 800f, -500f));

        var resultNoScroll = GridLayoutEngine.Calculate(stateNoScroll);
        var resultNegScroll = GridLayoutEngine.Calculate(stateNegScroll);

        Assert.Equal(resultNoScroll.Commands.Length, resultNegScroll.Commands.Length);
        Assert.Equal(resultNoScroll.VisibleStart, resultNegScroll.VisibleStart);
        Assert.Equal(resultNoScroll.VisibleEnd, resultNegScroll.VisibleEnd);
    }

    [Fact]
    public void Calculate_ManyCards_AllCommandsGeneratedAtOnce()
    {
        // With a very tall viewport (9000px), all 100 cards should be in a single render pass
        var config = new GridConfig { MinCardWidth = 100f, CardSpacing = 8f, LabelHeight = 42f };
        var cards = Enumerable.Range(1, 100)
            .Select(i => new CardState(new CardId(i.ToString()), $"Card {i}", "TST", $"{i}", $"scry{i}", 0, false))
            .ToImmutableArray();

        var state = new GridState(cards, config, new Viewport(360f, 9000f, 0f));
        var result = GridLayoutEngine.Calculate(state);

        Assert.Equal(100, result.Commands.Length);
    }

    [Fact]
    public void Calculate_WideTabletViewport_GivesMoreColumns()
    {
        // 800dp wide with MinCardWidth=100 should generate more render commands than 360dp
        // because more cards fit in the visible area per row.
        // 360dp → columns = floor((340-8)/(100+8)) = 3
        // 800dp → columns = floor((780-8)/(100+8)) = 7
        var config = new GridConfig { MinCardWidth = 100f, CardSpacing = 8f, LabelHeight = 42f };
        var cards = Enumerable.Range(1, 10)
            .Select(i => new CardState(new CardId(i.ToString()), $"Card {i}", "TST", $"{i}", $"scry{i}", 0, false))
            .ToImmutableArray();

        // Use same tall viewport so all cards are visible in both cases
        var narrowState = new GridState(cards, config, new Viewport(360f, 9000f, 0f));
        var wideState = new GridState(cards, config, new Viewport(800f, 9000f, 0f));

        var narrowResult = GridLayoutEngine.Calculate(narrowState);
        var wideResult = GridLayoutEngine.Calculate(wideState);

        // Both render all 10 cards, but the wide layout puts 7 per row vs 3 per row
        Assert.Equal(10, narrowResult.Commands.Length);
        Assert.Equal(10, wideResult.Commands.Length);

        // On the wide layout, first-row cards are further to the right (card index 6 has a non-zero X)
        var narrowCmds = narrowResult.Commands.OfType<DrawCardCommand>().ToList();
        var wideCmds = wideResult.Commands.OfType<DrawCardCommand>().ToList();

        // Column 0 X should be the same (leftmost); column 6 should exist only on wide
        // Wide: card index 6 is still in row 0 (7 columns); narrow: it's in row 2
        float wideCard6Y = wideCmds[6].Rect.Top;
        float narrowCard6Y = narrowCmds[6].Rect.Top;
        Assert.True(wideCard6Y < narrowCard6Y,
            $"Card 6 should be higher (same row as card 0) on wide viewport. Wide y={wideCard6Y:F1}, Narrow y={narrowCard6Y:F1}");
    }

    [Fact]
    public void Calculate_ListMode_RowsSpanFullWidth()
    {
        var config = new GridConfig { MinCardWidth = 100f, CardSpacing = 8f, LabelHeight = 42f, ViewMode = ViewMode.List };
        var card = new CardState(new CardId("1"), "Card", "TST", "1", "scry1", 0, false);
        var state = new GridState(ImmutableArray.Create(card), config, new Viewport(360f, 800f, 0f));

        var result = GridLayoutEngine.Calculate(state);

        Assert.Single(result.Commands);
        var cmd = result.Commands[0] as DrawCardCommand;
        Assert.NotNull(cmd);
        // List rows span the full viewport width
        Assert.Equal(360f, cmd.Rect.Width, 0.1f);
    }

    [Fact]
    public void Calculate_TextOnlyMode_UsesTextRowHeight()
    {
        const float TextRowHeight = 50f;
        var config = new GridConfig { MinCardWidth = 100f, CardSpacing = 8f, LabelHeight = 42f, ViewMode = ViewMode.TextOnly };
        var card = new CardState(new CardId("1"), "Card", "TST", "1", "scry1", 0, false);
        var state = new GridState(ImmutableArray.Create(card), config, new Viewport(360f, 800f, 0f));

        var result = GridLayoutEngine.Calculate(state);

        Assert.Single(result.Commands);
        var cmd = result.Commands[0] as DrawCardCommand;
        Assert.NotNull(cmd);
        Assert.Equal(TextRowHeight, cmd.Rect.Height, 0.1f);
    }
}
