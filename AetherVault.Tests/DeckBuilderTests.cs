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
        var result = await _service.AddCardAsync(deckId, card.Uuid, 4);

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
        var result = await _service.AddCardAsync(deckId, card.Uuid, 1);

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
        await _service.AddCardAsync(deckId, card.Uuid, 4);
        var result = await _service.AddCardAsync(deckId, card.Uuid, 1);

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
        var result = await _service.SetCommanderAsync(deckId, commander.Uuid);

        // Assert
        Assert.True(result.IsSuccess);
        var deck = await _deckRepo.GetDeckAsync(deckId);
        Assert.NotNull(deck);
        Assert.Equal(commander.Uuid, deck!.CommanderId);
        Assert.Equal("WU", deck.ColorIdentity);
    }

    [Fact]
    public async Task AddCard_CommanderSectionPartner_ExpandsColorIdentityForValidation()
    {
        var deckId = await _service.CreateDeckAsync("Partner Deck", DeckFormat.Commander);

        var commander = CreateCard("cmdr-w", "Mono W", DeckFormat.Commander, LegalityStatus.Legal);
        commander.CardType = "Legendary Creature";
        commander.Colors = "W";
        _cardRepo.AddCard(commander);

        var partner = CreateCard("partner-u", "Partner U", DeckFormat.Commander, LegalityStatus.Legal);
        partner.CardType = "Legendary Creature";
        partner.Colors = "U";
        _cardRepo.AddCard(partner);

        await _service.SetCommanderAsync(deckId, commander.Uuid);

        await _service.AddCardAsync(deckId, partner.Uuid, 1, DeckCsvV1.Sections.Commander, skipLegalityCheck: true);

        var blueCard = CreateCard("island-golem", "Thing", DeckFormat.Commander, LegalityStatus.Legal);
        blueCard.Colors = "U";
        _cardRepo.AddCard(blueCard);

        var result = await _service.AddCardAsync(deckId, blueCard.Uuid, 1);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task ApplyEditorMutations_CommanderDeck_CachesCommanderIdentityForBatch()
    {
        var deckId = await _service.CreateDeckAsync("Batch Cmd", DeckFormat.Commander);
        var commander = CreateCard("cmdr-batch", "Cmdr", DeckFormat.Commander, LegalityStatus.Legal);
        commander.CardType = "Legendary Creature";
        commander.Colors = "W";
        _cardRepo.AddCard(commander);
        var w1 = CreateCard("w1", "White One", DeckFormat.Commander, LegalityStatus.Legal);
        w1.Colors = "W";
        var w2 = CreateCard("w2", "White Two", DeckFormat.Commander, LegalityStatus.Legal);
        w2.Colors = "W";
        _cardRepo.AddCard(w1);
        _cardRepo.AddCard(w2);

        await _service.SetCommanderAsync(deckId, commander.Uuid);
        var deck = await _deckRepo.GetDeckAsync(deckId);
        Assert.NotNull(deck);
        deck!.ColorIdentity = "";
        await _deckRepo.UpdateDeckAsync(deck);

        _cardRepo.ResetGetCardDetailsCallCount();

        var mutations = new DeckEditorMutation[]
        {
            new(DeckEditorMutationKind.Add, w1.Uuid, "Main", null, 1),
            new(DeckEditorMutationKind.Add, w2.Uuid, "Main", null, 1),
        };

        var result = await _service.ApplyEditorMutationsAsync(deckId, mutations);
        Assert.True(result.IsSuccess);

        // One commander lookup for batch color resolution + one per distinct added card (no per-add commander refetch).
        Assert.Equal(3, _cardRepo.GetCardDetailsAsyncCallCount);
    }

    [Fact]
    public void DeckFormatRules_MaxNonBasicCopies_MatchesCommanderLikeRules()
    {
        Assert.Equal(1, DeckFormatRules.MaxNonBasicCopies(DeckFormat.Commander));
        Assert.Equal(1, DeckFormatRules.MaxNonBasicCopies(DeckFormat.Duel));
        Assert.Equal(4, DeckFormatRules.MaxNonBasicCopies(DeckFormat.Standard));
    }

    [Fact]
    public void ValidationResult_Combined_PreservesDetailLines()
    {
        var a = ValidationResult.Warning("Size issue");
        var b = ValidationResult.Warning("Color issue");
        var c = ValidationResult.Combined(a, b);
        Assert.True(c.IsWarning);
        Assert.Equal(2, c.DetailLines.Count);
        Assert.Contains("Size issue", c.Message);
        Assert.Contains("Color issue", c.Message);
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

        await _service.SetCommanderAsync(deckId, commander.Uuid);

        var redCard = CreateCard("red-1", "Red Card", DeckFormat.Commander, LegalityStatus.Legal);
        redCard.Colors = "R";
        _cardRepo.AddCard(redCard);

        // Act
        var result = await _service.AddCardAsync(deckId, redCard.Uuid, 1);

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
        var result = await _service.AddCardAsync(9999, card.Uuid, 1);
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

        await _service.AddCardAsync(deckId, land.Uuid, 4);
        var result = await _service.AddCardAsync(deckId, land.Uuid, 20); // total 24

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

        var plainsQty = deckCards.First(c => c.CardId == plains.Uuid).Quantity;
        var islandQty = deckCards.First(c => c.CardId == island.Uuid).Quantity;

        Assert.Equal(19, plainsQty);
        Assert.Equal(18, islandQty);
    }

    [Fact]
    public async Task AutoSuggestLands_CommanderIdentityFallback_UsesCommanderColors()
    {
        var deckId = await _service.CreateDeckAsync("Commander Deck", DeckFormat.Commander);

        // Set commander, but clear cached ColorIdentity to simulate older/broken decks.
        var commander = CreateCard("cmdr-wub", "Three Color Commander", DeckFormat.Commander, LegalityStatus.Legal);
        commander.CardType = "Legendary Creature";
        commander.Colors = "W,U,B";
        _cardRepo.AddCard(commander);

        var setResult = await _service.SetCommanderAsync(deckId, commander.Uuid);
        Assert.True(setResult.IsSuccess);

        var deck = await _deckRepo.GetDeckAsync(deckId);
        Assert.NotNull(deck);
        deck!.ColorIdentity = ""; // force fallback
        await _deckRepo.UpdateDeckAsync(deck);

        var plains = CreateCard("plains-2", "Plains", DeckFormat.Commander, LegalityStatus.Legal);
        plains.CardType = "Basic Land — Plains";
        _cardRepo.AddCard(plains);

        var island = CreateCard("island-2", "Island", DeckFormat.Commander, LegalityStatus.Legal);
        island.CardType = "Basic Land — Island";
        _cardRepo.AddCard(island);

        var swamp = CreateCard("swamp-2", "Swamp", DeckFormat.Commander, LegalityStatus.Legal);
        swamp.CardType = "Basic Land — Swamp";
        _cardRepo.AddCard(swamp);

        var added = await _service.AutoSuggestLandsAsync(deckId);
        Assert.Equal(37, added);

        var deckCards = await _deckRepo.GetDeckCardsAsync(deckId);
        var mainCards = deckCards.Where(c => c.Section == "Main").ToList();
        Assert.Equal(3, mainCards.Count);

        // With 3 colors: 13/12/12 distribution for 37.
        var quantities = mainCards.Select(c => c.Quantity).OrderByDescending(q => q).ToArray();
        Assert.Equal([13, 12, 12], quantities);

        var updatedDeck = await _deckRepo.GetDeckAsync(deckId);
        Assert.False(string.IsNullOrWhiteSpace(updatedDeck!.ColorIdentity));
    }

    [Fact]
    public async Task AddCard_RelentlessCard_AllowsMoreThan4Copies()
    {
        var deckId = await _service.CreateDeckAsync("Modern Deck", DeckFormat.Modern);
        var relentless = CreateCard("rats-1", "Relentless Rats", DeckFormat.Modern, LegalityStatus.Legal);
        relentless.Text = "A deck can have any number of cards named Relentless Rats.";
        _cardRepo.AddCard(relentless);

        await _service.AddCardAsync(deckId, relentless.Uuid, 4);
        var result = await _service.AddCardAsync(deckId, relentless.Uuid, 20);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task AddCard_VintageRestricted_AllowsOneCopy()
    {
        var deckId = await _service.CreateDeckAsync("Vintage Deck", DeckFormat.Vintage);
        var restricted = new Card
        {
            Uuid = "restricted-1",
            Name = "Black Lotus",
            CardType = "Artifact",
            Legalities = new CardLegalities(),
            Colors = "",
            Text = ""
        };
        restricted.Legalities[DeckFormat.Vintage] = LegalityStatus.Restricted;
        _cardRepo.AddCard(restricted);

        var result = await _service.AddCardAsync(deckId, restricted.Uuid, 1);
        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task AddCard_VintageRestricted_RejectsTwoCopies()
    {
        var deckId = await _service.CreateDeckAsync("Vintage Deck", DeckFormat.Vintage);
        var restricted = new Card
        {
            Uuid = "restricted-2",
            Name = "Ancestral Recall",
            CardType = "Instant",
            Legalities = new CardLegalities(),
            Colors = "U",
            Text = ""
        };
        restricted.Legalities[DeckFormat.Vintage] = LegalityStatus.Restricted;
        _cardRepo.AddCard(restricted);

        await _service.AddCardAsync(deckId, restricted.Uuid, 1);
        var result = await _service.AddCardAsync(deckId, restricted.Uuid, 1); // total 2

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

        var result = await _service.SetCommanderAsync(deckId, card.Uuid);
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

        var result = await _service.SetCommanderAsync(deckId, card.Uuid);
        Assert.True(result.IsError);
        Assert.Contains("cannot be a commander", result.Message);
    }

    [Fact]
    public async Task SetCommander_Planeswalker_BrawlFormat_Succeeds()
    {
        var deckId = await _service.CreateDeckAsync("Brawl Deck", DeckFormat.Brawl);
        var pw = new Card
        {
            Uuid = "pw-1",
            Name = "Jace, the Mind Sculptor",
            CardType = "Legendary Planeswalker — Jace",
            Legalities = new CardLegalities(),
            Colors = "U",
            Text = ""
        };
        pw.Legalities[DeckFormat.Brawl] = LegalityStatus.Legal;
        _cardRepo.AddCard(pw);

        var result = await _service.SetCommanderAsync(deckId, pw.Uuid);
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

        var result = await _service.AddCardAsync(deckId, card.Uuid, 1);
        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task AddCard_CommanderFormat_MaxOneCopyPerCard()
    {
        var deckId = await _service.CreateDeckAsync("Commander Deck", DeckFormat.Commander);
        var card = CreateCard("sol-ring", "Sol Ring", DeckFormat.Commander, LegalityStatus.Legal);
        _cardRepo.AddCard(card);

        await _service.AddCardAsync(deckId, card.Uuid, 1);
        var result = await _service.AddCardAsync(deckId, card.Uuid, 1); // total 2

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
        await _service.AddCardAsync(deckId, card.Uuid, 2);

        var result = await _service.UpdateQuantityAsync(deckId, card.Uuid, 0, "Main");

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
        await _service.AddCardAsync(deckId, card.Uuid, 2);

        var result = await _service.UpdateQuantityAsync(deckId, card.Uuid, -1, "Main");

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
        await _service.AddCardAsync(deckId, card.Uuid, 2);

        await _service.RemoveCardAsync(deckId, card.Uuid, "Main");

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

        await _service.AddCardAsync(deckId, card.Uuid, 4, "Main");
        // Note: adding to sideboard after 4 in main will fail total-quantity check
        // because GetTotalQuantity sums across all sections.
        // This test documents the current behavior.
        var sideResult = await _service.AddCardAsync(deckId, card.Uuid, 1, "Sideboard");

        // Expect error: total across all sections (4+1=5) exceeds max 4
        Assert.True(sideResult.IsError);
        Assert.Contains("more than 4 copies", sideResult.Message);
    }

    private Card CreateCard(string uuid, string name, DeckFormat format, LegalityStatus legality)
    {
        var card = new Card
        {
            Uuid = uuid,
            Name = name,
            CardType = "Creature",
            Legalities = new CardLegalities(),
            Colors = "",
            Text = ""
        };
        card.Legalities[format] = legality;
        return card;
    }

    [Fact]
    public async Task ApplyEditorMutations_BatchAddTwoCards_SucceedsAtomically()
    {
        var deckId = await _service.CreateDeckAsync("Batch", DeckFormat.Standard);
        var a = CreateCard("a1", "Card A", DeckFormat.Standard, LegalityStatus.Legal);
        var b = CreateCard("b1", "Card B", DeckFormat.Standard, LegalityStatus.Legal);
        _cardRepo.AddCard(a);
        _cardRepo.AddCard(b);

        var mutations = new DeckEditorMutation[]
        {
            new(DeckEditorMutationKind.Add, a.Uuid, "Main", null, 2),
            new(DeckEditorMutationKind.Add, b.Uuid, "Main", null, 1),
        };

        var result = await _service.ApplyEditorMutationsAsync(deckId, mutations);

        Assert.True(result.IsSuccess);
        var cards = await _deckRepo.GetDeckCardsAsync(deckId);
        Assert.Equal(2, cards.Count);
        Assert.Equal(2, cards.Single(c => c.CardId == "a1").Quantity);
        Assert.Equal(1, cards.Single(c => c.CardId == "b1").Quantity);
    }

    [Fact]
    public async Task ApplyEditorMutations_SecondCardInvalid_NoPartialApply()
    {
        var deckId = await _service.CreateDeckAsync("Partial", DeckFormat.Standard);
        var legal = CreateCard("ok", "Ok", DeckFormat.Standard, LegalityStatus.Legal);
        var banned = CreateCard("bad", "Bad", DeckFormat.Standard, LegalityStatus.Banned);
        _cardRepo.AddCard(legal);
        _cardRepo.AddCard(banned);

        var mutations = new DeckEditorMutation[]
        {
            new(DeckEditorMutationKind.Add, legal.Uuid, "Main", null, 1),
            new(DeckEditorMutationKind.Add, banned.Uuid, "Main", null, 1),
        };

        var result = await _service.ApplyEditorMutationsAsync(deckId, mutations);

        Assert.True(result.IsError);
        var cards = await _deckRepo.GetDeckCardsAsync(deckId);
        Assert.Empty(cards);
    }

    [Fact]
    public async Task ApplyEditorMutations_MoveMainToSideboard_PreservesTotalCopies()
    {
        var deckId = await _service.CreateDeckAsync("Move", DeckFormat.Standard);
        var card = CreateCard("m1", "Mover", DeckFormat.Standard, LegalityStatus.Legal);
        _cardRepo.AddCard(card);
        await _service.AddCardAsync(deckId, card.Uuid, 3, "Main");

        var result = await _service.ApplyEditorMutationsAsync(deckId,
        [
            new DeckEditorMutation(DeckEditorMutationKind.Move, card.Uuid, "Main", "Sideboard", 0)
        ]);

        Assert.True(result.IsSuccess);
        var cards = await _deckRepo.GetDeckCardsAsync(deckId);
        Assert.DoesNotContain(cards, c => c.Section == "Main");
        var sb = cards.Single(c => c.Section == "Sideboard");
        Assert.Equal(3, sb.Quantity);
    }

    [Fact]
    public void DeckFormat_UsesCommanderZone_MatchesEditorTabs()
    {
        Assert.True(DeckFormat.Commander.UsesCommanderZone());
        Assert.True(DeckFormat.Brawl.UsesCommanderZone());
        Assert.True(DeckFormat.PauperCommander.UsesCommanderZone());
        Assert.True(DeckFormat.Oathbreaker.UsesCommanderZone());
        Assert.True(DeckFormat.StandardBrawl.UsesCommanderZone());
        Assert.False(DeckFormat.Standard.UsesCommanderZone());
        Assert.False(DeckFormat.Modern.UsesCommanderZone());
    }

    [Fact]
    public async Task UpdateCommanderArchetypeAsync_Persists()
    {
        int deckId = await _service.CreateDeckAsync("Cmd", DeckFormat.Commander);
        await _service.UpdateCommanderArchetypeAsync(deckId, CommanderArchetype.Spellslinger);
        var deck = await _deckRepo.GetDeckAsync(deckId);
        Assert.NotNull(deck);
        Assert.Equal(CommanderArchetype.Spellslinger.ToArchetypeDbValue(), deck!.CommanderArchetype);
    }

    [Fact]
    public async Task ValidateDeckAsync_WithPreloadedCardMap_SkipsPerRowGetCardDetails()
    {
        var commander = CreateCard("cmd-map", "Commander", DeckFormat.Commander, LegalityStatus.Legal);
        commander.CardType = "Legendary Creature";
        commander.Colors = "W";
        _cardRepo.AddCard(commander);

        var entities = new List<DeckCardEntity>();
        for (int i = 0; i < 20; i++)
        {
            string id = $"map-{i}";
            var c = CreateCard(id, $"Card {i}", DeckFormat.Commander, LegalityStatus.Legal);
            c.Colors = "W";
            _cardRepo.AddCard(c);
            entities.Add(new DeckCardEntity { CardId = id, Section = "Main", Quantity = 1 });
        }

        var deck = new DeckEntity
        {
            Format = DeckFormat.Commander.ToDbField(),
            CommanderId = commander.Uuid,
            ColorIdentity = "W"
        };

        var uuids = entities.Select(e => e.CardId).Append(commander.Uuid).Distinct().ToArray();
        var map = await _cardRepo.GetCardsAsync(uuids);

        Assert.Equal(0, _cardRepo.GetCardDetailsAsyncCallCount);

        await _service.ValidateDeckAsync(deck, entities, map);

        Assert.Equal(0, _cardRepo.GetCardDetailsAsyncCallCount);
    }

    [Fact]
    public async Task ValidateDeckAsync_WithPartialCardMap_FallsBackToGetCardDetailsForMissingUuids()
    {
        var commander = CreateCard("cmd-partial", "Commander", DeckFormat.Commander, LegalityStatus.Legal);
        commander.CardType = "Legendary Creature";
        commander.Colors = "W";
        _cardRepo.AddCard(commander);

        var present = CreateCard("present-1", "In Map", DeckFormat.Commander, LegalityStatus.Legal);
        present.Colors = "W";
        _cardRepo.AddCard(present);

        var missing = CreateCard("missing-1", "Not In Map", DeckFormat.Commander, LegalityStatus.Legal);
        missing.Colors = "W";
        _cardRepo.AddCard(missing);

        var entities = new List<DeckCardEntity>
        {
            new() { CardId = present.Uuid, Section = "Main", Quantity = 1 },
            new() { CardId = missing.Uuid, Section = "Main", Quantity = 1 },
        };

        var deck = new DeckEntity
        {
            Format = DeckFormat.Commander.ToDbField(),
            CommanderId = commander.Uuid,
            ColorIdentity = "W"
        };

        var partialMap = new Dictionary<string, Card>(StringComparer.OrdinalIgnoreCase)
        {
            [present.Uuid] = present,
        };

        int before = _cardRepo.GetCardDetailsAsyncCallCount;
        await _service.ValidateDeckAsync(deck, entities, partialMap);
        int after = _cardRepo.GetCardDetailsAsyncCallCount;

        // Commander resolution (not in partial map) + one fallback fetch for the main-deck row missing from the map.
        Assert.Equal(before + 2, after);
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
        var list = _decks.ToList();
        foreach (var d in list)
            d.CardCount = _deckCards.Where(c => c.DeckId == d.Id).Sum(c => c.Quantity);
        return Task.FromResult(list);
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

    public Task ApplyMutationsAsync(int deckId, IReadOnlyList<DeckCardPersistenceMutation> mutations)
    {
        foreach (var m in mutations)
        {
            switch (m.Kind)
            {
                case DeckCardPersistenceKind.Remove:
                    _deckCards.RemoveAll(c => c.DeckId == deckId && c.CardId == m.CardId && c.Section == m.Section);
                    break;
                case DeckCardPersistenceKind.UpdateQuantity:
                    {
                        var row = _deckCards.FirstOrDefault(c => c.DeckId == deckId && c.CardId == m.CardId && c.Section == m.Section);
                        if (m.Quantity <= 0)
                        {
                            if (row != null)
                                _deckCards.Remove(row);
                        }
                        else if (row != null)
                        {
                            row.Quantity = m.Quantity;
                        }
                        else
                        {
                            _deckCards.Add(new DeckCardEntity
                            {
                                DeckId = deckId,
                                CardId = m.CardId,
                                Section = m.Section,
                                Quantity = m.Quantity,
                                DateAdded = m.DateAdded ?? DateTime.Now
                            });
                        }

                        break;
                    }
                case DeckCardPersistenceKind.InsertOrReplace:
                    {
                        var old = _deckCards.FirstOrDefault(c => c.DeckId == deckId && c.CardId == m.CardId && c.Section == m.Section);
                        if (old != null)
                            _deckCards.Remove(old);
                        _deckCards.Add(new DeckCardEntity
                        {
                            DeckId = deckId,
                            CardId = m.CardId,
                            Section = m.Section,
                            Quantity = m.Quantity,
                            DateAdded = m.DateAdded ?? DateTime.Now
                        });
                        break;
                    }
            }
        }

        return Task.CompletedTask;
    }
}

public class MockCardRepository : ICardRepository
{
    private readonly Dictionary<string, Card> _cards = new();

    /// <summary>Increments on each <see cref="GetCardDetailsAsync"/> call (for perf-related tests).</summary>
    public int GetCardDetailsAsyncCallCount { get; private set; }

    public void ResetGetCardDetailsCallCount() => GetCardDetailsAsyncCallCount = 0;

    public void AddCard(Card card)
    {
        _cards[card.Uuid] = card;
    }

    public Task<Card> GetCardDetailsAsync(string uuid)
    {
        GetCardDetailsAsyncCallCount++;
        return Task.FromResult(_cards.ContainsKey(uuid) ? _cards[uuid] : null!);
    }

    // Stub other methods
    public Task<Card> GetCardByUuidAsync(string uuid) => GetCardDetailsAsync(uuid);
    public Task<Card> GetCardWithLegalitiesAsync(string uuid) => GetCardDetailsAsync(uuid);
    public Task<Card> GetCardWithRulingsAsync(string uuid) => GetCardDetailsAsync(uuid);
    public Task<Card> GetCardByFaceAndSetAsync(string faceName, string setCode) => throw new NotImplementedException();
    public Task<string> GetScryfallIdAsync(string cardUUID) => throw new NotImplementedException();
    public Task<CardRuling[]> GetCardRulingsAsync(string uuid) => throw new NotImplementedException();
    public Task<string[]> GetOtherFaceIdsAsync(string uuid) => throw new NotImplementedException();
    public Task<Card[]> GetOtherFacesAsync(string uuid) => throw new NotImplementedException();
    public Task<Card[]> GetFullCardPackageAsync(string uuid) => throw new NotImplementedException();
    public Task<Dictionary<string, Card>> GetCardsAsync(string[] uuids)
    {
        var dict = new Dictionary<string, Card>(StringComparer.OrdinalIgnoreCase);
        foreach (var id in uuids)
        {
            if (_cards.TryGetValue(id, out var card))
                dict[id] = card;
        }
        return Task.FromResult(dict);
    }
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

    public Task<Card[]> SearchAdvancedAsync(MtgSearchHelper searchHelper)
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

    public async Task<(Card[] cards, int totalCount)> SearchAdvancedWithResultTotalAsync(MtgSearchHelper searchHelper)
    {
        var cards = await SearchAdvancedAsync(searchHelper);
        return (cards, cards.Length);
    }

    public Task<int> CountAdvancedAsync(MtgSearchHelper searchHelper) => throw new NotImplementedException();
    public MtgSearchHelper CreateSearchHelper() => new();
    public Task<IReadOnlyList<ImportLookupRow>> GetImportLookupRowsAsync() => Task.FromResult<IReadOnlyList<ImportLookupRow>>([]);
    public Task<IReadOnlyList<SetInfo>> GetAllSetsAsync() => Task.FromResult<IReadOnlyList<SetInfo>>([]);
    public Task<IReadOnlyList<SetBrowseRow>> GetSetsBrowseAsync() => Task.FromResult<IReadOnlyList<SetBrowseRow>>([]);
    public Task<bool> HasFtsAsync() => Task.FromResult(false);

    public Task<IReadOnlyList<OtherPrintingSummary>> GetOtherPrintingsByOracleIdAsync(string oracleId, string currentUuid) =>
        Task.FromResult<IReadOnlyList<OtherPrintingSummary>>([]);

    public Task<Card?> GetCardByScryfallIdAsync(string scryfallId)
    {
        var card = _cards.Values.FirstOrDefault(c => string.Equals(c.ScryfallId, scryfallId, StringComparison.OrdinalIgnoreCase));
        return Task.FromResult(card);
    }
    public Task<Card?> GetCardByNameAndSetAsync(string name, string setCode)
    {
        var card = _cards.Values.FirstOrDefault(c =>
            string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(c.SetCode, setCode, StringComparison.OrdinalIgnoreCase));
        return Task.FromResult(card);
    }
}
