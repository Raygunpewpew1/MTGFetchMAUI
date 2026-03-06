using AetherVault.Data;
using AetherVault.Models;
using CsvHelper;
using CsvHelper.Configuration;
using System.Globalization;

namespace AetherVault.Services.ImportExport;

public class ImportResult
{
    public int SuccessCount { get; set; }
    public int TotalCards { get; set; }
    public List<string> Errors { get; set; } = [];
}

public class CollectionImporter
{
    private readonly CardManager _cardManager;
    private readonly ICardRepository _cardRepo;

    public CollectionImporter(CardManager cardManager, ICardRepository cardRepo)
    {
        _cardManager = cardManager;
        _cardRepo = cardRepo;
    }

    public async Task<ImportResult> ImportCsvAsync(Stream csvStream, Action<string, int>? onProgress = null)
    {
        var result = new ImportResult();
        var cardsToAdd = new List<(string uuid, int quantity, bool isFoil, bool isEtched)>();
        var seenUuids = new Dictionary<string, int>(); // uuid → index in cardsToAdd, to deduplicate

        var config = new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            HasHeaderRecord = true,
            MissingFieldFound = null,
            HeaderValidated = null,
            IgnoreBlankLines = true,
            TrimOptions = TrimOptions.Trim,
        };

        using var reader = new StreamReader(csvStream);
        using var csv = new CsvReader(reader, config);

        if (!await csv.ReadAsync() || !csv.ReadHeader())
        {
            result.Errors.Add("Empty file or missing header");
            return result;
        }

        var headers = csv.HeaderRecord;
        if (headers == null || headers.Length == 0)
        {
            result.Errors.Add("Empty file or missing header");
            return result;
        }

        var lowerHeaders = headers.Select(h => h.ToLowerInvariant().Trim()).ToArray();

        // Find column indices
        int nameIdx = Array.IndexOf(lowerHeaders, "name");
        if (nameIdx == -1) nameIdx = Array.IndexOf(lowerHeaders, "card name"); // MTGO, Deckbox?

        int countIdx = Array.IndexOf(lowerHeaders, "count"); // Deckbox, Moxfield, Decked Builder???
        if (countIdx == -1) countIdx = Array.IndexOf(lowerHeaders, "quantity"); // MTGO, TCGplayer, ManaBox??
        if (countIdx == -1) countIdx = Array.IndexOf(lowerHeaders, "qty"); // MTG Studio, Helvault??
        if (countIdx == -1) countIdx = Array.IndexOf(lowerHeaders, "amount"); // Deckstats??
        if (countIdx == -1) countIdx = Array.IndexOf(lowerHeaders, "reg qty"); // Decked Builder???

        int setIdx = Array.IndexOf(lowerHeaders, "edition"); // Deckbox, MTG Studio, Archidekt (short)
        if (setIdx == -1) setIdx = Array.IndexOf(lowerHeaders, "edition (printing)"); // Archidekt full header
        if (setIdx == -1) setIdx = Array.IndexOf(lowerHeaders, "set"); // MTGO, Decked Builder, Moxfield, TappedOut
        if (setIdx == -1) setIdx = Array.IndexOf(lowerHeaders, "set code"); // Helvault, CardSphere, Dragon Shield
        if (setIdx == -1) setIdx = Array.IndexOf(lowerHeaders, "set name"); // Dragon Shield fallback
        if (setIdx == -1) setIdx = Array.IndexOf(lowerHeaders, "edition code"); // ManaBox

        int foilIdx = Array.IndexOf(lowerHeaders, "foil"); // Deckbox, MTG Studio, Moxfield <--
        if (foilIdx == -1) foilIdx = Array.IndexOf(lowerHeaders, "is foil"); // Helvault
        if (foilIdx == -1) foilIdx = Array.IndexOf(lowerHeaders, "premium"); // MTGO
        if (foilIdx == -1) foilIdx = Array.IndexOf(lowerHeaders, "foil qty"); // Decked Builder
        if (foilIdx == -1) foilIdx = Array.IndexOf(lowerHeaders, "printing"); // TCGplayer, ManaBox <--

        int scryfallIdx = Array.IndexOf(lowerHeaders, "scryfall id"); // Moxfield
        if (scryfallIdx == -1) scryfallIdx = Array.IndexOf(lowerHeaders, "scryfall_id");

        int numberIdx = Array.IndexOf(lowerHeaders, "collector number"); // Moxfield, Archidekt
        if (numberIdx == -1) numberIdx = Array.IndexOf(lowerHeaders, "card number"); // TCGplayer
        if (numberIdx == -1) numberIdx = Array.IndexOf(lowerHeaders, "number"); // Dragon Shield

        // Ensure Name or Scryfall ID column is found
        if (nameIdx == -1 && scryfallIdx == -1)
        {
            result.Errors.Add("Could not find 'Name' or 'Scryfall ID' column in CSV header. Supported formats include Moxfield, Archidekt, CardSphere, Deckbox, Decked Builder, Deckstats, Helvault, ManaBox, TappedOut.");
            return result;
        }

