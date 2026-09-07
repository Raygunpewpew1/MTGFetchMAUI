using AetherVault.Core;
using AetherVault.Models;
using AetherVault.Services.DeckBuilder;

namespace AetherVault.Tests;

public class DeckNextStepAdvisorTests
{
    private static DeckStats Stats(int total = 0, int lands = 0, int ramp = 0, int draw = 0, int removal = 0, int wipes = 0) =>
        new()
        {
            TotalCards = total,
            Lands = lands,
            RampCount = ramp,
            CardDrawCount = draw,
            RemovalCount = removal,
            BoardWipesCount = wipes
        };

    // ── Progress ────────────────────────────────────────────────────

    [Fact]
    public void Progress_Commander_CountsCommanderTowardHundred()
    {
        var p = DeckNextStepAdvisor.ComputeProgress(DeckFormat.Commander, mainCount: 87, commanderCount: 1);
        Assert.Equal(88, p.Count);
        Assert.Equal(100, p.Target);
        Assert.False(p.IsComplete);
        Assert.False(p.IsOverTarget);
        Assert.Equal("88 / 100", p.DisplayText);
    }

    [Fact]
    public void Progress_Commander_ExactHundred_IsComplete()
    {
        var p = DeckNextStepAdvisor.ComputeProgress(DeckFormat.Commander, 99, 1);
        Assert.True(p.IsComplete);
        Assert.False(p.IsOverTarget);
        Assert.Equal(1.0, p.Fraction);
    }

    [Fact]
    public void Progress_Commander_OverHundred_IsOverTarget()
    {
        var p = DeckNextStepAdvisor.ComputeProgress(DeckFormat.Commander, 103, 1);
        Assert.True(p.IsOverTarget);
        Assert.Equal(1.0, p.Fraction);
    }

    [Fact]
    public void Progress_Standard_UsesSixtyMinimum_MainOnly()
    {
        var p = DeckNextStepAdvisor.ComputeProgress(DeckFormat.Standard, 60, 0);
        Assert.Equal(60, p.Count);
        Assert.Equal(60, p.Target);
        Assert.True(p.IsComplete);
        Assert.False(p.IsOverTarget); // 60 is a minimum, not a cap
    }

    [Fact]
    public void Progress_Standard_OverSixty_IsNotOverTarget()
    {
        var p = DeckNextStepAdvisor.ComputeProgress(DeckFormat.Standard, 75, 0);
        Assert.True(p.IsComplete);
        Assert.False(p.IsOverTarget);
    }

    // ── Next steps ──────────────────────────────────────────────────

    [Fact]
    public void EmptyCommanderDeck_SaysChooseCommanderFirst()
    {
        var steps = DeckNextStepAdvisor.GetNextSteps(DeckFormat.Commander, Stats(), 0, 0, 0);
        Assert.Equal(DeckNextStepKind.ChooseCommander, steps[0].Kind);
        Assert.Contains(steps, s => s.Kind == DeckNextStepKind.AddCards);
    }

    [Fact]
    public void EmptyStandardDeck_NoCommanderStep()
    {
        var steps = DeckNextStepAdvisor.GetNextSteps(DeckFormat.Standard, Stats(), 0, 0, 0);
        Assert.DoesNotContain(steps, s => s.Kind == DeckNextStepKind.ChooseCommander);
        Assert.Equal(DeckNextStepKind.AddCards, steps[0].Kind);
        Assert.Contains("60", steps[0].Title);
    }

    [Fact]
    public void AddCards_SingleMissingCard_UsesSingular()
    {
        var steps = DeckNextStepAdvisor.GetNextSteps(DeckFormat.Commander, Stats(total: 98, lands: 37), 98, 1, 0);
        var add = steps.First(s => s.Kind == DeckNextStepKind.AddCards);
        Assert.Equal("Add 1 more card", add.Title);
    }

    [Fact]
    public void LandAdvice_ShownOnceDeckHasSpells_AndBelowLandTarget()
    {
        var steps = DeckNextStepAdvisor.GetNextSteps(DeckFormat.Commander, Stats(total: 40, lands: 10), 40, 1, 0);
        var lands = steps.First(s => s.Kind == DeckNextStepKind.AddLands);
        Assert.Contains("10/37", lands.Title);
    }

