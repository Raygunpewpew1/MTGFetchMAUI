using AetherVault.Models;
using System.IO.Compression;
using System.Text.Json;

namespace AetherVault.Services;

/// <summary>
/// Fetches and caches the MTGJSON deck list catalog (DeckList.json) and individual deck JSON files.
/// </summary>
public class MtgJsonDeckListService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly string _cachePath;
    private List<MtgJsonDeckListEntry>? _cachedList;

    public MtgJsonDeckListService()
    {
        _cachePath = Path.Combine(AppDataManager.GetAppDataPath(), MtgConstants.MtgJsonDeckListCacheFile);
    }

    /// <summary>
    /// Gets the deck list catalog. Uses cached file if present and not forcing refresh.
    /// </summary>
    public async Task<IReadOnlyList<MtgJsonDeckListEntry>> GetDeckListAsync(bool forceRefresh = false, CancellationToken ct = default)
    {
        if (!forceRefresh && _cachedList != null)
            return _cachedList;

        if (!forceRefresh && File.Exists(_cachePath))
        {
            try
            {
                var json = await File.ReadAllTextAsync(_cachePath, ct);
                if (string.IsNullOrWhiteSpace(json))
                {
                    Logger.LogStuff("Cached DeckList empty.", LogLevel.Warning);
                }
                else
                {
                    var root = JsonSerializer.Deserialize<MtgJsonDeckListRoot>(json, JsonOptions);
                    _cachedList = root?.Data ?? [];
                    if (_cachedList.Count > 0)
                        return _cachedList;
                    Logger.LogStuff("Cached DeckList had no data entries; will re-download.", LogLevel.Warning);
                    try { File.Delete(_cachePath); } catch { /* ignore */ }
                }
            }
            catch (Exception ex)
            {
                Logger.LogStuff($"Failed to read cached DeckList: {ex.Message}", LogLevel.Warning);
                try { File.Delete(_cachePath); } catch { /* ignore */ }
            }
        }

        await DownloadAndCacheDeckListAsync(ct);
        return _cachedList ?? [];
    }

    private async Task DownloadAndCacheDeckListAsync(CancellationToken ct)
    {
        // Try zip first, then fall back to raw JSON (more reliable on some networks/devices).
        var json = await TryDownloadZipAsync(ct);
        if (string.IsNullOrEmpty(json))
            json = await TryDownloadJsonAsync(ct);

        if (string.IsNullOrEmpty(json))
        {
            _cachedList = [];
            return;
        }

        try
        {
            var root = JsonSerializer.Deserialize<MtgJsonDeckListRoot>(json, JsonOptions);
            _cachedList = root?.Data ?? [];
            if (_cachedList.Count == 0)
                Logger.LogStuff("DeckList parsed but data array empty.", LogLevel.Warning);
            await File.WriteAllTextAsync(_cachePath, json, ct);
        }
        catch (Exception ex)
        {
            Logger.LogStuff($"Failed to parse DeckList: {ex.Message}", LogLevel.Error);
            _cachedList = [];
        }
    }

    private async Task<string?> TryDownloadZipAsync(CancellationToken ct)
    {
        try
        {
            using var client = NetworkHelper.CreateHttpClient(TimeSpan.FromSeconds(60));
            using var response = await client.GetAsync(MtgConstants.MtgJsonDeckListUrl, HttpCompletionOption.ResponseHeadersRead, ct);
            response.EnsureSuccessStatusCode();

            await using var zipStream = await response.Content.ReadAsStreamAsync(ct);
            using var archive = new ZipArchive(zipStream, ZipArchiveMode.Read);

            // Zip may have entry name "DeckList.json" or path "v5/DeckList.json" etc.
            var entry = archive.Entries.FirstOrDefault(e =>
                e.Name.Equals("DeckList.json", StringComparison.OrdinalIgnoreCase));

            if (entry == null)
            {
                var names = string.Join(", ", archive.Entries.Take(5).Select(e => e.FullName));
                if (archive.Entries.Count > 5) names += ", ...";
                Logger.LogStuff($"DeckList.json not in zip. Entry names: [{names}]", LogLevel.Warning);
                return null;
            }

            await using var entryStream = entry.Open();
            using var reader = new StreamReader(entryStream);
            return await reader.ReadToEndAsync(ct);
        }
        catch (Exception ex)
        {
            Logger.LogStuff($"DeckList zip download failed: {ex.Message}", LogLevel.Warning);
            return null;
        }
    }

    private async Task<string?> TryDownloadJsonAsync(CancellationToken ct)
    {
        try
        {
            using var client = NetworkHelper.CreateHttpClient(TimeSpan.FromSeconds(60));
            var json = await client.GetStringAsync(MtgConstants.MtgJsonDeckListJsonUrl, ct);
            if (!string.IsNullOrWhiteSpace(json))
                Logger.LogStuff("DeckList loaded via direct JSON URL.", LogLevel.Info);
            return json;
        }
        catch (Exception ex)
        {
            Logger.LogStuff($"DeckList JSON download failed: {ex.Message}", LogLevel.Error);
            return null;
        }
    }

    /// <summary>
    /// Parses MTGJSON deck from JSON text. Handles both wrapped format
    /// ({"data": { "name", "mainBoard", "sideBoard", ... }, "meta": {...}}) and raw deck object at root.
    /// If normal deserialization yields no cards, falls back to raw JsonDocument parsing to catch naming/structure quirks.
    /// </summary>
    public MtgJsonDeck? ParseDeckFromJson(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;
        try
        {
            var root = JsonSerializer.Deserialize<MtgJsonDeckRoot>(json, JsonOptions);
            var deck = root?.Data ?? JsonSerializer.Deserialize<MtgJsonDeck>(json, JsonOptions);
            if (deck == null)
                return null;
            var totalCards = (deck.MainBoard?.Count ?? 0) + (deck.SideBoard?.Count ?? 0) +
                            (deck.Commander?.Count ?? 0) + (deck.DisplayCommander?.Count ?? 0);
            if (TryParseDeckCardsFromRawJson(json, out var main, out var side, out var commander))
            {
                var rawTotal = main.Count + side.Count + commander.Count;
                if (rawTotal > totalCards || totalCards == 0)
                {
                    deck.MainBoard = main;
                    deck.SideBoard = side;
                    deck.Commander = commander.Count > 0 ? commander : deck.Commander;
                    deck.DisplayCommander = null;
                    if (rawTotal != totalCards)
                        Logger.LogStuff($"Deck cards from raw parse: {main.Count} main, {side.Count} side, {commander.Count} commander (was {totalCards}).", LogLevel.Info);
                }
            }
            return deck;
        }
        catch (JsonException ex)
        {
            Logger.LogStuff($"ParseDeckFromJson failed: {ex.Message}", LogLevel.Warning);
            return null;
        }
    }

    /// <summary>
    /// Extracts card arrays from deck JSON using JsonDocument so we never miss cards due to property naming (e.g. main_board).
    /// Tries both camelCase and snake_case. Returns true if at least one card was found.
    /// </summary>
    private static bool TryParseDeckCardsFromRawJson(string json,
        out List<MtgJsonDeckCard> mainBoard,
        out List<MtgJsonDeckCard> sideBoard,
        out List<MtgJsonDeckCard> commander)
    {
        mainBoard = [];
        sideBoard = [];
        commander = [];
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            JsonElement dataEl = default;
            if (root.TryGetProperty("data", out var dataProp))
                dataEl = dataProp;
            else
                dataEl = root;

            void AddCardsFromArray(JsonElement arr, List<MtgJsonDeckCard> into)
            {
                if (arr.ValueKind != JsonValueKind.Array) return;
                foreach (var el in arr.EnumerateArray())
                {
                    var card = ParseDeckCardElement(el);
                    if (card != null)
                        into.Add(card);
                }
            }

            foreach (var name in new[] { "mainBoard", "main_board" })
            {
                if (dataEl.TryGetProperty(name, out var arr))
                {
                    AddCardsFromArray(arr, mainBoard);
                    break;
                }
            }
            foreach (var name in new[] { "sideBoard", "side_board" })
            {
                if (dataEl.TryGetProperty(name, out var arr))
                {
                    AddCardsFromArray(arr, sideBoard);
                    break;
                }
            }
            foreach (var name in new[] { "commander" })
            {
                if (dataEl.TryGetProperty(name, out var arr))
                    AddCardsFromArray(arr, commander);
            }

            return mainBoard.Count + sideBoard.Count + commander.Count > 0;
        }
        catch (Exception ex)
        {
            Logger.LogStuff($"Raw deck parse failed: {ex.Message}", LogLevel.Warning);
            return false;
        }
    }

    private static MtgJsonDeckCard? ParseDeckCardElement(JsonElement el)
    {
        var card = new MtgJsonDeckCard();
        if (el.TryGetProperty("uuid", out var u)) card.Uuid = u.GetString() ?? "";
        if (el.TryGetProperty("count", out var c)) card.Count = c.TryGetInt32(out var n) ? n : 1;
        if (el.TryGetProperty("name", out var name)) card.Name = name.GetString();
        if (string.IsNullOrEmpty(card.Name) && el.TryGetProperty("faceName", out var fn)) card.Name = fn.GetString();
        if (el.TryGetProperty("setCode", out var set)) card.SetCode = set.GetString();
        if (string.IsNullOrEmpty(card.SetCode) && el.TryGetProperty("set_code", out var set2)) card.SetCode = set2.GetString();
        if (el.TryGetProperty("identifiers", out var ids))
        {
            var sf = ids.TryGetProperty("scryfallId", out var s1) ? s1.GetString() : null;
            if (string.IsNullOrEmpty(sf) && ids.TryGetProperty("scryfall_id", out var s2)) sf = s2.GetString();
            if (!string.IsNullOrEmpty(sf)) card.Identifiers = new MtgJsonIdentifiers { ScryfallId = sf };
        }
        if (string.IsNullOrEmpty(card.Uuid) && string.IsNullOrEmpty(card.Name))
            return null;
        return card;
    }

    /// <summary>
    /// Fetches a single deck by its fileName (e.g. "Commander_2021_Arcane_Maelstrom_C21" or "ShawnHammerRegnierQuarterfinalist_PTC").
    /// </summary>
    public async Task<MtgJsonDeck?> GetDeckAsync(string fileName, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            return null;

        var name = fileName.TrimEnd();
        if (!name.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            name += ".json";

        var url = MtgConstants.MtgJsonDeckBaseUrl + name;
        try
        {
            using var client = NetworkHelper.CreateHttpClient(TimeSpan.FromSeconds(30));
            var json = await client.GetStringAsync(url, ct);
            return ParseDeckFromJson(json);
        }
        catch (Exception ex)
        {
            Logger.LogStuff($"Failed to fetch deck {fileName}: {ex.Message}", LogLevel.Warning);
            return null;
        }
    }
}
