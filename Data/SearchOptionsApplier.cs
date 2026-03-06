using AetherVault.Core;

namespace AetherVault.Data;

/// <summary>
/// Applies <see cref="SearchOptions"/> to an <see cref="MTGSearchHelper"/>.
/// Shared by SearchViewModel and CardSearchPickerViewModel.
/// </summary>
public static class SearchOptionsApplier
{
    public static void Apply(MTGSearchHelper helper, SearchOptions options)
    {
        if (!string.IsNullOrEmpty(options.NameFilter))
            helper.WhereNameContains(options.NameFilter);

        if (!string.IsNullOrEmpty(options.TextFilter))
            helper.WhereTextContains(options.TextFilter);

        if (!string.IsNullOrEmpty(options.TypeFilter) &&
            !options.TypeFilter.Equals("Any", StringComparison.OrdinalIgnoreCase))
            helper.WhereType(options.TypeFilter);

        if (!string.IsNullOrEmpty(options.SubtypeFilter))
            helper.WhereSubtype(options.SubtypeFilter);

        if (!string.IsNullOrEmpty(options.SupertypeFilter))
            helper.WhereSupertype(options.SupertypeFilter);

        if (!string.IsNullOrEmpty(options.ColorFilter))
            helper.WhereColors(options.ColorFilter);

        if (options.RarityFilter.Count > 0)
            helper.WhereRarity([.. options.RarityFilter]);

        if (!string.IsNullOrEmpty(options.SetFilter))
            helper.WhereSet(options.SetFilter);

        if (options.UseCMCExact)
            helper.WhereCMC(options.CMCExact);
        else if (options.UseCMCRange)
            helper.WhereCMCBetween(options.CMCMin, options.CMCMax);

        if (!string.IsNullOrEmpty(options.PowerFilter))
            helper.WherePower(options.PowerFilter);

        if (!string.IsNullOrEmpty(options.ToughnessFilter))
            helper.WhereToughness(options.ToughnessFilter);

        if (options.UseLegalFormat)
            helper.WhereLegalIn(options.LegalFormat);

        if (!string.IsNullOrEmpty(options.ArtistFilter))
            helper.WhereArtist(options.ArtistFilter);

        if (options.PrimarySideOnly)
            helper.WherePrimarySideOnly();
        else
            helper.IncludeAllFaces();

        if (options.NoVariations)
            helper.WhereNoVariations();

        if (options.IncludeAllFaces)
            helper.IncludeAllFaces();
    }
}
