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

    public async Task<ImportResult> ImportCsvAsync(Stream csvStream)
    {
        var result = new ImportResult();

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

        // Ensure Name column is found
        if (nameIdx == -1)
        {
            result.Errors.Add("Could not find 'Name' column in CSV header. Supported formats include Moxfield, Archidekt, CardSphere, Deckbox, Decked Builder, Deckstats, Helvault, ManaBox, TappedOut.");
            return result;
        }

        int lineNumber = 1;
        while (await csv.ReadAsync())
        {
            lineNumber++;

            string? name = csv.GetField(nameIdx)?.Trim();
            if (string.IsNullOrWhiteSpace(name)) continue;

            // Handle Deckstats inline set abbr (e.g. "Abrupt Decay [RTR]")
            string? extractedSet = null;
            if (setIdx == -1 && name.EndsWith("]"))
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

            // Match Logic: Name + Set Code match
            if (!string.IsNullOrWhiteSpace(set))
            {
                var helper = _cardRepo.CreateSearchHelper();
                helper.SearchCards()
                      .WhereNameContains(name)
                      .WherePrimarySideOnly()
                      .Limit(50);

                var candidates = await _cardRepo.SearchCardsAdvancedAsync(helper);

                var exactMatches = candidates.Where(c => c.Name.Equals(name, StringComparison.OrdinalIgnoreCase)).ToList();

                if (exactMatches.Count > 0)
                {
                    // Match by 3-letter set code or full set name
                    card = exactMatches.FirstOrDefault(c =>
                        c.SetCode.Equals(set, StringComparison.OrdinalIgnoreCase) ||
                        (c.SetName != null && c.SetName.Equals(set, StringComparison.OrdinalIgnoreCase)));

                    if (card == null)
                        card = exactMatches.First();
                }
                else if (candidates.Length > 0)
                {
                    card = candidates.First();
                }
            }

            // Match Logic: Name only fallback
            if (card == null)
            {
                var helper = _cardRepo.CreateSearchHelper();
                helper.SearchCards()
                      .WhereNameContains(name)
                      .WherePrimarySideOnly()
                      .Limit(50);

                var candidates = await _cardRepo.SearchCardsAdvancedAsync(helper);
                card = candidates.FirstOrDefault(c => c.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

                if (card == null && candidates.Length > 0)
                    card = candidates.First();
            }

            if (card != null && !string.IsNullOrEmpty(card.UUID))
            {
                if (foilQuantity > 0)
                {
                    await _cardManager.AddCardToCollectionAsync(card.UUID, foilQuantity, true, false);
                    result.SuccessCount++;
                    result.TotalCards += foilQuantity;

                    if (quantity > 0)
                    {
                        await _cardManager.AddCardToCollectionAsync(card.UUID, quantity, false, false);
                        result.SuccessCount++;
                        result.TotalCards += quantity;
                    }
                }
                else
                {
                    await _cardManager.AddCardToCollectionAsync(card.UUID, quantity, isFoil, false);
                    result.SuccessCount++;
                    result.TotalCards += quantity;
                }
            }
            else
            {
                result.Errors.Add($"Line {lineNumber}: Could not find card '{name}'" + (string.IsNullOrEmpty(set) ? "" : $" in set '{set}'"));
            }
        }

        return result;
    }
}
