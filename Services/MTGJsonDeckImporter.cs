using AetherVault.Core;
using AetherVault.Data;
using AetherVault.Models;
using AetherVault.Services.DeckBuilder;

namespace AetherVault.Services;

/// <summary>
/// Result of importing a single MTGJSON deck into the app.
/// </summary>
public sealed class MtgJsonDeckImportResult
{
    public int DeckId { get; set; }
    public int CardsAdded { get; set; }
    public List<string> MissingUuids { get; set; } = [];
    public bool Success => DeckId > 0;
}

/// <summary>
/// Imports an MTGJSON deck (mainBoard, sideBoard, commander) into the app by resolving UUIDs
/// against the local card DB and creating a new deck via DeckBuilderService.
/// </summary>
public class MtgJsonDeckImporter
{
    private readonly DeckBuilderService _deckService;
    private readonly ICardRepository _cardRepo;

    public MtgJsonDeckImporter(DeckBuilderService deckService, ICardRepository cardRepo)
    {
        _deckService = deckService;
        _cardRepo = cardRepo;
    }

    private static bool SupportsMultipleCommanders(Card? commanderCard)
    {
        if (commanderCard == null)
            return false;

        // Keep this intentionally permissive for valid multi-commander mechanics.
        return commanderCard.HasKeyword("Partner")
            || commanderCard.Text.Contains("Partner", StringComparison.OrdinalIgnoreCase)
            || commanderCard.Text.Contains("choose a Background", StringComparison.OrdinalIgnoreCase)
            || commanderCard.Text.Contains("Doctor's companion", StringComparison.OrdinalIgnoreCase)
            || commanderCard.Text.Contains("Friends forever", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Maps MTGJSON deck type strings to the closest internal DeckFormat.
    /// MTGJSON uses type names like "Theme Deck", "Commander", "Preconstructed", etc.
    /// Competitive format names match directly; everything else defaults to Legacy
    /// (permissive legality, 4-of limit) which suits casual precon products.
    /// </summary>
    private static DeckFormat MapMtgJsonDeckType(string? deckType)
    {
        if (string.IsNullOrWhiteSpace(deckType))
            return DeckFormat.Legacy;

        return deckType.Trim().ToLowerInvariant() switch
        {
            // MTGJSON uses multi-word strings for product types; match both forms defensively.
            "commander" or "commander deck" => DeckFormat.Commander,
            "brawl" or "brawl deck" => DeckFormat.Brawl,
            "standard brawl" or "standardbrawl" => DeckFormat.StandardBrawl,
            "pauper commander" or "paupercommander"
                or "pauper commander deck" => DeckFormat.PauperCommander,
            "oathbreaker" or "oathbreaker deck" => DeckFormat.Oathbreaker,
            "duel" or "duel commander" or "duel deck" => DeckFormat.Duel,
            "standard" => DeckFormat.Standard,
            "pioneer" => DeckFormat.Pioneer,
            "modern" => DeckFormat.Modern,
            "legacy" => DeckFormat.Legacy,
            "vintage" => DeckFormat.Vintage,
            "pauper" => DeckFormat.Pauper,
            "historic" => DeckFormat.Historic,
            "alchemy" => DeckFormat.Alchemy,
            "timeless" => DeckFormat.Timeless,
            "gladiator" => DeckFormat.Gladiator,
            "premodern" => DeckFormat.Premodern,
            "penny" or "penny dreadful" => DeckFormat.Penny,
            // All other MTGJSON product types — Theme Deck, Planeswalker Deck, Starter Kit,
            // Two-Headed Giant, Welcome Deck, Preconstructed, Draft Set, etc. — use Legacy:
            // permissive legality, 4-of limit, no color-identity constraint.
            _ => DeckFormat.Legacy
        };
    }

    /// <summary>
    /// Imports the given MTGJSON deck as a new deck. Returns the new deck id and counts.
    /// </summary>
    public async Task<MtgJsonDeckImportResult> ImportDeckAsync(MtgJsonDeck deck, IProgress<string>? progress = null, CancellationToken ct = default)
    {
        var result = new MtgJsonDeckImportResult();
        if (deck == null)
            return result;

        var format = MapMtgJsonDeckType(deck.Type);
        progress?.Report("Creating deck...");
        var deckId = await _deckService.CreateDeckAsync(deck.Name, format, deck.ReleaseDate ?? "");
        result.DeckId = deckId;
        if (deckId <= 0)
            return result;

        var allCards = new List<(string Section, MtgJsonDeckCard Card)>();
        foreach (var c in deck.MainBoard ?? [])
        {
            if (c != null)
                allCards.Add(("Main", c));
        }
        foreach (var c in deck.SideBoard ?? [])
        {
            if (c != null)
                allCards.Add(("Sideboard", c));
        }
        var commanderCards = deck.Commander;
        if (commanderCards == null || commanderCards.Count == 0)
            commanderCards = deck.DisplayCommander;

        foreach (var c in commanderCards ?? [])
        {
            if (c != null)
                allCards.Add(("Commander", c));
        }

        var uuids = allCards.Select(x => x.Card.Uuid).Where(u => !string.IsNullOrWhiteSpace(u)).Distinct().ToArray();
        progress?.Report($"Resolving {uuids.Length} cards...");
        var cardMap = await _cardRepo.GetCardsByUuiDsAsync(uuids);
        var missing = uuids.Where(u => !cardMap.ContainsKey(u)).ToList();
        result.MissingUuids = missing;

        // Fallback cache: resolve missing cards by name+set or ScryfallId (avoids repeated DB calls).
        var fallbackCache = new Dictionary<string, Card?>(StringComparer.OrdinalIgnoreCase);

        async Task<Card?> ResolveCardAsync(MtgJsonDeckCard mtgCard)
        {
            if (cardMap.TryGetValue(mtgCard.Uuid, out var byUuid))
                return byUuid;
            if (!string.IsNullOrWhiteSpace(mtgCard.Name) && !string.IsNullOrWhiteSpace(mtgCard.SetCode))
            {
                var key = "n:" + mtgCard.Name + "|s:" + mtgCard.SetCode;
                if (!fallbackCache.TryGetValue(key, out var byName))
                {
                    byName = await _cardRepo.GetCardByNameAndSetAsync(mtgCard.Name, mtgCard.SetCode);
                    fallbackCache[key] = byName;
                }
                if (byName != null)
                    return byName;
            }
            var scryfallId = mtgCard.Identifiers?.ScryfallId;
            if (!string.IsNullOrWhiteSpace(scryfallId))
            {
                var key = "sf:" + scryfallId;
                if (!fallbackCache.TryGetValue(key, out var byScryfall))
                {
                    byScryfall = await _cardRepo.GetCardByScryfallIdAsync(scryfallId!);
                    fallbackCache[key] = byScryfall;
                }
                if (byScryfall != null)
                    return byScryfall;
            }
            return null;
        }

        Card? firstCommanderCard = null;
        if ((commanderCards?.Count ?? 0) > 0)
            firstCommanderCard = await ResolveCardAsync(commanderCards[0]);
        if (firstCommanderCard != null && !string.IsNullOrEmpty(firstCommanderCard.Uuid))
        {
            progress?.Report("Setting commander...");
            await _deckService.SetCommanderAsync(deckId, firstCommanderCard.Uuid);
            result.CardsAdded += 1;
        }

        var allowMultipleCommanders = SupportsMultipleCommanders(firstCommanderCard);

        foreach (var (section, mtgCard) in allCards)
        {
            ct.ThrowIfCancellationRequested();
            var card = await ResolveCardAsync(mtgCard);
            if (card == null || string.IsNullOrEmpty(card.Uuid))
                continue;
            if (section == "Commander" && firstCommanderCard != null && card.Uuid == firstCommanderCard.Uuid)
                continue; // already added by SetCommanderAsync
            if (section == "Commander" && !allowMultipleCommanders)
                continue; // safety guard: non-partner commander decks should not import a second commander

            var quantity = mtgCard.Count < 1 ? 1 : mtgCard.Count;
            // skipLegalityCheck: MTGJSON deck files are authoritative — trust the source.
            var addResult = await _deckService.AddCardAsync(deckId, card.Uuid, quantity, section, skipLegalityCheck: true);
            if (!addResult.IsError)
                result.CardsAdded += quantity;
        }

        return result;
    }
}
