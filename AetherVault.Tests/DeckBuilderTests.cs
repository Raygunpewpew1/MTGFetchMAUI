using AetherVault.Core;
using AetherVault.Data;
using AetherVault.Models;
using AetherVault.Services.DeckBuilder;

namespace AetherVault.Tests;

public class DeckBuilderTests
{
    private readonly MockCardRepository _cardRepo;
    private readonly MockDeckRepository _deckRepo;
    private readonly DeckValidator _validator;
    private readonly DeckBuilderService _service;

    public DeckBuilderTests()
    {
        _cardRepo = new MockCardRepository();
        _deckRepo = new MockDeckRepository();
        _validator = new DeckValidator(_cardRepo);
        _service = new DeckBuilderService(_deckRepo, _validator, _cardRepo);
    }

    [Fact]
    public async Task AddCard_ValidStandard_Success()
    {
        // Arrange
        var deckId = await _service.CreateDeckAsync("Standard Deck", DeckFormat.Standard);
        var card = CreateCard("uuid-1", "Valid Card", DeckFormat.Standard, LegalityStatus.Legal);
        _cardRepo.AddCard(card);

        // Act
        var result = await _service.AddCardAsync(deckId, card.UUID, 4);

        // Assert
        Assert.True(result.IsSuccess);
        var deckCards = await _deckRepo.GetDeckCardsAsync(deckId);
        Assert.Single(deckCards);
        Assert.Equal(4, deckCards[0].Quantity);
    }

    [Fact]
    public async Task AddCard_BannedStandard_Fails()
    {
        // Arrange
        var deckId = await _service.CreateDeckAsync("Standard Deck", DeckFormat.Standard);
        var card = CreateCard("uuid-banned", "Banned Card", DeckFormat.Standard, LegalityStatus.Banned);
        _cardRepo.AddCard(card);

        // Act
        var result = await _service.AddCardAsync(deckId, card.UUID, 1);

        // Assert
        Assert.True(result.IsError);
        Assert.Contains("not legal", result.Message);
        var deckCards = await _deckRepo.GetDeckCardsAsync(deckId);
        Assert.Empty(deckCards);
    }

    [Fact]
    public async Task AddCard_MoreThan4_Standard_Fails()
    {
        // Arrange
        var deckId = await _service.CreateDeckAsync("Standard Deck", DeckFormat.Standard);
        var card = CreateCard("uuid-1", "Valid Card", DeckFormat.Standard, LegalityStatus.Legal);
        _cardRepo.AddCard(card);

        // Act
        await _service.AddCardAsync(deckId, card.UUID, 4);
        var result = await _service.AddCardAsync(deckId, card.UUID, 1);

        // Assert
        Assert.True(result.IsError);
        Assert.Contains("more than 4 copies", result.Message);
        var deckCards = await _deckRepo.GetDeckCardsAsync(deckId);
        Assert.Equal(4, deckCards[0].Quantity); // Should remain 4
    }

    [Fact]
    public async Task SetCommander_ValidLegendary_Success()
    {
        // Arrange
        var deckId = await _service.CreateDeckAsync("Commander Deck", DeckFormat.Commander);
        var commander = CreateCard("cmdr-1", "My Commander", DeckFormat.Commander, LegalityStatus.Legal);
        commander.CardType = "Legendary Creature";
        commander.Colors = "W,U";
        _cardRepo.AddCard(commander);

        // Act
        var result = await _service.SetCommanderAsync(deckId, commander.UUID);

        // Assert
        Assert.True(result.IsSuccess);
        var deck = await _deckRepo.GetDeckAsync(deckId);
        Assert.NotNull(deck);
        Assert.Equal(commander.UUID, deck!.CommanderId);
        Assert.Equal("WU", deck.ColorIdentity);
    }

