using AetherVault.Core;
using AetherVault.Models;

namespace AetherVault.Services.DeckBuilder;

/// <summary>Kind of build guidance shown as a "next step" chip in the deck editor.</summary>
public enum DeckNextStepKind
{
    ChooseCommander,
    AddCards,
    AddLands,
    RoleGap,
    TrimMain,
    TrimSideboard,
    Ready
}

/// <summary>Functional deck roles the advisor can flag as under-represented.</summary>
public enum DeckRole
{
    Ramp,
    CardDraw,
    Removal,
    BoardWipes
}

/// <summary>One actionable build hint (chip) for the deck editor.</summary>
public sealed record DeckNextStep(DeckNextStepKind Kind, string Title, DeckRole? Role = null);

/// <summary>Card-count progress toward the format's build target (main + commander for singleton formats).</summary>
public sealed record DeckBuildProgress(int Count, int Target, double Fraction, bool IsComplete, bool IsOverTarget)
{
    /// <summary>e.g. "87 / 100" for the editor header.</summary>
    public string DisplayText => $"{Count} / {Target}";
}

/// <summary>
/// Turns existing deck math (<see cref="DeckStats"/>, format rules, role heuristics) into an ordered list of
/// actionable next steps for the deck editor. Pure and stateless — covered by unit tests.
/// </summary>
public static class DeckNextStepAdvisor
{
    /// <summary>Land count auto-fill aims for (mirrors <see cref="DeckBuilderService.AutoSuggestLandsAsync"/>).</summary>
    public const int CommanderLikeLandTarget = 37;
    public const int ConstructedLandTarget = 24;

    /// <summary>Role targets mirror the gap boosts in <see cref="DeckSuggestionService"/> scoring.</summary>
    public const int RampTarget = 12;
    public const int CardDrawTarget = 10;
    public const int RemovalTarget = 10;
    public const int BoardWipeTarget = 3;

    /// <summary>Skip land advice until the deck has a few spells; skip role advice until it has taken shape.</summary>
    public const int MinCardsForLandAdvice = 10;
    public const int MinCardsForRoleAdvice = 30;

    /// <summary>At most this many role-gap chips at once (keeps the strip scannable).</summary>
    public const int MaxRoleSteps = 2;

    /// <summary>Build target: 100 for singleton commander formats, 60 minimum otherwise.</summary>
    public static int TargetCards(DeckFormat format) =>
        format.IsCommanderLikeRules()
            ? DeckValidationConstants.CommanderLikeDeckTargetCards
            : DeckValidationConstants.MinMainConstructedDeck;

    public static int LandTarget(DeckFormat format) =>
        format.IsCommanderLikeRules() ? CommanderLikeLandTarget : ConstructedLandTarget;

    /// <summary>Commander-like formats count commander cards toward the 100; constructed counts main only.</summary>
    public static DeckBuildProgress ComputeProgress(DeckFormat format, int mainCount, int commanderCount)
    {
        int target = TargetCards(format);
        int count = format.IsCommanderLikeRules() ? mainCount + commanderCount : mainCount;
        double fraction = target <= 0 ? 1 : Math.Clamp((double)count / target, 0, 1);
        bool over = format.IsCommanderLikeRules() && count > target;
        return new DeckBuildProgress(count, target, fraction, count >= target, over);
    }

    /// <summary>Ordered, capped list of next-step chips. Always returns at least one step (Ready when nothing to do).</summary>
    public static IReadOnlyList<DeckNextStep> GetNextSteps(
        DeckFormat format,
        DeckStats stats,
        int mainCount,
        int commanderCount,
        int sideboardCount)
    {
        var steps = new List<DeckNextStep>();
        bool commanderLike = format.IsCommanderLikeRules();
        var progress = ComputeProgress(format, mainCount, commanderCount);

        if (commanderLike && commanderCount == 0)
            steps.Add(new DeckNextStep(DeckNextStepKind.ChooseCommander, "Choose a commander"));

        if (progress.Count < progress.Target)
        {
            int missing = progress.Target - progress.Count;
            steps.Add(new DeckNextStep(
                DeckNextStepKind.AddCards,
                missing == 1 ? "Add 1 more card" : $"Add {missing} more cards"));
        }

        int landTarget = LandTarget(format);
        if (stats.TotalCards >= MinCardsForLandAdvice && stats.Lands < landTarget && progress.Count < progress.Target)
            steps.Add(new DeckNextStep(DeckNextStepKind.AddLands, $"Fill lands ({stats.Lands}/{landTarget})"));

        if (commanderLike && commanderCount > 0 && stats.TotalCards >= MinCardsForRoleAdvice)
        {
            (DeckRole role, int have, int want)[] gaps =
            [
                (DeckRole.Ramp, stats.RampCount, RampTarget),
                (DeckRole.CardDraw, stats.CardDrawCount, CardDrawTarget),
                (DeckRole.Removal, stats.RemovalCount, RemovalTarget),
                (DeckRole.BoardWipes, stats.BoardWipesCount, BoardWipeTarget),
            ];
            foreach (var (role, have, want) in gaps
                         .Where(g => g.have < g.want)
                         .OrderByDescending(g => (g.want - g.have) / (double)g.want)
                         .Take(MaxRoleSteps))
                steps.Add(new DeckNextStep(DeckNextStepKind.RoleGap, $"{RoleLabel(role)} ({have}/{want})", role));
        }

        if (commanderLike && progress.Count > progress.Target)
        {
            int extra = progress.Count - progress.Target;
            steps.Add(new DeckNextStep(
                DeckNextStepKind.TrimMain,
                extra == 1 ? "Cut 1 card" : $"Cut {extra} cards"));
        }

        if (!commanderLike && sideboardCount > DeckValidationConstants.MaxConstructedSideboardCards)
            steps.Add(new DeckNextStep(
                DeckNextStepKind.TrimSideboard,
                $"Trim sideboard ({sideboardCount}/{DeckValidationConstants.MaxConstructedSideboardCards})"));

        if (steps.Count == 0)
            steps.Add(new DeckNextStep(DeckNextStepKind.Ready, "Ready to play!"));

        return steps;
    }

    public static string RoleLabel(DeckRole role) => role switch
    {
        DeckRole.Ramp => "More ramp",
        DeckRole.CardDraw => "More card draw",
        DeckRole.Removal => "More removal",
        DeckRole.BoardWipes => "More board wipes",
        _ => "More"
    };
}
