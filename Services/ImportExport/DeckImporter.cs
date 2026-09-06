using AetherVault.Core;
using AetherVault.Data;
using AetherVault.Services.DeckBuilder;
using CsvHelper;

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
    /// <summary>When import rows omit format, treat large lists as Commander (typical pasted EDH lists).</summary>
    private const int InferCommanderMinTotalCopies = 85;

    private readonly DeckBuilderService _deckService;
    private readonly ICardRepository _cardRepo;

    public DeckImporter(DeckBuilderService deckService, ICardRepository cardRepo)
    {
        _deckService = deckService;
        _cardRepo = cardRepo;
    }

    /// <summary>
    /// Buffers the stream, detects CSV vs TXT when the file name has no extension (Android pickers),
    /// then imports.
    /// </summary>
    public async Task<DeckImportResult> ImportFromFileStreamAsync(
        Stream source,
        string? fileName,
        Action<string, int>? onProgress = null,
        CancellationToken cancellationToken = default)
    {
        var buffer = await DeckImportFormatSniffer.BufferEntireStreamAsync(source, cancellationToken).ConfigureAwait(false);
        var kind = DeckImportFormatSniffer.DetectFormat(fileName, buffer);
        var suggestedName = Path.GetFileNameWithoutExtension(fileName ?? "") ?? "Imported deck";
        buffer.Position = 0;
        return kind == DeckImportFormatSniffer.DeckImportKind.Csv
            ? await ImportCsvAsync(buffer, onProgress).ConfigureAwait(false)
            : await ImportTxtAsync(buffer, suggestedName, onProgress).ConfigureAwait(false);
    }

    /// <summary>
    /// Imports a single deck from pre-built rows (e.g. URL download from Moxfield JSON).
    /// </summary>
    internal async Task<DeckImportResult> ImportPreparedDeckAsync(
        string deckName,
        string? formatText,
        IReadOnlyList<DeckCsvRowV1> rows,
        Action<string, int>? onProgress = null)
    {
        var result = new DeckImportResult();
        if (rows.Count == 0)
        {
            result.Errors.Add("No cards to import.");
            return result;
        }

        var baseName = string.IsNullOrWhiteSpace(deckName) ? "Imported deck" : deckName.Trim();
        var list = new List<DeckCsvRowV1>(rows.Count);
        foreach (var r in rows)
        {
            var copy = new DeckCsvRowV1
            {
                DeckName = baseName,
                Format = string.IsNullOrWhiteSpace(r.Format) ? formatText : r.Format,
                Section = r.Section,
                Quantity = r.Quantity,
                CardUuid = r.CardUuid,
                CardName = r.CardName,
                SetCode = r.SetCode,
                CollectorNumber = r.CollectorNumber,
                ScryfallId = r.ScryfallId,
            };
            list.Add(copy);
        }

        var grouped = new Dictionary<string, List<DeckCsvRowV1>>(StringComparer.OrdinalIgnoreCase)
        {
            [baseName] = list,
        };

        onProgress?.Invoke("Preparing card lookup index...", 0);
        var resolver = new CardImportResolver(_cardRepo);
        var resolveSession = await resolver.CreateSessionAsync().ConfigureAwait(false);
        await ImportGroupedDecksAsync(grouped, resolveSession, result, onProgress).ConfigureAwait(false);
        return result;
    }

    public async Task<DeckImportResult> ImportFromPlainTextAsync(string text, string suggestedDeckName, Action<string, int>? onProgress = null)
    {
        await using var ms = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(text ?? ""));
        return await ImportTxtAsync(ms, suggestedDeckName, onProgress).ConfigureAwait(false);
    }

    public async Task<DeckImportResult> ImportCsvAsync(Stream csvStream, Action<string, int>? onProgress = null)
    {
        var result = new DeckImportResult();

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

        int deckNameIdx = CsvImportHelpers.FindHeader(lowerHeaders, "deck name", "deck");
        int formatIdx = CsvImportHelpers.FindHeader(lowerHeaders, "format");
        int sectionIdx = CsvImportHelpers.FindHeader(lowerHeaders, "section");
        int qtyIdx = CsvImportHelpers.FindHeader(lowerHeaders, "quantity", "qty", "count", "amount");
        int uuidIdx = CsvImportHelpers.FindHeader(lowerHeaders, "card uuid", "uuid", "cardid", "card id");
        int cardNameIdx = CsvImportHelpers.FindHeader(lowerHeaders, "card name", "name");
        int setIdx = CsvImportHelpers.FindHeader(lowerHeaders, "set code", "set", "edition");
        int numberIdx = CsvImportHelpers.FindHeader(lowerHeaders, "collector number", "number", "card number");
        int scryfallIdx = CsvImportHelpers.FindHeader(lowerHeaders, "scryfall id", "scryfall_id");

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

        onProgress?.Invoke("Preparing card lookup index...", 0);
        var resolver = new CardImportResolver(_cardRepo);
        var resolveSession = await resolver.CreateSessionAsync();

        await ImportGroupedDecksAsync(grouped, resolveSession, result, onProgress);
        return result;
    }

    /// <summary>
    /// Imports a plain-text deck list (MTG Arena export, Moxfield TXT, etc.) as a single new deck.
    /// </summary>
    /// <param name="txtStream">UTF-8 text stream.</param>
    /// <param name="suggestedDeckName">Base name when the file has no <c>Name:</c> header (e.g. file name without extension).</param>
    public async Task<DeckImportResult> ImportTxtAsync(Stream txtStream, string suggestedDeckName, Action<string, int>? onProgress = null)
    {
        var result = new DeckImportResult();
        using var reader = new StreamReader(txtStream);
        var text = await reader.ReadToEndAsync();

        var lines = DeckTxtFormat.Parse(text, out var metaName);
        if (lines.Count == 0)
        {
            result.Errors.Add("No card lines found in text file.");
            return result;
        }

        var baseName = !string.IsNullOrWhiteSpace(metaName) ? metaName.Trim() : suggestedDeckName.Trim();
        if (baseName.Length == 0)
            baseName = "Imported deck";

        string? formatHint = InferTxtFormat(lines);
        var rows = new List<DeckCsvRowV1>(lines.Count);
        foreach (var line in lines)
        {
            rows.Add(new DeckCsvRowV1
            {
                DeckName = baseName,
                Format = formatHint,
                Section = line.Section,
                Quantity = line.Quantity,
                CardName = line.CardName,
                SetCode = line.SetCode,
                CollectorNumber = line.CollectorNumber,
            });
        }

        var grouped = new Dictionary<string, List<DeckCsvRowV1>>(StringComparer.OrdinalIgnoreCase)
        {
            [baseName] = rows,
        };

        onProgress?.Invoke("Preparing card lookup index...", 0);
        var resolver = new CardImportResolver(_cardRepo);
        var resolveSession = await resolver.CreateSessionAsync();

        await ImportGroupedDecksAsync(grouped, resolveSession, result, onProgress);
        return result;
    }

    private static string? InferTxtFormat(IReadOnlyList<DeckTxtFormat.Line> lines)
    {
        for (int i = 0; i < lines.Count; i++)
        {
            if (lines[i].Section.Equals(DeckCsvV1.Sections.Commander, StringComparison.OrdinalIgnoreCase))
                return DeckFormat.Commander.ToDbField();
        }

        return null;
    }

    private async Task ImportGroupedDecksAsync(
        Dictionary<string, List<DeckCsvRowV1>> grouped,
        CardImportResolver.Session resolveSession,
        DeckImportResult result,
        Action<string, int>? onProgress)
    {
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

            // Plain-text pastes often lack a Commander section; default Standard then rejects EDH cards.
            // 60+15 is max for tournament 60-card formats — above that, assume Commander.
            if (string.IsNullOrWhiteSpace(formatText) && format == DeckFormat.Standard)
            {
                int totalCopies = 0;
                for (int ri = 0; ri < rows.Count; ri++)
                {
                    int q = rows[ri].Quantity;
                    if (q > 0)
                        totalCopies += q;
                }

                if (totalCopies >= InferCommanderMinTotalCopies)
                    format = DeckFormat.Commander;
            }

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
                        var add = await _deckService.AddCardAsync(
                            deckId, uuid, 1, DeckCsvV1.Sections.Commander, skipLegalityCheck: true);
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

                // Trust import source (same as MTGJSON import); DB legality gaps should not block deck lists.
                var addResult = await _deckService.AddCardAsync(deckId, uuid, r.Quantity, section, skipLegalityCheck: true);
                if (addResult.IsError)
                {
                    var display = !string.IsNullOrWhiteSpace(r.CardName) ? r.CardName : uuid;
                    result.Errors.Add($"Deck '{deckName}': could not add {r.Quantity}× {display} to {section}: {addResult.Message}");
                    continue;
                }

                result.ImportedCards += r.Quantity;
            }
        }
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

