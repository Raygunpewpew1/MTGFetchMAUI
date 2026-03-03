using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using AetherVault.Models;
using AetherVault.Data;
using CsvHelper;
using CsvHelper.Configuration;

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

        int setIdx = Array.IndexOf(lowerHeaders, "edition"); // Deckbox, MTG Studio???
        if (setIdx == -1) setIdx = Array.IndexOf(lowerHeaders, "set"); // MTGO, Decked Builder, Moxfield? <--
        if (setIdx == -1) setIdx = Array.IndexOf(lowerHeaders, "set code"); // Helvault, CardSphere??
        if (setIdx == -1) setIdx = Array.IndexOf(lowerHeaders, "edition code"); // ManaBox???

        int foilIdx = Array.IndexOf(lowerHeaders, "foil"); // Deckbox, MTG Studio, Moxfield <--
        if (foilIdx == -1) foilIdx = Array.IndexOf(lowerHeaders, "is foil"); // Helvault??
        if (foilIdx == -1) foilIdx = Array.IndexOf(lowerHeaders, "premium"); // MTGO??
        if (foilIdx == -1) foilIdx = Array.IndexOf(lowerHeaders, "foil qty"); // Decked Builder
        if (foilIdx == -1) foilIdx = Array.IndexOf(lowerHeaders, "printing"); // TCGplayer, ManaBox <--

        int scryfallIdx = Array.IndexOf(lowerHeaders, "scryfall id"); // Moxfield
        if (scryfallIdx == -1) scryfallIdx = Array.IndexOf(lowerHeaders, "scryfall_id");

        int numberIdx = Array.IndexOf(lowerHeaders, "collector number"); // Moxfield
        if (numberIdx == -1) numberIdx = Array.IndexOf(lowerHeaders, "card number"); // TCGplayer

        // Ensure Name or Scryfall ID column is found
        if (nameIdx == -1 && scryfallIdx == -1)
        {
            result.Errors.Add("Could not find 'Name' or 'Scryfall ID' column in CSV header. Supported formats include Moxfield, Archidekt, CardSphere, Deckbox, Decked Builder, Deckstats, Helvault, ManaBox, TappedOut.");
            return result;
        }

        int lineNumber = 1;
        while (await csv.ReadAsync())
        {
            lineNumber++;

            if (lineNumber % 100 == 0)
                onProgress?.Invoke($"Parsing row {lineNumber}...", lineNumber);

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
            int foilQuantity = 0;
            if (foilIdx != -1)
            {
                var foilVal = csv.GetField(foilIdx)?.Trim().ToLowerInvariant() ?? "";

                // Decked Builder separation
                if (lowerHeaders[foilIdx] == "foil qty")
                {
                    if (int.TryParse(foilVal, out foilQuantity) && foilQuantity > 0)
                    {
                        isFoil = true;
                    }
                }
                else if (lowerHeaders[foilIdx] == "printing")
                {
                    isFoil = foilVal == "foil";
                }
                else
                {
                    isFoil = foilVal == "true" || foilVal == "yes" || foilVal == "1" || foilVal == "foil";
                }
            }

            Card? card = null;

            // Strategy 1: Scryfall ID (exact)
            if (!string.IsNullOrWhiteSpace(scryfallId))
            {
                var helper = _cardRepo.CreateSearchHelper();
                helper.SearchCards().WhereScryfallId(scryfallId).Limit(1);
                var matches = await _cardRepo.SearchCardsAdvancedAsync(helper);
                if (matches.Length > 0) card = matches[0];
            }

            // Strategy 2: Set Code + Collector Number (exact)
            if (card == null && !string.IsNullOrWhiteSpace(set) && !string.IsNullOrWhiteSpace(number))
            {
                var helper = _cardRepo.CreateSearchHelper();
                helper.SearchCards()
                      .WhereSet(set)
                      .WhereNumber(number)
                      .Limit(1);
                var matches = await _cardRepo.SearchCardsAdvancedAsync(helper);
                if (matches.Length > 0) card = matches[0];
            }

            // Strategy 3: Name + Set (exact name, primary face)
            if (card == null && !string.IsNullOrWhiteSpace(set) && !string.IsNullOrWhiteSpace(name))
            {
                var helper = _cardRepo.CreateSearchHelper();
                helper.SearchCards()
                      .WhereNameEquals(name)
                      .WherePrimarySideOnly();

                var matches = await _cardRepo.SearchCardsAdvancedAsync(helper);
                card = matches.FirstOrDefault(c =>
                    c.SetCode.Equals(set, StringComparison.OrdinalIgnoreCase) ||
                    (c.SetName != null && c.SetName.Equals(set, StringComparison.OrdinalIgnoreCase)));

                // If no exact set match, pick the first exact name match found in any set
                if (card == null && matches.Length > 0)
                    card = matches.First();
            }

            // Strategy 4: Name only fallback (exact name)
            if (card == null && !string.IsNullOrWhiteSpace(name))
            {
                var helper = _cardRepo.CreateSearchHelper();
                helper.SearchCards()
                      .WhereNameEquals(name)
                      .WherePrimarySideOnly()
                      .Limit(1);

                var matches = await _cardRepo.SearchCardsAdvancedAsync(helper);
                if (matches.Length > 0) card = matches[0];
            }

            // Strategy 5: Name partial fallback (contains, careful with this)
            if (card == null && !string.IsNullOrWhiteSpace(name))
            {
                var helper = _cardRepo.CreateSearchHelper();
                helper.SearchCards()
                      .WhereNameContains(name)
                      .WherePrimarySideOnly()
                      .Limit(50); // Get a bunch to try and find an exact match programmatically

                var candidates = await _cardRepo.SearchCardsAdvancedAsync(helper);
                card = candidates.FirstOrDefault(c => c.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

                if (card == null && candidates.Length > 0)
                {
                    // As an absolute last resort, just take the first candidate
                    card = candidates.First();
                }
            }


            if (card != null && !string.IsNullOrEmpty(card.UUID))
            {
                if (foilQuantity > 0)
                {
                    cardsToAdd.Add((card.UUID, foilQuantity, true, false));
                    result.SuccessCount++;
                    result.TotalCards += foilQuantity;

                    if (quantity > 0)
                    {
                        cardsToAdd.Add((card.UUID, quantity, false, false));
                        result.SuccessCount++;
                        result.TotalCards += quantity;
                    }
                }
                else
                {
                    cardsToAdd.Add((card.UUID, quantity, isFoil, false));
                    result.SuccessCount++;
                    result.TotalCards += quantity;
                }
            }
            else
            {
                result.Errors.Add($"Line {lineNumber}: Could not find card '{name}'" + (string.IsNullOrEmpty(set) ? "" : $" in set '{set}'"));
            }
        }

        onProgress?.Invoke("Saving to database...", 0);

        // Add all cards using the bulk method
        if (cardsToAdd.Count > 0)
        {
            await _cardManager.AddCardsToCollectionBulkAsync(cardsToAdd);
        }

        return result;
    }
}
