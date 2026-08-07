using AetherVault.Data;
using CsvHelper;

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

        var config = CsvImportHelpers.CreateReaderConfiguration();

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

        var lowerHeaders = CsvImportHelpers.ToLowerHeaders(headers);

        // Find column indices (aliases cover Moxfield, Archidekt, Deckbox, MTGO, etc.)
        int nameIdx = CsvImportHelpers.FindHeader(lowerHeaders, "name", "card name");
        int countIdx = CsvImportHelpers.FindHeader(lowerHeaders, "count", "quantity", "qty", "amount", "reg qty");
        int setIdx = CsvImportHelpers.FindHeader(lowerHeaders,
            "edition", "edition (printing)", "set", "set code", "set name", "edition code");
        int foilIdx = CsvImportHelpers.FindHeader(lowerHeaders, "foil", "is foil", "premium", "foil qty", "printing");
        int scryfallIdx = CsvImportHelpers.FindHeader(lowerHeaders, "scryfall id", "scryfall_id");
        int numberIdx = CsvImportHelpers.FindHeader(lowerHeaders, "collector number", "card number", "number");

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