    [Fact]
    public async Task AddCard_WrongColorIdentity_Commander_Fails()
    {
        // Arrange
        var deckId = await _service.CreateDeckAsync("Commander Deck", DeckFormat.Commander);

        var commander = CreateCard("cmdr-1", "My Commander", DeckFormat.Commander, LegalityStatus.Legal);
        commander.CardType = "Legendary Creature";
        commander.Colors = "W"; // White only
        _cardRepo.AddCard(commander);

        await _service.SetCommanderAsync(deckId, commander.UUID);

        var redCard = CreateCard("red-1", "Red Card", DeckFormat.Commander, LegalityStatus.Legal);
        redCard.Colors = "R";
        _cardRepo.AddCard(redCard);

        // Act
        var result = await _service.AddCardAsync(deckId, redCard.UUID, 1);

        // Assert
        Assert.True(result.IsError);
        Assert.Contains("outside commander's color identity", result.Message);
    }

    // ── Not-found guard tests ─────────────────────────────────────────

    [Fact]
    public async Task AddCard_CardNotFound_ReturnsError()
    {
        var deckId = await _service.CreateDeckAsync("Test Deck", DeckFormat.Standard);
        // "missing-uuid" was never added to _cardRepo
        var result = await _service.AddCardAsync(deckId, "missing-uuid", 1);
        Assert.True(result.IsError);
        Assert.Contains("Card not found", result.Message);
    }

    [Fact]
    public async Task AddCard_DeckNotFound_ReturnsError()
    {
        var card = CreateCard("uuid-x", "Some Card", DeckFormat.Standard, LegalityStatus.Legal);
        _cardRepo.AddCard(card);
        var result = await _service.AddCardAsync(9999, card.UUID, 1);
        Assert.True(result.IsError);
        Assert.Contains("Deck not found", result.Message);
    }

    [Fact]
    public async Task SetCommander_DeckNotFound_ReturnsError()
    {
        var result = await _service.SetCommanderAsync(9999, "any-uuid");
        Assert.True(result.IsError);
        Assert.Contains("Deck not found", result.Message);
    }

    [Fact]
    public async Task SetCommander_CardNotFound_ReturnsError()
    {
        var deckId = await _service.CreateDeckAsync("Commander Deck", DeckFormat.Commander);
        var result = await _service.SetCommanderAsync(deckId, "missing-uuid");
        Assert.True(result.IsError);
        Assert.Contains("Card not found", result.Message);
    }

    // ── Copy limit and exception tests ────────────────────────────────

