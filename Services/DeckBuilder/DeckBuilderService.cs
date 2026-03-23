using AetherVault.Core;
using AetherVault.Data;
using AetherVault.Models;

namespace AetherVault.Services.DeckBuilder;

public class DeckBuilderService
{
    private readonly IDeckRepository _repository;
    private readonly DeckValidator _validator;
    private readonly ICardRepository _cardRepository;

    // Remember last successful deck/section selection to streamline future adds.
    private int? _lastDeckId;
    private string? _lastSection;

    public DeckBuilderService(IDeckRepository repository, DeckValidator validator, ICardRepository cardRepository)
    {
        _repository = repository;
        _validator = validator;
        _cardRepository = cardRepository;
    }

    public (int? deckId, string? section) GetLastSelection() => (_lastDeckId, _lastSection);

    public void SetLastSelection(int deckId, string section)
    {
        _lastDeckId = deckId;
        _lastSection = string.IsNullOrWhiteSpace(section) ? "Main" : section;
    }

    public async Task<int> CreateDeckAsync(string name, DeckFormat format, string description = "")
    {
        var deck = new DeckEntity
        {
            Name = name,
            Format = format.ToDbField(),
            Description = description,
            DateCreated = DateTime.Now,
            DateModified = DateTime.Now
        };
        return await _repository.CreateDeckAsync(deck);
    }

    public async Task<ValidationResult> AddCardAsync(int deckId, string cardUuid, int quantityToAdd, string section = "Main", bool skipLegalityCheck = false)
    {
        var deck = await _repository.GetDeckAsync(deckId);
        if (deck == null) return ValidationResult.Error("Deck not found.");

        var card = await _cardRepository.GetCardDetailsAsync(cardUuid);
        if (card == null) return ValidationResult.Error("Card not found.");

        var currentCards = await _repository.GetDeckCardsAsync(deckId);

        // Calculate new quantity
        var existingCard = currentCards.FirstOrDefault(c => c.CardId == cardUuid && c.Section == section);
        int currentQty = existingCard?.Quantity ?? 0;
        int newTotalQty = currentQty + quantityToAdd;

        // Validate (pass quantityToAdd to check against limits relative to current state)
        // DeckValidator logic: total = existing + toAdd. Correct.
        var result = await _validator.ValidateCardAdditionAsync(deck, card, quantityToAdd, currentCards, skipLegalityCheck);

        if (result.IsError) return result;

        // Perform Add/Update
        if (existingCard != null)
        {
            await _repository.UpdateCardQuantityAsync(deckId, cardUuid, section, newTotalQty);
        }
        else
        {
            await _repository.AddCardToDeckAsync(new DeckCardEntity
            {
                DeckId = deckId,
                CardId = cardUuid,
                Quantity = newTotalQty,
                Section = section,
                DateAdded = DateTime.Now
            });
        }

        // Update deck modified date
        deck.DateModified = DateTime.Now;
        await _repository.UpdateDeckAsync(deck);

        // Remember this as the last-used deck/section for future adds.
        SetLastSelection(deckId, section);

        return result;
    }

