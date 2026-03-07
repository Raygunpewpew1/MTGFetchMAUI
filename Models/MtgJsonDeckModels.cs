using System.Text.Json.Serialization;

namespace AetherVault.Models;

/// <summary>
/// One entry from MTGJSON DeckList.json (catalog of available decks).
/// </summary>
public class MtgJsonDeckListEntry
{
    [JsonPropertyName("code")]
    public string Code { get; set; } = "";

    [JsonPropertyName("fileName")]
    public string FileName { get; set; } = "";

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("releaseDate")]
    public string? ReleaseDate { get; set; }

    [JsonPropertyName("type")]
    public string Type { get; set; } = "";
}

/// <summary>
/// Root of DeckList.json: has a "data" array of deck list entries.
/// </summary>
public class MtgJsonDeckListRoot
{
    [JsonPropertyName("data")]
    public List<MtgJsonDeckListEntry> Data { get; set; } = [];
}

/// <summary>
/// Optional identifiers on a deck card (for fallback lookup when uuid is missing in local DB).
/// </summary>
public class MtgJsonIdentifiers
{
    [JsonPropertyName("scryfallId")]
    public string? ScryfallId { get; set; }
}

/// <summary>
/// A single card entry in an MTGJSON deck (mainBoard, sideBoard, or commander).
/// uuid and count are required; name, setCode, identifiers used for fallback resolution.
/// </summary>
public class MtgJsonDeckCard
{
    [JsonPropertyName("uuid")]
    public string Uuid { get; set; } = "";

    [JsonPropertyName("count")]
    public int Count { get; set; } = 1;

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("setCode")]
    public string? SetCode { get; set; }

    [JsonPropertyName("identifiers")]
    public MtgJsonIdentifiers? Identifiers { get; set; }
}

/// <summary>
/// Root of a single-deck API response: deck object is under "data".
/// </summary>
public class MtgJsonDeckRoot
{
    [JsonPropertyName("data")]
    public MtgJsonDeck? Data { get; set; }
}

/// <summary>
/// Full deck JSON from MTGJSON (one deck file).
/// </summary>
public class MtgJsonDeck
{
    [JsonPropertyName("code")]
    public string Code { get; set; } = "";

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("type")]
    public string Type { get; set; } = "";

    [JsonPropertyName("releaseDate")]
    public string? ReleaseDate { get; set; }

    [JsonPropertyName("mainBoard")]
    public List<MtgJsonDeckCard> MainBoard { get; set; } = [];

    [JsonPropertyName("sideBoard")]
    public List<MtgJsonDeckCard> SideBoard { get; set; } = [];

    [JsonPropertyName("commander")]
    public List<MtgJsonDeckCard>? Commander { get; set; }

    [JsonPropertyName("displayCommander")]
    public List<MtgJsonDeckCard>? DisplayCommander { get; set; }
}