    [Fact]
    public void LandAdvice_HiddenForBrandNewDeck()
    {
        var steps = DeckNextStepAdvisor.GetNextSteps(DeckFormat.Commander, Stats(total: 5), 5, 0, 0);
        Assert.DoesNotContain(steps, s => s.Kind == DeckNextStepKind.AddLands);
    }

    [Fact]
    public void LandAdvice_HiddenWhenDeckIsFull()
    {
        // 100 cards but only 30 lands: cutting is the real advice, not adding more lands.
        var steps = DeckNextStepAdvisor.GetNextSteps(DeckFormat.Commander, Stats(total: 99, lands: 30), 99, 1, 0);
        Assert.DoesNotContain(steps, s => s.Kind == DeckNextStepKind.AddLands);
    }

    [Fact]
    public void RoleGaps_CommanderDeckInProgress_FlagsBiggestGapsFirst_CappedAtTwo()
    {
        // Ramp 12/12 fine; draw 9/10 minor; removal 0/10 and wipes 0/3 are 100% gaps.
        var stats = Stats(total: 60, lands: 24, ramp: 12, draw: 9, removal: 0, wipes: 0);
        var steps = DeckNextStepAdvisor.GetNextSteps(DeckFormat.Commander, stats, 60, 1, 0);
        var roles = steps.Where(s => s.Kind == DeckNextStepKind.RoleGap).ToList();
        Assert.Equal(DeckNextStepAdvisor.MaxRoleSteps, roles.Count);
        Assert.All(roles, r => Assert.True(r.Role is DeckRole.Removal or DeckRole.BoardWipes));
    }

    [Fact]
    public void RoleGaps_NotShownWithoutCommander()
    {
        var stats = Stats(total: 60, lands: 24);
        var steps = DeckNextStepAdvisor.GetNextSteps(DeckFormat.Commander, stats, 60, 0, 0);
        Assert.DoesNotContain(steps, s => s.Kind == DeckNextStepKind.RoleGap);
    }

    [Fact]
    public void RoleGaps_NotShownForConstructedFormats()
    {
        var stats = Stats(total: 60, lands: 24);
        var steps = DeckNextStepAdvisor.GetNextSteps(DeckFormat.Modern, stats, 60, 0, 0);
        Assert.DoesNotContain(steps, s => s.Kind == DeckNextStepKind.RoleGap);
    }

    [Fact]
    public void OverfullCommanderDeck_SaysCutCards()
    {
        var steps = DeckNextStepAdvisor.GetNextSteps(DeckFormat.Commander, Stats(total: 104, lands: 38), 104, 1, 0);
        var trim = steps.First(s => s.Kind == DeckNextStepKind.TrimMain);
        Assert.Equal("Cut 5 cards", trim.Title);
    }

    [Fact]
    public void OversizedSideboard_Constructed_SaysTrim()
    {
        var steps = DeckNextStepAdvisor.GetNextSteps(DeckFormat.Standard, Stats(total: 77, lands: 24), 60, 0, 17);
        var trim = steps.First(s => s.Kind == DeckNextStepKind.TrimSideboard);
        Assert.Contains("17/15", trim.Title);
    }

    [Fact]
    public void FinishedCommanderDeck_IsReady()
    {
        var stats = Stats(total: 99, lands: 37, ramp: 12, draw: 10, removal: 10, wipes: 3);
        var steps = DeckNextStepAdvisor.GetNextSteps(DeckFormat.Commander, stats, 99, 1, 0);
        var only = Assert.Single(steps);
        Assert.Equal(DeckNextStepKind.Ready, only.Kind);
    }

    [Fact]
    public void FinishedStandardDeck_IsReady()
    {
        var steps = DeckNextStepAdvisor.GetNextSteps(DeckFormat.Standard, Stats(total: 60, lands: 24), 60, 0, 15);
        var only = Assert.Single(steps);
        Assert.Equal(DeckNextStepKind.Ready, only.Kind);
    }

    [Fact]
    public void LandTargets_MatchAutoSuggestLands()
    {
        Assert.Equal(37, DeckNextStepAdvisor.LandTarget(DeckFormat.Commander));
        Assert.Equal(24, DeckNextStepAdvisor.LandTarget(DeckFormat.Modern));
    }
}
