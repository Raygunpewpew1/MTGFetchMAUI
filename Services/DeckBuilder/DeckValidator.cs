using AetherVault.Core;
using AetherVault.Data;
using AetherVault.Models;

namespace AetherVault.Services.DeckBuilder;

public enum ValidationLevel
{
    Success,
    Warning,
    Error
}

public class ValidationResult
{
    public ValidationLevel Level { get; set; }
    public string Message { get; set; } = "";

    public bool IsSuccess => Level == ValidationLevel.Success;
    public bool IsError => Level == ValidationLevel.Error;
    public bool IsWarning => Level == ValidationLevel.Warning;

    public static ValidationResult Success() => new() { Level = ValidationLevel.Success };
    public static ValidationResult Error(string message) => new() { Level = ValidationLevel.Error, Message = message };
    public static ValidationResult Warning(string message) => new() { Level = ValidationLevel.Warning, Message = message };
}

public class DeckValidator
{
    private readonly ICardRepository _cardRepository;

    public DeckValidator(ICardRepository cardRepository)
    {
        _cardRepository = cardRepository;
    }

    public async Task<ValidationResult> ValidateCardAdditionAsync(DeckEntity deck, Card card, int quantityToAdd, List<DeckCardEntity> currentCards, bool skipLegalityCheck = false)
    {
        var format = EnumExtensions.ParseDeckFormat(deck.Format);

        // 1. Format Legality
        // Skipped during trusted imports (e.g. MTGJSON precon decks) where the source is authoritative.
        if (!skipLegalityCheck)
        {
            // Note: CardLegalities indexer uses the DeckFormat enum directly
            if (!card.Legalities.IsLegalInFormat(format))
            {
                // Allow restricted in Vintage
                if (format == DeckFormat.Vintage && card.Legalities[format] == LegalityStatus.Restricted)
                {
                    // Validate restricted (max 1)
                    int currentQty = GetTotalQuantity(card.UUID, currentCards);
                    if (currentQty + quantityToAdd > 1)
                    {
                        return ValidationResult.Error($"Card '{card.Name}' is Restricted in Vintage (Max 1).");
                    }
                }
                else
                {
                    return ValidationResult.Error($"Card '{card.Name}' is not legal in {format.ToDisplayName()}.");
                }
            }
        }

        // 2. Quantity Limits
        int existingQuantity = GetTotalQuantity(card.UUID, currentCards);
        int totalQuantity = existingQuantity + quantityToAdd;

        bool isBasicLand = card.IsBasicLand;
        bool isRelentless = card.Text.Contains("A deck can have any number of cards named", StringComparison.OrdinalIgnoreCase);

        int maxCopies = GetMaxCopies(format);

        if (!isBasicLand && !isRelentless && totalQuantity > maxCopies)
        {
            return ValidationResult.Error($"Cannot have more than {maxCopies} copies of '{card.Name}' in {format.ToDisplayName()}.");
        }

        // 3. Commander Color Identity
        if (IsCommanderFormat(format))
        {
            // If deck has a commander, check color identity
            if (!string.IsNullOrEmpty(deck.CommanderId))
            {
                // Use cached identity if available, otherwise fetch commander
                ColorIdentity commanderIdentity;
                if (!string.IsNullOrEmpty(deck.ColorIdentity))
                {
                    commanderIdentity = ColorIdentity.FromString(deck.ColorIdentity);
                }
                else
                {
                    var commander = await _cardRepository.GetCardDetailsAsync(deck.CommanderId);
                    if (commander == null) return ValidationResult.Warning("Commander not found, skipping color check.");
                    commanderIdentity = commander.GetColorIdentity();
                }

                var cardIdentity = card.GetColorIdentity();
                if (!commanderIdentity.Contains(cardIdentity))
                {
                    return ValidationResult.Error($"Card '{card.Name}' ({cardIdentity.AsString()}) is outside commander's color identity ({commanderIdentity.AsString()}).");
                }
            }
        }

        return ValidationResult.Success();
    }

    public async Task<ValidationResult> ValidateCommanderAsync(Card card, DeckFormat format)
    {
        if (!IsCommanderFormat(format))
        {
            return ValidationResult.Error("Commanders are only valid in Commander-like formats.");
        }

        // Must be Legendary Creature or Planeswalker with specific text
        bool isLegendaryCreature = card.IsCreature && card.IsLegendary;
        bool canBeCommander = card.Text.Contains("can be your commander", StringComparison.OrdinalIgnoreCase);

        if (!isLegendaryCreature && !canBeCommander)
        {
            // Brawl allows any Planeswalker?
            if ((format == DeckFormat.Brawl || format == DeckFormat.StandardBrawl) && card.IsPlaneswalker)
            {
                // Brawl commanders can be Planeswalkers
            }
            else
            {
                return ValidationResult.Error($"'{card.Name}' cannot be a commander (must be Legendary Creature).");
            }
        }

        // Check Ban list as Commander specifically? (Some cards are banned as commander only)
        // CardLegalities usually handles "Banned", but doesn't distinguish "Banned as Commander" vs "Banned in 99" easily unless we have that data.
        // Assuming Standard legality check covers it for now.

        return ValidationResult.Success();
    }

    private int GetTotalQuantity(string cardId, List<DeckCardEntity> cards)
    {
        return cards.Where(c => c.CardId == cardId).Sum(c => c.Quantity);
    }

