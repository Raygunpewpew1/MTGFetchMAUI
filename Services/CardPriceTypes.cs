using System.Globalization;

namespace MTGFetchMAUI.Services;

/// <summary>
/// Price vendor type (normal vs foil).
/// Port of TPriceType from CardPriceTypes.pas.
/// </summary>
public enum PriceType { Normal, Foil }

/// <summary>
/// Price category (retail vs buylist).
/// Port of TPriceCategory from CardPriceTypes.pas.
/// </summary>
public enum PriceCategory { Retail, Buylist }

/// <summary>
/// Supported currencies.
/// Port of TCurrency from CardPriceTypes.pas.
/// </summary>
public enum PriceCurrency { USD, EUR }

/// <summary>
/// A single price data point with date.
/// Port of TPriceEntry from CardPriceTypes.pas.
/// </summary>
public readonly record struct PriceEntry(DateTime Date, double Price)
{
    public static readonly PriceEntry Empty = new(DateTime.MinValue, 0);
}

/// <summary>
/// Prices from a single vendor across retail/buylist and normal/foil.
/// Port of TVendorPrices from CardPriceTypes.pas.
/// </summary>
public record VendorPrices
{
    public PriceEntry RetailNormal { get; init; }
    public PriceEntry RetailFoil { get; init; }
    public PriceEntry BuylistNormal { get; init; }
    public PriceCurrency Currency { get; init; }

    public PriceEntry RetailEtched { get; init; }
    public PriceEntry BuylistEtched { get; init; }

    // Historical data
    public List<PriceEntry> RetailNormalHistory { get; init; } = [];
    public List<PriceEntry> RetailFoilHistory { get; init; } = [];
    public List<PriceEntry> RetailEtchedHistory { get; init; } = [];
    public List<PriceEntry> BuylistNormalHistory { get; init; } = [];
    public List<PriceEntry> BuylistEtchedHistory { get; init; } = [];

    public bool IsValid => RetailNormal.Price > 0 || RetailFoil.Price > 0 || RetailEtched.Price > 0;

    public static readonly VendorPrices Empty = new()
    {
        RetailNormal = PriceEntry.Empty,
        RetailFoil = PriceEntry.Empty,
        RetailEtched = PriceEntry.Empty,
        BuylistNormal = PriceEntry.Empty,
        BuylistEtched = PriceEntry.Empty,
        Currency = PriceCurrency.USD
    };
}

/// <summary>
/// Paper pricing across all supported vendors.
/// Port of TPaperPlatform from CardPriceTypes.pas.
/// </summary>
public record PaperPlatform
{
    public VendorPrices TCGPlayer { get; init; } = VendorPrices.Empty;
    public VendorPrices Cardmarket { get; init; } = VendorPrices.Empty;
    public VendorPrices CardKingdom { get; init; } = VendorPrices.Empty;
    public VendorPrices ManaPool { get; init; } = VendorPrices.Empty;
}

/// <summary>
/// Complete price data for a single card.
/// Port of TCardPriceData from CardPriceTypes.pas.
/// </summary>
public record CardPriceData
{
    public string UUID { get; init; } = "";
    public PaperPlatform Paper { get; init; } = new();
    public DateTime LastUpdated { get; init; }

    public static readonly CardPriceData Empty = new();
}

/// <summary>
/// MTGJSON metadata info (date and version).
/// Port of TMetaInfo from CardPriceTypes.pas.
/// </summary>
public record MetaInfo(string Date, string Version)
{
    public static readonly MetaInfo Empty = new("", "");
}

/// <summary>
/// Utility for parsing ISO 8601 date strings.
/// </summary>
public static class PriceDateParser
{
    public static DateTime ParseISO8601Date(string s)
    {
        if (DateTime.TryParseExact(s, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var result))
            return result;
        return DateTime.MinValue;
    }

    public static DateTime ParseCompactDate(string s)
    {
        if (DateTime.TryParseExact(s, "yyyyMMdd", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var result))
            return result;
        return DateTime.MinValue;
    }
}
