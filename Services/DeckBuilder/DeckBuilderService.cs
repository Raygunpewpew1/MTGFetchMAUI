using MTGFetchMAUI.Core;
using MTGFetchMAUI.Data;
using MTGFetchMAUI.Models;

namespace MTGFetchMAUI.Services.DeckBuilder;

public class DeckBuilderService
{
    private readonly IDeckRepository _repository;
    private readonly DeckValidator _validator;
    private readonly ICardRepository _cardRepository;

    public DeckBuilderService(IDeckRepository repository, DeckValidator validator, ICardRepository cardRepository)
    {
        _repository = repository;
        _validator = validator;
        _cardRepository = cardRepository;
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

    public async Task<ValidationResult> AddCardAsync(int deckId, string cardUuid, int quantityToAdd, string section = "Main")
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
        var result = await _validator.ValidateCardAdditionAsync(deck, card, quantityToAdd, currentCards);

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

        var result = await _validator.ValidateCommanderAsync(card, format);
        if (result.IsError) return result;

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

        return result;
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
}