    public async Task<ValidationResult> SetCommanderAsync(int deckId, string cardUuid)
    {
        var deck = await _repository.GetDeckAsync(deckId);
        if (deck == null) return ValidationResult.Error("Deck not found.");

        var format = EnumExtensions.ParseDeckFormat(deck.Format);
        if (format != DeckFormat.Commander && format != DeckFormat.Brawl && format != DeckFormat.Oathbreaker && format != DeckFormat.StandardBrawl && format != DeckFormat.PauperCommander && format != DeckFormat.Duel)
        {
            return ValidationResult.Error("This format does not support commanders.");
        }

        var card = await _cardRepository.GetCardDetailsAsync(cardUuid);
        if (card == null) return ValidationResult.Error("Card not found.");

        var commanderValidation = await _validator.ValidateCommanderAsync(card, format);
        if (commanderValidation.IsError) return commanderValidation;

        // Remove old commander from "Commander" section if exists
        if (!string.IsNullOrEmpty(deck.CommanderId))
        {
            await _repository.RemoveCardFromDeckAsync(deckId, deck.CommanderId, "Commander");
        }

        // Update deck commander
        deck.CommanderId = cardUuid;
        deck.CommanderName = card.Name;
        deck.ColorIdentity = card.GetColorIdentity().AsString(); // "W,U,B" etc.
        deck.DateModified = DateTime.Now;

        // Also add commander to deck as a card in "Commander" section?
        // Usually yes, but some apps keep it separate.
        // Let's add it to "Commander" section for consistency in card counts if desired,
        // or just rely on CommanderId field.
        // Best practice: Add to deck list in "Commander" section so it's tracked as a card entity.

        await _repository.AddCardToDeckAsync(new DeckCardEntity
        {
            DeckId = deckId,
            CardId = cardUuid,
            Quantity = 1,
            Section = "Commander",
            DateAdded = DateTime.Now
        });

        await _repository.UpdateDeckAsync(deck);

        // Soft warning: after commander is set, check existing cards for color identity issues.
        var currentCards = await _repository.GetDeckCardsAsync(deckId);
        var colorCheck = await _validator.ValidateDeckColorIdentityAsync(deck, currentCards);
        if (colorCheck.IsError || colorCheck.IsWarning)
            return colorCheck;

        return commanderValidation;
    }

    public async Task RemoveCardAsync(int deckId, string cardUuid, string section)
    {
        await _repository.RemoveCardFromDeckAsync(deckId, cardUuid, section);
        var deck = await _repository.GetDeckAsync(deckId);
        if (deck != null)
        {
            deck.DateModified = DateTime.Now;
            await _repository.UpdateDeckAsync(deck);
        }
    }

    public async Task<ValidationResult> UpdateQuantityAsync(int deckId, string cardUuid, int newQuantity, string section)
    {
        if (newQuantity <= 0)
        {
            await RemoveCardAsync(deckId, cardUuid, section);
            return ValidationResult.Success();
        }

        var deck = await _repository.GetDeckAsync(deckId);
        if (deck == null) return ValidationResult.Error("Deck not found.");

        var card = await _cardRepository.GetCardDetailsAsync(cardUuid);
        if (card == null) return ValidationResult.Error("Card not found.");

        var currentCards = await _repository.GetDeckCardsAsync(deckId);
        var existing = currentCards.FirstOrDefault(c => c.CardId == cardUuid && c.Section == section);
        int oldQty = existing?.Quantity ?? 0;
        int diff = newQuantity - oldQty;

        if (diff > 0)
        {
            var result = await _validator.ValidateCardAdditionAsync(deck, card, diff, currentCards);
            if (result.IsError)
            {
                return result;
            }
        }

        await _repository.UpdateCardQuantityAsync(deckId, cardUuid, section, newQuantity);

        deck.DateModified = DateTime.Now;
        await _repository.UpdateDeckAsync(deck);

        return ValidationResult.Success();
    }

    public async Task<int> AutoSuggestLandsAsync(int deckId)
    {
        var deck = await _repository.GetDeckAsync(deckId);
        if (deck == null) return 0;

        var format = EnumExtensions.ParseDeckFormat(deck.Format);
        int targetLands = format is DeckFormat.Commander or DeckFormat.Brawl or DeckFormat.Oathbreaker or DeckFormat.StandardBrawl or DeckFormat.PauperCommander or DeckFormat.Duel
            ? 37
            : 24;

        var entities = await _repository.GetDeckCardsAsync(deckId);
        var uuids = entities.Select(e => e.CardId).Distinct().ToArray();
        Dictionary<string, Card> cardMap = uuids.Length > 0
            ? await _cardRepository.GetCardsByUuiDsAsync(uuids)
            : [];

        int currentLands = 0;
        foreach (var entity in entities)
        {
            if (cardMap.TryGetValue(entity.CardId, out var card) &&
                (card.CardType?.Contains("Land", StringComparison.OrdinalIgnoreCase) ?? false))
            {
                currentLands += entity.Quantity;
            }
        }

        int delta = targetLands - currentLands;
        if (delta <= 0) return 0;

        // Determine basic land names based on deck color identity.
        string identity = deck.ColorIdentity ?? "";
        if (string.IsNullOrWhiteSpace(identity) && !string.IsNullOrWhiteSpace(deck.CommanderId))
        {
            // Some older decks may have a commander but no cached ColorIdentity.
            // Derive it from commander and persist for future operations.
            var commander = await _cardRepository.GetCardDetailsAsync(deck.CommanderId);
            if (!string.IsNullOrEmpty(commander?.Uuid))
            {
                identity = commander.GetColorIdentity().AsString();
                deck.ColorIdentity = identity;
                deck.DateModified = DateTime.Now;
                await _repository.UpdateDeckAsync(deck);
            }
        }

        var landNames = new List<string>();
        if (identity.Contains('W')) landNames.Add("Plains");
        if (identity.Contains('U')) landNames.Add("Island");
        if (identity.Contains('B')) landNames.Add("Swamp");
        if (identity.Contains('R')) landNames.Add("Mountain");
        if (identity.Contains('G')) landNames.Add("Forest");

        if (landNames.Count == 0)
            landNames.Add("Wastes");

        var allocations = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in landNames)
            allocations[name] = 0;

