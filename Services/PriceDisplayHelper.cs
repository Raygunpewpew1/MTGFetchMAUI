namespace AetherVault.Services;

/// <summary>
/// Central helper for resolving display and numeric prices using the user's vendor priority.
/// Reads/writes vendor order via Preferences; used by grid, card detail, and collection total.
/// </summary>
public static class PriceDisplayHelper
{
    private const string VendorPriorityKey = "PriceVendorPriority";
    private const string DefaultOrder = "TCGPlayer,Cardmarket,CardKingdom,ManaPool";

    private static readonly PriceVendor[] DefaultVendorOrder =
    [PriceVendor.TCGPlayer, PriceVendor.Cardmarket, PriceVendor.CardKingdom, PriceVendor.ManaPool];

    /// <summary>
    /// Gets the current vendor priority order from preferences.
    /// </summary>
    public static PriceVendor[] GetVendorPriority()
    {
        var stored = Preferences.Default.Get(VendorPriorityKey, DefaultOrder);
        if (string.IsNullOrWhiteSpace(stored)) return DefaultVendorOrder;

        var names = stored.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var result = new List<PriceVendor>();
        foreach (var name in names)
        {
            if (Enum.TryParse<PriceVendor>(name, ignoreCase: true, out var v))
                result.Add(v);
        }

        // Ensure all vendors appear; append any missing in default order
        foreach (var v in DefaultVendorOrder)
        {
            if (!result.Contains(v))
                result.Add(v);
        }

        return [.. result];
    }

    /// <summary>
    /// Sets the preferred (first) vendor; remaining vendors follow in default order.
    /// </summary>
    public static void SetPreferredVendor(PriceVendor first)
    {
        var rest = DefaultVendorOrder.Where(v => v != first).ToArray();
        SetVendorPriority([first, .. rest]);
    }

    /// <summary>
    /// Saves the vendor priority order. Order is used left-to-right when resolving prices.
    /// </summary>
    public static void SetVendorPriority(IReadOnlyList<PriceVendor> order)
    {
        if (order == null || order.Count == 0)
        {
            Preferences.Default.Remove(VendorPriorityKey);
            return;
        }
        var value = string.Join(",", order.Select(v => v.ToString()));
        Preferences.Default.Set(VendorPriorityKey, value);
    }

    /// <summary>
    /// Returns the display price string (e.g. "$12.34") for the first vendor with a valid price,
    /// using the user's vendor priority. Optionally prefers foil/etched for labeling.
    /// </summary>
    public static string GetDisplayPrice(CardPriceData? data, bool preferFoilLabel = false, bool preferEtchedLabel = false)
    {
        if (data == null) return "";
        var (price, isFoil, isEtched, currency) = GetNumericPriceAndFinish(data, false, false);
        if (price <= 0) return "";

        var suffix = "";
        if (preferEtchedLabel && isEtched) suffix = " (Etched)";
        else if (preferFoilLabel && isFoil) suffix = " (Foil)";

        return currency == PriceCurrency.EUR ? $"€{price:F2}{suffix}" : $"${price:F2}{suffix}";
    }

    /// <summary>
    /// Returns the numeric price for collection total: uses vendor priority and picks
    /// RetailNormal, RetailFoil, or RetailEtched based on the item's finish.
    /// </summary>
    public static double GetNumericPrice(CardPriceData? data, bool isFoil, bool isEtched)
    {
        if (data == null) return 0;
        var (price, _, _, _) = GetNumericPriceAndFinish(data, isFoil, isEtched);
        return price;
    }

    private static (double price, bool usedFoil, bool usedEtched, PriceCurrency currency) GetNumericPriceAndFinish(
        CardPriceData data, bool preferFoil, bool preferEtched)
    {
        var paper = data.Paper;
        var order = GetVendorPriority();

        foreach (var vendor in order)
        {
            var v = GetVendorPrices(paper, vendor);
            if (v == null || !v.IsValid) continue;

            var cur = v.Currency;

            // Prefer etched then foil then normal when matching collection item finish
            if (preferEtched && v.RetailEtched.Price > 0)
                return (v.RetailEtched.Price, false, true, cur);
            if (preferFoil && v.RetailFoil.Price > 0)
                return (v.RetailFoil.Price, true, false, cur);
            if (v.RetailNormal.Price > 0)
                return (v.RetailNormal.Price, false, false, cur);
            if (v.RetailFoil.Price > 0)
                return (v.RetailFoil.Price, true, false, cur);
            if (v.RetailEtched.Price > 0)
                return (v.RetailEtched.Price, false, true, cur);
        }

        return (0, false, false, PriceCurrency.USD);
    }

    private static VendorPrices? GetVendorPrices(PaperPlatform paper, PriceVendor vendor)
    {
        return vendor switch
        {
            PriceVendor.TCGPlayer => paper.TCGPlayer,
            PriceVendor.Cardmarket => paper.Cardmarket,
            PriceVendor.CardKingdom => paper.CardKingdom,
            PriceVendor.ManaPool => paper.ManaPool,
            _ => null
        };
    }
}
