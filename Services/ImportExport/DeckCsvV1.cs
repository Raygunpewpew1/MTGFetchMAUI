namespace AetherVault.Services.ImportExport;

internal static class DeckCsvV1
{
    public const string Version = "AetherVaultDeckCsvV1";

    // Required (for import)
    public const string DeckName = "Deck Name";
    public const string Format = "Format";
    public const string Section = "Section";
    public const string Quantity = "Quantity";

    // Strongly recommended for lossless round-trip
    public const string CardUuid = "Card UUID";

    // Optional (fallback resolution / human readability)
    public const string CardName = "Card Name";
    public const string SetCode = "Set Code";
    public const string CollectorNumber = "Collector Number";
    public const string ScryfallId = "Scryfall ID";

    // Optional metadata (for file identification)
    public const string Source = "Source";

    public static readonly string[] HeaderOrder =
    [
        Source,
        DeckName,
        Format,
        Section,
        Quantity,
        CardUuid,
        CardName,
        SetCode,
        CollectorNumber,
        ScryfallId
    ];

    public static class Sections
    {
        public const string Main = "Main";
        public const string Sideboard = "Sideboard";
        public const string Commander = "Commander";

        public static string Normalize(string? value)
        {
            var v = (value ?? "").Trim();
            if (v.Length == 0) return Main;

            // Accept common variants
            if (v.Equals("main", StringComparison.OrdinalIgnoreCase) ||
                v.Equals("maindeck", StringComparison.OrdinalIgnoreCase) ||
                v.Equals("main deck", StringComparison.OrdinalIgnoreCase))
            {
                return Main;
            }

            if (v.Equals("sideboard", StringComparison.OrdinalIgnoreCase) ||
                v.Equals("side", StringComparison.OrdinalIgnoreCase) ||
                v.Equals("sb", StringComparison.OrdinalIgnoreCase))
            {
                return Sideboard;
            }

            if (v.Equals("commander", StringComparison.OrdinalIgnoreCase) ||
                v.Equals("cmd", StringComparison.OrdinalIgnoreCase))
            {
                return Commander;
            }

            return v;
        }
    }
}

internal sealed class DeckCsvRowV1
{
    public string DeckName { get; set; } = "";
    public string? Format { get; set; }
    public string? Section { get; set; }
    public int Quantity { get; set; }

    public string? CardUuid { get; set; }
    public string? CardName { get; set; }
    public string? SetCode { get; set; }
    public string? CollectorNumber { get; set; }
    public string? ScryfallId { get; set; }
}