        // Distribute suggested lands roughly evenly across colors.
        for (int i = 0; i < delta; i++)
        {
            string name = landNames[i % landNames.Count];
            allocations[name]++;
        }

        int added = 0;
        var failures = new List<string>();

        foreach (var kvp in allocations)
        {
            if (kvp.Value <= 0) continue;

            try
            {
                // Use a deterministic lookup so we don't accidentally pick e.g. "Island Fish Jasconius".
                // We want a real BASIC land printing so validation rules (copy limits, etc.) behave correctly.
                var helper = _cardRepository.CreateSearchHelper();
                helper.SearchCards()
                    .WhereNameEquals(kvp.Key)
                    .WhereType("Land")
                    .WhereSupertype("Basic")
                    .WherePrimarySideOnly()
                    .OrderBy("c.name")
                    .Limit(1);

                var exactBasics = await _cardRepository.SearchCardsAdvancedAsync(helper);
                var landCard = exactBasics.FirstOrDefault();

                if (landCard == null)
                {
                    failures.Add($"Could not find basic land '{kvp.Key}'.");
                    continue;
                }

                var result = await AddCardAsync(deckId, landCard.Uuid, kvp.Value, "Main");
                if (result.IsSuccess)
                {
                    added += kvp.Value;
                }
                else
                {
                    failures.Add(!string.IsNullOrWhiteSpace(result.Message)
                        ? $"Could not add {kvp.Value}× {kvp.Key}: {result.Message}"
                        : $"Could not add {kvp.Value}× {kvp.Key}.");
                }
            }
            catch (Exception ex)
            {
                failures.Add($"Failed while adding {kvp.Value}× {kvp.Key}: {ex.Message}");
            }
        }

        if (added == 0 && delta > 0 && failures.Count > 0)
        {
            // Surface *why* it didn't add anything; the UI will show this as an error status.
            throw new InvalidOperationException(string.Join(" ", failures.Take(3)));
        }

