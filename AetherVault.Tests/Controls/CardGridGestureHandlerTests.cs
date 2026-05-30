using AetherVault.Controls;

namespace AetherVault.Tests.Controls;

public class CardGridGestureHandlerTests
{
    private const string TestUuid = "test-uuid-1";

    private sealed class ScrollContext
    {
        public double ScrollY;
        public CardGridGestureHandler Handler = null!;
        public TestDispatcher Dispatcher = null!;
        public List<string> Taps = null!;
    }

    private static ScrollContext CreateScrollContext(double initialScrollY = 0)
    {
        var ctx = new ScrollContext { ScrollY = initialScrollY };
        var dispatcher = new TestDispatcher();
        ctx.Dispatcher = dispatcher;
        ctx.Taps = [];
        ctx.Handler = new CardGridGestureHandler(
            dispatcher,
            (x, y) => x >= 10 && x <= 110 && y >= 10 && y <= 210 ? (TestUuid, 0) : (null, -1),
            () => ctx.ScrollY);
        ctx.Handler.Tapped += ctx.Taps.Add;
        return ctx;
    }

    [Fact]
    public void CleanTap_FiresImmediatelyWithoutDeferredFlush()
    {
        var ctx = CreateScrollContext();
        ctx.Handler.HandleDown(50, 50);
        ctx.Handler.HandleUp();

        Assert.Single(ctx.Taps);
        Assert.Equal(TestUuid, ctx.Taps[0]);
        ctx.Dispatcher.FlushDelayed();
        Assert.Single(ctx.Taps);
    }

    [Fact]
    public void ScrollNotifiedDuringPress_SuppressesTap()
    {
        var ctx = CreateScrollContext();
        ctx.Handler.HandleDown(50, 50);
        ctx.Handler.NotifyScrolled();
        ctx.Handler.HandleUp();
        ctx.Dispatcher.FlushDelayed();

        Assert.Empty(ctx.Taps);
    }

    [Fact]
    public void ScrollNotifiedAfterHandleUpBeforeDeferred_SuppressesTap()
    {
        var ctx = CreateScrollContext();
        ctx.Handler.HandleDown(50, 50);
        ctx.ScrollY = 2;
        ctx.Handler.HandleUp();

        ctx.Handler.NotifyScrolled();
        ctx.Dispatcher.FlushDelayed();

        Assert.Empty(ctx.Taps);
    }

    [Fact]
    public void VerticalPanIntent_SuppressesTap()
    {
        var ctx = CreateScrollContext();
        ctx.Handler.HandleDown(50, 50);
        ctx.Handler.HandleMove(50, 57);
        ctx.Handler.HandleUp();
        ctx.Dispatcher.FlushDelayed();

        Assert.Empty(ctx.Taps);
    }

    [Fact]
    public void ScrollYDeltaAtOrAboveThreshold_SuppressesTap()
    {
        var ctx = CreateScrollContext();
        ctx.Handler.HandleDown(50, 50);
        ctx.ScrollY = 10;
        ctx.Handler.HandleUp();
        ctx.Dispatcher.FlushDelayed();

        Assert.Empty(ctx.Taps);
    }

    [Fact]
    public void MovementBeyondTapSlop_SuppressesTap()
    {
        var ctx = CreateScrollContext();
        ctx.Handler.HandleDown(50, 50);
        ctx.Handler.HandleMove(70, 50);
        ctx.Handler.HandleUp();
        ctx.Dispatcher.FlushDelayed();

        Assert.Empty(ctx.Taps);
    }

    [Fact]
    public void LongPressWithoutDrag_FiresLongPressedNotTap()
    {
        var ctx = CreateScrollContext();
        var longPresses = new List<string>();
        ctx.Handler.LongPressed += longPresses.Add;
        ctx.Handler.IsDragEnabled = false;

        ctx.Handler.HandleDown(50, 50);
        ctx.Dispatcher.FireLastTimerTick();
        ctx.Handler.HandleUp();

        Assert.Empty(ctx.Taps);
        Assert.Single(longPresses);
        Assert.Equal(TestUuid, longPresses[0]);
    }

    [Fact]
    public void HandleCancel_DoesNotFireTap()
    {
        var ctx = CreateScrollContext();
        ctx.Handler.HandleDown(50, 50);
        ctx.Handler.HandleCancel();

        Assert.Empty(ctx.Taps);
    }

    [Fact]
    public void TinyScrollJitter_StillFiresImmediateTap()
    {
        var ctx = CreateScrollContext();
        ctx.Handler.HandleDown(50, 50);
        ctx.ScrollY = 1.5;
        ctx.Handler.HandleUp();

        Assert.Single(ctx.Taps);
        ctx.Dispatcher.FlushDelayed();
        Assert.Single(ctx.Taps);
    }

    [Fact]
    public void AmbiguousDeferredTap_StillFiresWhenScrollNeverCommits()
    {
        var ctx = CreateScrollContext();
        ctx.Handler.HandleDown(50, 50);
        ctx.ScrollY = 3;
        ctx.Handler.HandleUp();
        ctx.Dispatcher.FlushDelayed();

        Assert.Single(ctx.Taps);
        Assert.Equal(TestUuid, ctx.Taps[0]);
    }
}