    private int GetMaxCopies(DeckFormat format)
    {
        return format switch
        {
            DeckFormat.Commander
            or DeckFormat.Brawl
            or DeckFormat.Oathbreaker
            or DeckFormat.StandardBrawl
            or DeckFormat.PauperCommander
            or DeckFormat.Duel => 1,
            _ => 4
        };
    }

    private bool IsCommanderFormat(DeckFormat format)
    {
        return format is DeckFormat.Commander or DeckFormat.Brawl or DeckFormat.Oathbreaker or DeckFormat.StandardBrawl or DeckFormat.PauperCommander or DeckFormat.Duel;
    }

    /// <summary>
    /// Validates overall deck size (main / sideboard / commander slots) for the given format.
    /// Returns soft warnings only (no hard errors) so the UI can surface guidance without blocking edits.
    /// </summary>
    public ValidationResult ValidateDeckSize(DeckEntity deck, List<DeckCardEntity> currentCards)
    {
        var format = EnumExtensions.ParseDeckFormat(deck.Format);

        int mainCount = currentCards
            .Where(c => string.Equals(c.Section, "Main", StringComparison.OrdinalIgnoreCase))
            .Sum(c => c.Quantity);

        int sideboardCount = currentCards
            .Where(c => string.Equals(c.Section, "Sideboard", StringComparison.OrdinalIgnoreCase))
            .Sum(c => c.Quantity);

        int commanderCount = currentCards
            .Where(c => string.Equals(c.Section, "Commander", StringComparison.OrdinalIgnoreCase))
            .Sum(c => c.Quantity);

        var issues = new List<string>();
        string formatName = format.ToDisplayName();

        if (IsCommanderFormat(format))
        {
            // For Commander-like formats, total physical cards in main+commander should be close to 100.
            int total = mainCount + commanderCount;
            const int target = 100;

            if (total != target)
            {
                int diff = total - target;
                if (diff < 0)
                {
                    issues.Add($"{formatName} deck is {-diff} card(s) short of {target} (currently {total}/{target}).");
                }
                else
                {
                    issues.Add($"{formatName} deck has {diff} extra card(s) over {target} (currently {total}/{target}).");
                }
            }
        }
        else
        {
            // Non-commander formats: only enforce minimum main count and sideboard size.
            int minMain = format switch
            {
                DeckFormat.Standard or DeckFormat.Modern or DeckFormat.Pioneer or DeckFormat.Legacy or DeckFormat.Vintage
                or DeckFormat.Historic or DeckFormat.Timeless => 60,
                _ => 0
            };

            if (minMain > 0 && mainCount < minMain)
            {
                issues.Add($"{formatName} deck has only {mainCount} main-deck cards (needs at least {minMain}).");
            }

            // Standard 15-card sideboard rule applied to traditional 4-of formats.
            bool usesStandardSideboard =
                format is DeckFormat.Standard or DeckFormat.Modern or DeckFormat.Pioneer or DeckFormat.Legacy or DeckFormat.Vintage;

            if (usesStandardSideboard && sideboardCount > 15)
            {
                issues.Add($"Sideboard has {sideboardCount} cards (maximum is 15).");
            }
        }

        if (issues.Count == 0)
            return ValidationResult.Success();

        return ValidationResult.Warning(string.Join(" ", issues));
    }

    /// <summary>
    /// Validates that all cards currently in a commander-style deck obey the commander's color identity.
    /// Returns a soft warning listing offending cards; does not block changes.
    /// </summary>
    public async Task<ValidationResult> ValidateDeckColorIdentityAsync(DeckEntity deck, List<DeckCardEntity> currentCards)
    {
        var format = EnumExtensions.ParseDeckFormat(deck.Format);
        if (!IsCommanderFormat(format))
            return ValidationResult.Success();

        if (string.IsNullOrEmpty(deck.CommanderId))
            return ValidationResult.Success();

        ColorIdentity commanderIdentity;
        if (!string.IsNullOrEmpty(deck.ColorIdentity))
        {
            commanderIdentity = ColorIdentity.FromString(deck.ColorIdentity);
        }
        else
        {
            var commander = await _cardRepository.GetCardDetailsAsync(deck.CommanderId);
            if (commander == null)
                return ValidationResult.Warning("Commander not found, skipping color identity check.");
            commanderIdentity = commander.GetColorIdentity();
        }

        var offendingNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var entity in currentCards)
        {
            if (string.Equals(entity.Section, "Commander", StringComparison.OrdinalIgnoreCase))
                continue;

            if (entity.Quantity <= 0)
                continue;

            var card = await _cardRepository.GetCardDetailsAsync(entity.CardId);
            if (card == null)
                continue;

            var cardIdentity = card.GetColorIdentity();
            if (!commanderIdentity.Contains(cardIdentity))
            {
                offendingNames.Add(card.Name);
            }
        }

        if (offendingNames.Count == 0)
            return ValidationResult.Success();

        string commanderColors = commanderIdentity.AsString();
        string list = string.Join(", ", offendingNames.Take(5));
        if (offendingNames.Count > 5)
            list += ", ...";

        string msg = $"Deck has {offendingNames.Count} card(s) outside the commander's color identity ({commanderColors}): {list}.";
        return ValidationResult.Warning(msg);
    }
}