        onProgress?.Invoke("Preparing card lookup index...", 0);
        var resolver = new CardImportResolver(_cardRepo);
        var resolveSession = await resolver.CreateSessionAsync();

        int lineNumber = 1;
        while (await csv.ReadAsync())
        {
            lineNumber++;

            if (lineNumber % 250 == 0)
            {
                onProgress?.Invoke(
                    $"Importing row {lineNumber}... ({result.SuccessCount} unique cards / {result.TotalCards} total copies found so far)",
                    lineNumber);
            }

            string? scryfallId = scryfallIdx != -1 ? csv.GetField(scryfallIdx)?.Trim() : null;
            string? name = nameIdx != -1 ? csv.GetField(nameIdx)?.Trim() : null;
            string? number = numberIdx != -1 ? csv.GetField(numberIdx)?.Trim() : null;

            if (string.IsNullOrWhiteSpace(scryfallId) && string.IsNullOrWhiteSpace(name)) continue;

            // Handle Deckstats inline set abbr (e.g. "Abrupt Decay [RTR]")
            string? extractedSet = null;
            if (setIdx == -1 && !string.IsNullOrWhiteSpace(name) && name.EndsWith("]"))
            {
                int openBracket = name.LastIndexOf('[');
                if (openBracket != -1)
                {
                    extractedSet = name.Substring(openBracket + 1, name.Length - openBracket - 2).Trim();
                    name = name.Substring(0, openBracket).Trim();
                }
            }

            int quantity = 1;
            if (countIdx != -1)
            {
                var countStr = csv.GetField(countIdx)?.Trim();
                if (!string.IsNullOrWhiteSpace(countStr))
                {
                    int.TryParse(countStr, out quantity);
                }
            }
            if (quantity <= 0) continue;

            string? set = extractedSet;
            if (setIdx != -1)
            {
                var setStr = csv.GetField(setIdx)?.Trim();
                if (!string.IsNullOrWhiteSpace(setStr))
                {
                    set = setStr;
                }
            }

            bool isFoil = false;
            bool isEtched = false;
            int foilQuantity = 0;
            if (foilIdx != -1)
            {
                var foilVal = csv.GetField(foilIdx)?.Trim().ToLowerInvariant() ?? "";

                // Decked Builder: "foil qty" is a numeric column for foil copies
                if (lowerHeaders[foilIdx] == "foil qty")
                {
                    if (int.TryParse(foilVal, out foilQuantity) && foilQuantity > 0)
                    {
                        isFoil = true;
                    }
                }
                else if (lowerHeaders[foilIdx] == "printing")
                {
                    // TCGplayer / ManaBox: "foil" or "etched"
                    isFoil = foilVal == "foil";
                    isEtched = foilVal == "etched";
                }
                else
                {
                    // Moxfield and others: "foil", "etched", "true", "yes", "1"
                    isEtched = foilVal == "etched";
                    isFoil = !isEtched && (foilVal == "true" || foilVal == "yes" || foilVal == "1" || foilVal == "foil");
                }
            }

            var resolvedUuid = resolveSession.ResolveFromLookup(scryfallId, set, number, name);
            if (string.IsNullOrEmpty(resolvedUuid))
            {
                resolvedUuid = await resolveSession.ResolveFromFallbackAsync(scryfallId, set, number, name);
            }

            if (!string.IsNullOrEmpty(resolvedUuid))
            {
                // The collection schema has card_uuid as PRIMARY KEY, so only one entry per UUID.
                // When Decked Builder provides separate foil and non-foil quantities, combine them.
                int totalQty = foilQuantity > 0 ? foilQuantity + (quantity > 0 ? quantity : 0) : quantity;
                bool cardIsFoil = foilQuantity > 0 ? true : isFoil;
                bool cardIsEtched = isEtched;

                if (seenUuids.TryGetValue(resolvedUuid, out int existingIdx))
                {
                    // UUID already queued — accumulate quantity
                    var existing = cardsToAdd[existingIdx];
                    cardsToAdd[existingIdx] = (existing.uuid, existing.quantity + totalQty, existing.isFoil || cardIsFoil, existing.isEtched || cardIsEtched);
                    result.TotalCards += totalQty;
                }
                else
                {
                    seenUuids[resolvedUuid] = cardsToAdd.Count;
                    cardsToAdd.Add((resolvedUuid, totalQty, cardIsFoil, cardIsEtched));
                    result.SuccessCount++;
                    result.TotalCards += totalQty;
                }
            }
            else
            {
                var displayName = string.IsNullOrWhiteSpace(name) ? scryfallId ?? "(unknown)" : name;
                result.Errors.Add($"Line {lineNumber}: Could not find card '{displayName}'" + (string.IsNullOrEmpty(set) ? "" : $" in set '{set}'"));
            }
        }

        onProgress?.Invoke("Saving imported cards to your collection...", 0);

        // Add all cards using the bulk method
        if (cardsToAdd.Count > 0)
        {
            await _cardManager.AddCardsToCollectionBulkAsync(cardsToAdd);
        }

        onProgress?.Invoke(
            $"Import complete. Added {result.SuccessCount} unique cards ({result.TotalCards} total copies) to your collection.",
            result.TotalCards);

        return result;
    }
}