    [Fact]
    public async Task AddCard_BasicLand_AllowsMoreThan4Copies()
    {
        var deckId = await _service.CreateDeckAsync("Standard Deck", DeckFormat.Standard);
        var land = CreateCard("land-1", "Forest", DeckFormat.Standard, LegalityStatus.Legal);
        land.CardType = "Basic Land — Forest"; // triggers IsBasicLand
        _cardRepo.AddCard(land);

        await _service.AddCardAsync(deckId, land.UUID, 4);
        var result = await _service.AddCardAsync(deckId, land.UUID, 20); // total 24

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task AutoSuggestLands_NoColorIdentity_AddsWastesToTarget()
    {
        var deckId = await _service.CreateDeckAsync("Standard Deck", DeckFormat.Standard);

        var wastes = CreateCard("wastes-1", "Wastes", DeckFormat.Standard, LegalityStatus.Legal);
        wastes.CardType = "Basic Land — Wastes";
        _cardRepo.AddCard(wastes);

        var added = await _service.AutoSuggestLandsAsync(deckId);

        Assert.Equal(24, added);
        var deckCards = await _deckRepo.GetDeckCardsAsync(deckId);
        Assert.Single(deckCards);
        Assert.Equal(24, deckCards[0].Quantity);
        Assert.Equal("Main", deckCards[0].Section);
    }

    [Fact]
    public async Task AutoSuggestLands_WithColorIdentity_SplitsEvenly()
    {
        var deckId = await _service.CreateDeckAsync("Commander Deck", DeckFormat.Commander);

        // Manually set deck identity for the test.
        var deck = await _deckRepo.GetDeckAsync(deckId);
        deck!.ColorIdentity = "WU";
        await _deckRepo.UpdateDeckAsync(deck);

        var plains = CreateCard("plains-1", "Plains", DeckFormat.Commander, LegalityStatus.Legal);
        plains.CardType = "Basic Land — Plains";
        _cardRepo.AddCard(plains);

        var island = CreateCard("island-1", "Island", DeckFormat.Commander, LegalityStatus.Legal);
        island.CardType = "Basic Land — Island";
        _cardRepo.AddCard(island);

        var added = await _service.AutoSuggestLandsAsync(deckId);

        Assert.Equal(37, added);
        var deckCards = await _deckRepo.GetDeckCardsAsync(deckId);
        Assert.Equal(2, deckCards.Count);

        var plainsQty = deckCards.First(c => c.CardId == plains.UUID).Quantity;
        var islandQty = deckCards.First(c => c.CardId == island.UUID).Quantity;

        Assert.Equal(19, plainsQty);
        Assert.Equal(18, islandQty);
    }

    [Fact]
    public async Task AddCard_RelentlessCard_AllowsMoreThan4Copies()
    {
        var deckId = await _service.CreateDeckAsync("Modern Deck", DeckFormat.Modern);
        var relentless = CreateCard("rats-1", "Relentless Rats", DeckFormat.Modern, LegalityStatus.Legal);
        relentless.Text = "A deck can have any number of cards named Relentless Rats.";
        _cardRepo.AddCard(relentless);

        await _service.AddCardAsync(deckId, relentless.UUID, 4);
        var result = await _service.AddCardAsync(deckId, relentless.UUID, 20);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task AddCard_VintageRestricted_AllowsOneCopy()
    {
        var deckId = await _service.CreateDeckAsync("Vintage Deck", DeckFormat.Vintage);
        var restricted = new Card
        {
            UUID = "restricted-1",
            Name = "Black Lotus",
            CardType = "Artifact",
            Legalities = new CardLegalities(),
            Colors = "",
            Text = ""
        };
        restricted.Legalities[DeckFormat.Vintage] = LegalityStatus.Restricted;
        _cardRepo.AddCard(restricted);

        var result = await _service.AddCardAsync(deckId, restricted.UUID, 1);
        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task AddCard_VintageRestricted_RejectsTwoCopies()
    {
        var deckId = await _service.CreateDeckAsync("Vintage Deck", DeckFormat.Vintage);
        var restricted = new Card
        {
            UUID = "restricted-2",
            Name = "Ancestral Recall",
            CardType = "Instant",
            Legalities = new CardLegalities(),
            Colors = "U",
            Text = ""
        };
        restricted.Legalities[DeckFormat.Vintage] = LegalityStatus.Restricted;
        _cardRepo.AddCard(restricted);

        await _service.AddCardAsync(deckId, restricted.UUID, 1);
        var result = await _service.AddCardAsync(deckId, restricted.UUID, 1); // total 2

        Assert.True(result.IsError);
        Assert.Contains("Restricted", result.Message);
    }

    // ── Commander validation tests ─────────────────────────────────────

    [Fact]
    public async Task SetCommander_NonCommanderFormat_ReturnsError()
    {
        var deckId = await _service.CreateDeckAsync("Standard Deck", DeckFormat.Standard);
        var card = CreateCard("cmdr-std", "Legendary Creature", DeckFormat.Standard, LegalityStatus.Legal);
        card.CardType = "Legendary Creature";
        _cardRepo.AddCard(card);

        var result = await _service.SetCommanderAsync(deckId, card.UUID);
        Assert.True(result.IsError);
        Assert.Contains("format does not support commanders", result.Message);
    }

    [Fact]
    public async Task SetCommander_NonLegendaryCreature_ReturnsError()
    {
        var deckId = await _service.CreateDeckAsync("Commander Deck", DeckFormat.Commander);
        var card = CreateCard("plain-creature", "Grizzly Bears", DeckFormat.Commander, LegalityStatus.Legal);
        card.CardType = "Creature — Bear"; // NOT Legendary
        _cardRepo.AddCard(card);

        var result = await _service.SetCommanderAsync(deckId, card.UUID);
        Assert.True(result.IsError);
        Assert.Contains("cannot be a commander", result.Message);
    }

    [Fact]
    public async Task SetCommander_Planeswalker_BrawlFormat_Succeeds()
    {
        var deckId = await _service.CreateDeckAsync("Brawl Deck", DeckFormat.Brawl);
        var pw = new Card
        {
            UUID = "pw-1",
            Name = "Jace, the Mind Sculptor",
            CardType = "Legendary Planeswalker — Jace",
            Legalities = new CardLegalities(),
            Colors = "U",
            Text = ""
        };
        pw.Legalities[DeckFormat.Brawl] = LegalityStatus.Legal;
        _cardRepo.AddCard(pw);

        var result = await _service.SetCommanderAsync(deckId, pw.UUID);
        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task AddCard_CommanderFormat_NoCommanderSet_AllowsCard()
    {
        // Without a commander, there is no color identity restriction
        var deckId = await _service.CreateDeckAsync("Commander Deck", DeckFormat.Commander);
        var card = CreateCard("red-2", "Red Card", DeckFormat.Commander, LegalityStatus.Legal);
        card.Colors = "R";
        _cardRepo.AddCard(card);

        var result = await _service.AddCardAsync(deckId, card.UUID, 1);
        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task AddCard_CommanderFormat_MaxOneCopyPerCard()
    {
        var deckId = await _service.CreateDeckAsync("Commander Deck", DeckFormat.Commander);
        var card = CreateCard("sol-ring", "Sol Ring", DeckFormat.Commander, LegalityStatus.Legal);
        _cardRepo.AddCard(card);

        await _service.AddCardAsync(deckId, card.UUID, 1);
        var result = await _service.AddCardAsync(deckId, card.UUID, 1); // total 2

        Assert.True(result.IsError);
        Assert.Contains("more than 1 copies", result.Message);
    }

    // ── UpdateQuantity / Remove edge case tests ───────────────────────

    [Fact]
    public async Task UpdateQuantity_ZeroQuantity_RemovesCard()
    {
        var deckId = await _service.CreateDeckAsync("Standard Deck", DeckFormat.Standard);
        var card = CreateCard("uuid-rm1", "Card To Remove", DeckFormat.Standard, LegalityStatus.Legal);
        _cardRepo.AddCard(card);
        await _service.AddCardAsync(deckId, card.UUID, 2);

        var result = await _service.UpdateQuantityAsync(deckId, card.UUID, 0, "Main");

        Assert.True(result.IsSuccess);
        var deckCards = await _deckRepo.GetDeckCardsAsync(deckId);
        Assert.Empty(deckCards);
    }

    [Fact]
    public async Task UpdateQuantity_NegativeQuantity_RemovesCard()
    {
        var deckId = await _service.CreateDeckAsync("Standard Deck", DeckFormat.Standard);
        var card = CreateCard("uuid-rm2", "Card To Remove", DeckFormat.Standard, LegalityStatus.Legal);
        _cardRepo.AddCard(card);
        await _service.AddCardAsync(deckId, card.UUID, 2);

        var result = await _service.UpdateQuantityAsync(deckId, card.UUID, -1, "Main");

        Assert.True(result.IsSuccess);
        var deckCards = await _deckRepo.GetDeckCardsAsync(deckId);
        Assert.Empty(deckCards);
    }

    [Fact]
    public async Task RemoveCard_UpdatesDeck()
    {
        var deckId = await _service.CreateDeckAsync("Standard Deck", DeckFormat.Standard);
        var card = CreateCard("uuid-del1", "Removable Card", DeckFormat.Standard, LegalityStatus.Legal);
        _cardRepo.AddCard(card);
        await _service.AddCardAsync(deckId, card.UUID, 2);

        await _service.RemoveCardAsync(deckId, card.UUID, "Main");

        var deckCards = await _deckRepo.GetDeckCardsAsync(deckId);
        Assert.Empty(deckCards);
    }

    // ── Section tracking test ─────────────────────────────────────────

    [Fact]
    public async Task AddCard_SameCard_DifferentSections_BothTracked()
    {
        var deckId = await _service.CreateDeckAsync("Standard Deck", DeckFormat.Standard);
        var card = CreateCard("uuid-multi", "Versatile Card", DeckFormat.Standard, LegalityStatus.Legal);
        _cardRepo.AddCard(card);

        await _service.AddCardAsync(deckId, card.UUID, 4, "Main");
        // Note: adding to sideboard after 4 in main will fail total-quantity check
        // because GetTotalQuantity sums across all sections.
        // This test documents the current behavior.
        var sideResult = await _service.AddCardAsync(deckId, card.UUID, 1, "Sideboard");

        // Expect error: total across all sections (4+1=5) exceeds max 4
        Assert.True(sideResult.IsError);
        Assert.Contains("more than 4 copies", sideResult.Message);
    }

    private Card CreateCard(string uuid, string name, DeckFormat format, LegalityStatus legality)
    {
        var card = new Card
        {
            UUID = uuid,
            Name = name,
            CardType = "Creature",
            Legalities = new CardLegalities(),
            Colors = "",
            Text = ""
        };
        card.Legalities[format] = legality;
        return card;
    }
}

// ── Mocks ─────────────────────────────────────────────────────────────

public class MockDeckRepository : IDeckRepository
{
    private readonly List<DeckEntity> _decks = new();
    private readonly List<DeckCardEntity> _deckCards = new();
    private int _nextId = 1;

    public Task<int> CreateDeckAsync(DeckEntity deck)
    {
        deck.Id = _nextId++;
        _decks.Add(deck);
        return Task.FromResult(deck.Id);
    }

    public Task UpdateDeckAsync(DeckEntity deck)
    {
        var existing = _decks.FirstOrDefault(d => d.Id == deck.Id);
        if (existing != null)
        {
            _decks.Remove(existing);
            _decks.Add(deck);
        }
        return Task.CompletedTask;
    }

    public Task DeleteDeckAsync(int deckId)
    {
        _decks.RemoveAll(d => d.Id == deckId);
        _deckCards.RemoveAll(c => c.DeckId == deckId);
        return Task.CompletedTask;
    }

    public Task<DeckEntity?> GetDeckAsync(int deckId)
    {
        return Task.FromResult(_decks.FirstOrDefault(d => d.Id == deckId));
    }

    public Task<List<DeckEntity>> GetAllDecksAsync()
    {
        return Task.FromResult(_decks.ToList());
    }

    public Task AddCardToDeckAsync(DeckCardEntity card)
    {
        var existing = _deckCards.FirstOrDefault(c => c.DeckId == card.DeckId && c.CardId == card.CardId && c.Section == card.Section);
        if (existing != null)
        {
            _deckCards.Remove(existing);
        }
        _deckCards.Add(card);
        return Task.CompletedTask;
    }

    public Task RemoveCardFromDeckAsync(int deckId, string cardId, string section)
    {
        _deckCards.RemoveAll(c => c.DeckId == deckId && c.CardId == cardId && c.Section == section);
        return Task.CompletedTask;
    }

    public Task UpdateCardQuantityAsync(int deckId, string cardId, string section, int quantity)
    {
        var existing = _deckCards.FirstOrDefault(c => c.DeckId == deckId && c.CardId == cardId && c.Section == section);
        if (existing != null)
        {
            existing.Quantity = quantity;
        }
        else
        {
            // Usually Update implies exist, but here we might just add if implementing upsert logic
            _deckCards.Add(new DeckCardEntity { DeckId = deckId, CardId = cardId, Section = section, Quantity = quantity });
        }
        return Task.CompletedTask;
    }

    public Task<List<DeckCardEntity>> GetDeckCardsAsync(int deckId)
    {
        return Task.FromResult(_deckCards.Where(c => c.DeckId == deckId).ToList());
    }

    public Task<int> GetDeckCardCountAsync(int deckId)
    {
        return Task.FromResult(_deckCards.Where(c => c.DeckId == deckId).Sum(c => c.Quantity));
    }
}

public class MockCardRepository : ICardRepository
{
    private readonly Dictionary<string, Card> _cards = new();

    public void AddCard(Card card)
    {
        _cards[card.UUID] = card;
    }

    public Task<Card> GetCardDetailsAsync(string uuid)
    {
        return Task.FromResult(_cards.ContainsKey(uuid) ? _cards[uuid] : null!);
    }

    // Stub other methods
    public Task<Card> GetCardByUUIDAsync(string uuid) => GetCardDetailsAsync(uuid);
    public Task<Card> GetCardWithLegalitiesAsync(string uuid) => GetCardDetailsAsync(uuid);
    public Task<Card> GetCardWithRulingsAsync(string uuid) => GetCardDetailsAsync(uuid);
    public Task<Card> GetCardByFaceNameAndSetAsync(string faceName, string setCode) => throw new NotImplementedException();
    public Task<string> GetScryfallIdAsync(string cardUUID) => throw new NotImplementedException();
    public Task<CardRuling[]> GetCardRulingsAsync(string uuid) => throw new NotImplementedException();
    public Task<string[]> GetOtherFaceIdsAsync(string uuid) => throw new NotImplementedException();
    public Task<Card[]> GetCardWithOtherFacesAsync(string uuid) => throw new NotImplementedException();
    public Task<Card[]> GetFullCardPackageAsync(string uuid) => throw new NotImplementedException();
    public Task<Dictionary<string, Card>> GetCardsByUUIDsAsync(string[] uuids) => throw new NotImplementedException();
    public Task<Card[]> SearchCardsAsync(string searchText, int limit = 100)
    {
        // Minimal behavior for tests.
        var result = _cards.Values
            .Where(c => c.Name.Contains(searchText, StringComparison.OrdinalIgnoreCase))
            .OrderBy(c => c.Name)
            .Take(limit)
            .ToArray();
        return Task.FromResult(result);
    }

    public Task<Card[]> SearchCardsAdvancedAsync(MTGSearchHelper searchHelper)
    {
        // Minimal interpretation of the helper's parameters for unit tests.
        // This intentionally does NOT try to execute SQL; it just honors the key filters used by DeckBuilderService.
        var (_, parameters) = searchHelper.Build();

        string? nameEq = parameters
            .Select(p => p.value)
            .OfType<string>()
            .FirstOrDefault(v => !v.Contains('%'));

        bool requireLand = parameters.Select(p => p.value).OfType<string>().Any(v => v.Contains("Land", StringComparison.OrdinalIgnoreCase));
        bool requireBasic = parameters.Select(p => p.value).OfType<string>().Any(v => v.Contains("Basic", StringComparison.OrdinalIgnoreCase));

        IEnumerable<Card> query = _cards.Values;

        if (!string.IsNullOrEmpty(nameEq))
        {
            query = query.Where(c => string.Equals(c.Name, nameEq, StringComparison.OrdinalIgnoreCase));
        }

        if (requireLand)
            query = query.Where(c => c.CardType.Contains("Land", StringComparison.OrdinalIgnoreCase));
        if (requireBasic)
            query = query.Where(c => c.CardType.Contains("Basic", StringComparison.OrdinalIgnoreCase));

        // Respect the DeckBuilderService expectation (LIMIT 1).
        return Task.FromResult(query.Take(1).ToArray());
    }

    public Task<int> GetCountAdvancedAsync(MTGSearchHelper searchHelper) => throw new NotImplementedException();
    public MTGSearchHelper CreateSearchHelper() => new();
    public Task<IReadOnlyList<ImportLookupRow>> GetImportLookupRowsAsync() => Task.FromResult<IReadOnlyList<ImportLookupRow>>([]);
    public Task<IReadOnlyList<SetInfo>> GetAllSetsAsync() => Task.FromResult<IReadOnlyList<SetInfo>>([]);
}