        return added;
    }

    public async Task<List<DeckEntity>> GetDecksAsync()
    {
        return await _repository.GetAllDecksAsync();
    }

    public async Task<DeckEntity?> GetDeckAsync(int id)
    {
        return await _repository.GetDeckAsync(id);
    }

    public Task DeleteDeckAsync(int id) => _repository.DeleteDeckAsync(id);

    public Task<List<DeckCardEntity>> GetDeckCardsAsync(int id) => _repository.GetDeckCardsAsync(id);

    public async Task UpdateDeckNameAsync(int id, string newName)
    {
        var deck = await _repository.GetDeckAsync(id);
        if (deck == null) return;
        deck.Name = newName;
        deck.DateModified = DateTime.Now;
        await _repository.UpdateDeckAsync(deck);
    }

    /// <summary>
    /// Runs non-blocking validation over the full deck and returns soft warnings (or success)
    /// that can be surfaced in the UI without preventing edits.
    /// </summary>
    public async Task<ValidationResult> ValidateDeckAsync(int deckId)
    {
        var deck = await _repository.GetDeckAsync(deckId);
        if (deck == null)
            return ValidationResult.Error("Deck not found.");

        var cards = await _repository.GetDeckCardsAsync(deckId);

        var sizeResult = _validator.ValidateDeckSize(deck, cards);
        var colorResult = await _validator.ValidateDeckColorIdentityAsync(deck, cards);

        // Combine messages, preferring warnings over silent success, and errors over warnings.
        var messages = new List<string>();
        var level = ValidationLevel.Success;

        void Apply(ValidationResult r)
        {
            if (r.Level == ValidationLevel.Success || string.IsNullOrWhiteSpace(r.Message))
                return;

            if (r.Level == ValidationLevel.Error)
            {
                level = ValidationLevel.Error;
                messages.Add(r.Message);
            }
            else if (r.Level == ValidationLevel.Warning && level != ValidationLevel.Error)
            {
                level = ValidationLevel.Warning;
                messages.Add(r.Message);
            }
        }

        Apply(sizeResult);
        Apply(colorResult);

        if (level == ValidationLevel.Success)
            return ValidationResult.Success();

        var message = string.Join(" ", messages);
        return level == ValidationLevel.Error
            ? ValidationResult.Error(message)
            : ValidationResult.Warning(message);
    }

    /// <summary>
    /// Validates and applies multiple deck card edits in one transaction.
    /// Mutations run in order; on first validation error nothing is persisted.
    /// </summary>
    public async Task<ValidationResult> ApplyEditorMutationsAsync(
        int deckId,
        IReadOnlyList<DeckEditorMutation> mutations,
        bool skipLegalityCheck = false)
    {
        if (mutations == null || mutations.Count == 0)
            return ValidationResult.Success();

        var deck = await _repository.GetDeckAsync(deckId);
        if (deck == null)
            return ValidationResult.Error("Deck not found.");

        var beforeSnapshot = CloneDeckCardList(await _repository.GetDeckCardsAsync(deckId));
        var working = CloneDeckCardList(beforeSnapshot);

        foreach (var m in mutations)
        {
            var step = await ApplyOneEditorMutationAsync(deckId, deck, working, m, skipLegalityCheck);
            if (step.IsError)
                return step;
        }

        var plan = BuildPersistencePlan(beforeSnapshot, working);
        if (plan.Count > 0)
            await _repository.ApplyDeckCardMutationsAsync(deckId, plan);

        deck.DateModified = DateTime.Now;
        await _repository.UpdateDeckAsync(deck);

        return ValidationResult.Success();
    }

    private static List<DeckCardEntity> CloneDeckCardList(IEnumerable<DeckCardEntity> src) =>
        src.Select(c => new DeckCardEntity
        {
            DeckId = c.DeckId,
            CardId = c.CardId,
            Quantity = c.Quantity,
            Section = c.Section,
            DateAdded = c.DateAdded
        }).ToList();

    private static DeckCardEntity? FindRow(List<DeckCardEntity> working, string cardId, string section) =>
        working.FirstOrDefault(c => c.CardId == cardId && c.Section == section);

    private async Task<ValidationResult> ApplyOneEditorMutationAsync(
        int deckId,
        DeckEntity deck,
        List<DeckCardEntity> working,
        DeckEditorMutation m,
        bool skipLegalityCheck)
    {
        switch (m.Kind)
        {
            case DeckEditorMutationKind.Add:
            {
                int delta = m.Quantity <= 0 ? 1 : m.Quantity;
                var card = await _cardRepository.GetCardDetailsAsync(m.CardId);
                if (card == null)
                    return ValidationResult.Error("Card not found.");

                var vr = await _validator.ValidateCardAdditionAsync(deck, card, delta, working, skipLegalityCheck);
                if (vr.IsError)
                    return vr;

                var ex = FindRow(working, m.CardId, m.Section);
                if (ex != null)
                    ex.Quantity += delta;
                else
                {
                    working.Add(new DeckCardEntity
                    {
                        DeckId = deckId,
                        CardId = m.CardId,
                        Section = m.Section,
                        Quantity = delta,
                        DateAdded = DateTime.Now
                    });
                }

                return ValidationResult.Success();
            }

            case DeckEditorMutationKind.SetQuantity:
            {
                var ex = FindRow(working, m.CardId, m.Section);
                int oldQ = ex?.Quantity ?? 0;
                int newQ = m.Quantity;
                if (newQ < 0)
                    newQ = 0;

                int diff = newQ - oldQ;
                if (diff > 0)
                {
                    var card = await _cardRepository.GetCardDetailsAsync(m.CardId);
                    if (card == null)
                        return ValidationResult.Error("Card not found.");

                    var vr = await _validator.ValidateCardAdditionAsync(deck, card, diff, working, skipLegalityCheck);
                    if (vr.IsError)
                        return vr;
                }

                if (newQ <= 0)
                {
                    if (ex != null)
                        working.Remove(ex);
                }
                else if (ex != null)
                {
                    ex.Quantity = newQ;
                }
                else
                {
                    working.Add(new DeckCardEntity
                    {
                        DeckId = deckId,
                        CardId = m.CardId,
                        Section = m.Section,
                        Quantity = newQ,
                        DateAdded = DateTime.Now
                    });
                }

                return ValidationResult.Success();
            }

            case DeckEditorMutationKind.Remove:
            {
                var ex = FindRow(working, m.CardId, m.Section);
                if (ex != null)
                    working.Remove(ex);
                return ValidationResult.Success();
            }

            case DeckEditorMutationKind.Move:
            {
                if (string.IsNullOrEmpty(m.TargetSection))
                    return ValidationResult.Error("Move requires a target section.");

                if (string.Equals(m.Section, m.TargetSection, StringComparison.OrdinalIgnoreCase))
                    return ValidationResult.Error("Source and target section are the same.");

                var from = FindRow(working, m.CardId, m.Section);
                if (from == null || from.Quantity <= 0)
                    return ValidationResult.Error("Nothing to move from source section.");

                int amt = m.Quantity <= 0 ? from.Quantity : Math.Min(m.Quantity, from.Quantity);
                if (amt <= 0)
                    return ValidationResult.Error("Nothing to move.");

                from.Quantity -= amt;
                if (from.Quantity <= 0)
                    working.Remove(from);

                var card = await _cardRepository.GetCardDetailsAsync(m.CardId);
                if (card == null)
                    return ValidationResult.Error("Card not found.");

                var addCheck = await _validator.ValidateCardAdditionAsync(deck, card, amt, working, skipLegalityCheck);
                if (addCheck.IsError)
                    return addCheck;

                var to = FindRow(working, m.CardId, m.TargetSection);
                if (to != null)
                    to.Quantity += amt;
                else
                {
                    working.Add(new DeckCardEntity
                    {
                        DeckId = deckId,
                        CardId = m.CardId,
                        Section = m.TargetSection,
                        Quantity = amt,
                        DateAdded = DateTime.Now
                    });
                }

                return ValidationResult.Success();
            }

            default:
                return ValidationResult.Error("Unknown mutation.");
        }
    }

    private static List<DeckCardPersistenceMutation> BuildPersistencePlan(
        List<DeckCardEntity> before,
        List<DeckCardEntity> after)
    {
        static Dictionary<(string CardId, string Section), int> ToMap(List<DeckCardEntity> list)
        {
            var d = new Dictionary<(string, string), int>();
            foreach (var c in list)
                d[(c.CardId, c.Section)] = c.Quantity;
            return d;
        }

        var bMap = ToMap(before);
        var aMap = ToMap(after);
        var keys = bMap.Keys.Union(aMap.Keys).ToList();
        var plan = new List<DeckCardPersistenceMutation>();

        foreach (var key in keys)
        {
            int bq = bMap.GetValueOrDefault(key);
            int aq = aMap.GetValueOrDefault(key);
            if (bq == aq)
                continue;

            if (aq <= 0)
            {
                plan.Add(new DeckCardPersistenceMutation(DeckCardPersistenceKind.Remove, key.CardId, key.Section));
            }
            else if (bq <= 0)
            {
                plan.Add(new DeckCardPersistenceMutation(
                    DeckCardPersistenceKind.InsertOrReplace,
                    key.CardId,
                    key.Section,
                    aq,
                    DateTime.Now));
            }
            else
            {
                plan.Add(new DeckCardPersistenceMutation(
                    DeckCardPersistenceKind.UpdateQuantity,
                    key.CardId,
                    key.Section,
                    aq));
            }
        }

        return plan;
    }
}
