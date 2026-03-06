using AetherVault.Core;
using AetherVault.Data;
using AetherVault.Models;
using AetherVault.Services.DeckBuilder;
using CsvHelper;
using CsvHelper.Configuration;
using System.Globalization;

namespace AetherVault.Services.ImportExport;

public sealed class DeckImportResult
{
    public int ImportedDecks { get; set; }
    public int ImportedCards { get; set; }
    public List<string> Errors { get; set; } = [];
    public List<string> Warnings { get; set; } = [];
}

public class DeckImporter
{
    private readonly DeckBuilderService _deckService;
    private readonly ICardRepository _cardRepo;

    public DeckImporter(DeckBuilderService deckService, ICardRepository cardRepo)
    {
        _deckService = deckService;
        _cardRepo = cardRepo;
    }

    public async Task<DeckImportResult> ImportCsvAsync(Stream csvStream, Action<string, int>? onProgress = null)
    {
        var result = new DeckImportResult();

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

        int deckNameIdx = FindHeader(lowerHeaders, ["deck name", "deck"]);
        int formatIdx = FindHeader(lowerHeaders, ["format"]);
        int sectionIdx = FindHeader(lowerHeaders, ["section"]);
        int qtyIdx = FindHeader(lowerHeaders, ["quantity", "qty", "count", "amount"]);
        int uuidIdx = FindHeader(lowerHeaders, ["card uuid", "uuid", "cardid", "card id"]);
        int cardNameIdx = FindHeader(lowerHeaders, ["card name", "name"]);
        int setIdx = FindHeader(lowerHeaders, ["set code", "set", "edition"]);
        int numberIdx = FindHeader(lowerHeaders, ["collector number", "number", "card number"]);
        int scryfallIdx = FindHeader(lowerHeaders, ["scryfall id", "scryfall_id"]);

        if (deckNameIdx == -1)
        {
            result.Errors.Add("Could not find 'Deck Name' column in CSV header.");
            return result;
        }

        if (uuidIdx == -1 && cardNameIdx == -1 && scryfallIdx == -1)
        {
            result.Errors.Add("Could not find a card identifier column. Provide 'Card UUID' or at least 'Card Name'/'Scryfall ID'.");
            return result;
        }

        onProgress?.Invoke("Preparing card lookup index...", 0);
        var resolver = new CardImportResolver(_cardRepo);
        var resolveSession = await resolver.CreateSessionAsync();

        // Read rows, then group by deck name
        int lineNumber = 1;
        var grouped = new Dictionary<string, List<DeckCsvRowV1>>(StringComparer.OrdinalIgnoreCase);
        while (await csv.ReadAsync())
        {
            lineNumber++;

            var deckName = deckNameIdx != -1 ? (csv.GetField(deckNameIdx)?.Trim() ?? "") : "";
            if (string.IsNullOrWhiteSpace(deckName))
            {
                continue;
            }

            var row = new DeckCsvRowV1
            {
                DeckName = deckName,
                Format = formatIdx != -1 ? csv.GetField(formatIdx)?.Trim() : null,
                Section = sectionIdx != -1 ? csv.GetField(sectionIdx)?.Trim() : null,
                CardUuid = uuidIdx != -1 ? csv.GetField(uuidIdx)?.Trim() : null,
                CardName = cardNameIdx != -1 ? csv.GetField(cardNameIdx)?.Trim() : null,
                SetCode = setIdx != -1 ? csv.GetField(setIdx)?.Trim() : null,
                CollectorNumber = numberIdx != -1 ? csv.GetField(numberIdx)?.Trim() : null,
                ScryfallId = scryfallIdx != -1 ? csv.GetField(scryfallIdx)?.Trim() : null,
            };

            int qty = 1;
            if (qtyIdx != -1)
            {
                var qtyStr = csv.GetField(qtyIdx)?.Trim();
                if (!string.IsNullOrWhiteSpace(qtyStr))
                {
                    int.TryParse(qtyStr, out qty);
                }
            }
            row.Quantity = qty <= 0 ? 0 : qty;
            if (row.Quantity <= 0) continue;

            if (!grouped.TryGetValue(deckName, out var list))
            {
                list = [];
                grouped[deckName] = list;
            }
            list.Add(row);

            if (lineNumber % 250 == 0)
            {
                onProgress?.Invoke($"Reading CSV... ({lineNumber} rows)", lineNumber);
            }
        }

        if (grouped.Count == 0)
        {
            result.Errors.Add("No deck rows found in file.");
            return result;
        }

        var existingNames = new HashSet<string>(
            (await _deckService.GetDecksAsync()).Select(d => d.Name),
            StringComparer.OrdinalIgnoreCase);

        int deckIndex = 0;
        foreach (var kvp in grouped)
        {
            deckIndex++;
            string sourceDeckName = kvp.Key;
            var rows = kvp.Value;
            if (rows.Count == 0) continue;

            var formatText = rows.Select(r => r.Format).FirstOrDefault(f => !string.IsNullOrWhiteSpace(f))?.Trim();
            var format = EnumExtensions.ParseDeckFormat(formatText);

            string deckName = MakeUniqueName(sourceDeckName, existingNames);
            existingNames.Add(deckName);

            onProgress?.Invoke($"Importing deck {deckIndex}/{grouped.Count}: {deckName}", deckIndex);

            int deckId;
            try
            {
                deckId = await _deckService.CreateDeckAsync(deckName, format, description: "");
            }
            catch (Exception ex)
            {
                result.Errors.Add($"Deck '{sourceDeckName}': could not create deck: {ex.Message}");
                continue;
            }

            result.ImportedDecks++;

            // If a deck includes multiple commander rows, keep first successful commander set and import the rest as normal commander section cards.
            bool commanderSet = false;

            for (int i = 0; i < rows.Count; i++)
            {
                var r = rows[i];
                string section = DeckCsvV1.Sections.Normalize(r.Section);

                string? uuid = !string.IsNullOrWhiteSpace(r.CardUuid)
                    ? r.CardUuid.Trim()
                    : resolveSession.ResolveFromLookup(r.ScryfallId, r.SetCode, r.CollectorNumber, r.CardName);

                if (string.IsNullOrWhiteSpace(uuid))
                    uuid = await resolveSession.ResolveFromFallbackAsync(r.ScryfallId, r.SetCode, r.CollectorNumber, r.CardName);

                if (string.IsNullOrWhiteSpace(uuid))
                {
                    var display = !string.IsNullOrWhiteSpace(r.CardName) ? r.CardName : (r.ScryfallId ?? "(unknown)");
                    result.Errors.Add($"Deck '{deckName}': could not resolve card '{display}' (section {section}).");
                    continue;
                }

                if (section.Equals(DeckCsvV1.Sections.Commander, StringComparison.OrdinalIgnoreCase) && !commanderSet)
                {
                    if (r.Quantity != 1)
                    {
                        result.Warnings.Add($"Deck '{deckName}': commander row quantity was {r.Quantity}; using 1.");
                    }

                    var setCmd = await _deckService.SetCommanderAsync(deckId, uuid);
                    if (setCmd.IsError)
                    {
                        // Fall back to importing it as a commander-section card entry (so it isn't lost).
                        result.Warnings.Add($"Deck '{deckName}': could not set commander ({setCmd.Message}); importing as Commander section card instead.");
                        var add = await _deckService.AddCardAsync(deckId, uuid, 1, DeckCsvV1.Sections.Commander);
                        if (!add.IsSuccess)
                        {
                            result.Errors.Add($"Deck '{deckName}': could not add commander card: {add.Message}");
                            continue;
                        }
                    }
                    else if (setCmd.IsWarning && !string.IsNullOrWhiteSpace(setCmd.Message))
                    {
                        result.Warnings.Add($"Deck '{deckName}': {setCmd.Message}");
                    }

                    commanderSet = true;
                    result.ImportedCards += 1;
                    continue;
                }

                var addResult = await _deckService.AddCardAsync(deckId, uuid, r.Quantity, section);
                if (addResult.IsError)
                {
                    var display = !string.IsNullOrWhiteSpace(r.CardName) ? r.CardName : uuid;
                    result.Errors.Add($"Deck '{deckName}': could not add {r.Quantity}× {display} to {section}: {addResult.Message}");
                    continue;
                }

                result.ImportedCards += r.Quantity;
            }
        }

        return result;
    }

    private static int FindHeader(string[] lowerHeaders, string[] candidates)
    {
        for (int i = 0; i < candidates.Length; i++)
        {
            int idx = Array.IndexOf(lowerHeaders, candidates[i]);
            if (idx != -1) return idx;
        }
        return -1;
    }

    private static string MakeUniqueName(string baseName, HashSet<string> usedNames)
    {
        var name = baseName.Trim();
        if (name.Length == 0) name = "Imported deck";

        if (!usedNames.Contains(name)) return name;

        for (int i = 2; i < 1000; i++)
        {
            var candidate = $"{name} (import {i})";
            if (!usedNames.Contains(candidate)) return candidate;
        }

        // Extremely unlikely fallback
        return $"{name} (import {DateTime.Now:yyyyMMddHHmmss})";
    }
}

