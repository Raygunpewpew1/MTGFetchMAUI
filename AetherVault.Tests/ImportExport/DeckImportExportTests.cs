using AetherVault.Core;
using AetherVault.Data;
using AetherVault.Models;
using AetherVault.Services.DeckBuilder;
using AetherVault.Services.ImportExport;

namespace AetherVault.Tests.ImportExport;

public class DeckImportExportTests
{
    [Fact]
    public async Task ImportCsv_CreatesNewDeckWithSuffix_WhenNameCollides()
    {
        var deckRepo = new InMemoryDeckRepository();
        var cardRepo = new FakeCardRepository(new Dictionary<string, Card>(StringComparer.OrdinalIgnoreCase)
        {
            ["uuid1"] = new Card { UUID = "uuid1", Name = "Opt", CardType = "Instant", Text = "", Legalities = LegalEverywhere() }
        });

        var validator = new DeckValidator(cardRepo);
        var deckService = new DeckBuilderService(deckRepo, validator, cardRepo);

        // Existing deck with same name
        await deckService.CreateDeckAsync("MyDeck", DeckFormat.Standard, "");

        var importer = new DeckImporter(deckService, cardRepo);

        var csv = string.Join("\n",
        [
            "Deck Name,Format,Section,Quantity,Card UUID",
            "MyDeck,standard,Main,1,uuid1",
        ]);

        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(csv));
        var result = await importer.ImportCsvAsync(stream);

        Assert.Equal(1, result.ImportedDecks);

        var decks = await deckRepo.GetAllDecksAsync();
        Assert.Contains(decks, d => d.Name == "MyDeck");
        Assert.Contains(decks, d => d.Name == "MyDeck (import 2)");
    }

    [Fact]
    public async Task ExportThenImport_RoundTrip_PreservesSectionsAndQuantities_WhenUuidPresent()
    {
        var deckRepo = new InMemoryDeckRepository();
        var cardRepo = new FakeCardRepository(new Dictionary<string, Card>(StringComparer.OrdinalIgnoreCase)
        {
            ["uuidA"] = new Card { UUID = "uuidA", Name = "Shock", CardType = "Instant", Text = "", SetCode = "M21", Number = "159", ScryfallId = "s1", Legalities = LegalEverywhere() },
            ["uuidB"] = new Card { UUID = "uuidB", Name = "Negate", CardType = "Instant", Text = "", SetCode = "M21", Number = "59", ScryfallId = "s2", Legalities = LegalEverywhere() },
        });

        var validator = new DeckValidator(cardRepo);
        var deckService = new DeckBuilderService(deckRepo, validator, cardRepo);

        int deckId = await deckService.CreateDeckAsync("RoundTrip", DeckFormat.Standard, "");
        var add1 = await deckService.AddCardAsync(deckId, "uuidA", 2, "Main");
        var add2 = await deckService.AddCardAsync(deckId, "uuidB", 1, "Sideboard");

        Assert.True(add1.IsSuccess);
        Assert.True(add2.IsSuccess);

        var exporter = new DeckExporter(deckRepo, cardRepo);
        var csvText = await exporter.ExportDeckToCsvAsync(deckId);
        Assert.Contains("Card UUID", csvText);

        var importer = new DeckImporter(deckService, cardRepo);
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(csvText));
        var importResult = await importer.ImportCsvAsync(stream);

        Assert.Equal(1, importResult.ImportedDecks);
        Assert.True(importResult.Errors.Count == 0);

        var allDecks = await deckRepo.GetAllDecksAsync();
        var imported = allDecks.Single(d => d.Name == "RoundTrip (import 2)");

        var cards = await deckRepo.GetDeckCardsAsync(imported.Id);
        Assert.Contains(cards, c => c.CardId == "uuidA" && c.Section == "Main" && c.Quantity == 2);
        Assert.Contains(cards, c => c.CardId == "uuidB" && c.Section == "Sideboard" && c.Quantity == 1);
    }

    private sealed class InMemoryDeckRepository : IDeckRepository
    {
        private int _nextDeckId = 1;
        private readonly List<DeckEntity> _decks = [];
        private readonly List<DeckCardEntity> _cards = [];

        public Task<int> CreateDeckAsync(DeckEntity deck)
        {
            deck.Id = _nextDeckId++;
            deck.DateCreated = deck.DateCreated == default ? DateTime.Now : deck.DateCreated;
            deck.DateModified = deck.DateModified == default ? DateTime.Now : deck.DateModified;
            _decks.Add(Clone(deck));
            return Task.FromResult(deck.Id);
        }

        public Task UpdateDeckAsync(DeckEntity deck)
        {
            var idx = _decks.FindIndex(d => d.Id == deck.Id);
            if (idx >= 0)
            {
                _decks[idx] = Clone(deck);
            }
            return Task.CompletedTask;
        }

        public Task DeleteDeckAsync(int deckId)
        {
            _decks.RemoveAll(d => d.Id == deckId);
            _cards.RemoveAll(c => c.DeckId == deckId);
            return Task.CompletedTask;
        }

        public Task<DeckEntity?> GetDeckAsync(int deckId) =>
            Task.FromResult(_decks.FirstOrDefault(d => d.Id == deckId) is { } d ? Clone(d) : null);

        public Task<List<DeckEntity>> GetAllDecksAsync() =>
            Task.FromResult(_decks.Select(Clone).ToList());

        public Task AddCardToDeckAsync(DeckCardEntity card)
        {
            var idx = _cards.FindIndex(c =>
                c.DeckId == card.DeckId &&
                string.Equals(c.CardId, card.CardId, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(c.Section, card.Section, StringComparison.OrdinalIgnoreCase));

            if (idx >= 0)
            {
                _cards[idx] = Clone(card);
            }
            else
            {
                _cards.Add(Clone(card));
            }

            return Task.CompletedTask;
        }

        public Task RemoveCardFromDeckAsync(int deckId, string cardId, string section)
        {
            _cards.RemoveAll(c =>
                c.DeckId == deckId &&
                string.Equals(c.CardId, cardId, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(c.Section, section, StringComparison.OrdinalIgnoreCase));
            return Task.CompletedTask;
        }

        public Task UpdateCardQuantityAsync(int deckId, string cardId, string section, int quantity)
        {
            var idx = _cards.FindIndex(c =>
                c.DeckId == deckId &&
                string.Equals(c.CardId, cardId, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(c.Section, section, StringComparison.OrdinalIgnoreCase));

            if (idx >= 0)
            {
                var existing = _cards[idx];
                existing.Quantity = quantity;
                _cards[idx] = existing;
            }

            return Task.CompletedTask;
        }

        public Task<List<DeckCardEntity>> GetDeckCardsAsync(int deckId) =>
            Task.FromResult(_cards.Where(c => c.DeckId == deckId).Select(Clone).ToList());

        public Task<int> GetDeckCardCountAsync(int deckId) =>
            Task.FromResult(_cards.Where(c => c.DeckId == deckId).Sum(c => c.Quantity));

        public Task<Dictionary<int, int>> GetDeckCardCountsAsync(IEnumerable<int> deckIds)
        {
            var result = deckIds.ToDictionary(id => id, id => _cards.Where(c => c.DeckId == id).Sum(c => c.Quantity));
            return Task.FromResult(result);
        }

        private static DeckEntity Clone(DeckEntity d) => new()
        {
            Id = d.Id,
            Name = d.Name,
            Format = d.Format,
            Description = d.Description,
            CoverCardId = d.CoverCardId,
            DateCreated = d.DateCreated,
            DateModified = d.DateModified,
            CommanderId = d.CommanderId,
            CommanderName = d.CommanderName,
            PartnerId = d.PartnerId,
            ColorIdentity = d.ColorIdentity,
            CardCount = d.CardCount,
        };

        private static DeckCardEntity Clone(DeckCardEntity c) => new()
        {
            DeckId = c.DeckId,
            CardId = c.CardId,
            Quantity = c.Quantity,
            Section = c.Section,
            DateAdded = c.DateAdded,
        };
    }

    private static CardLegalities LegalEverywhere()
    {
        var legalities = new CardLegalities();
        foreach (var fmt in Enum.GetValues<DeckFormat>())
        {
            legalities[fmt] = LegalityStatus.Legal;
        }
        return legalities;
    }

    private sealed class FakeCardRepository : ICardRepository
    {
        private readonly Dictionary<string, Card> _cards;

        public FakeCardRepository(Dictionary<string, Card> cards)
        {
            _cards = cards;
        }

        public Task<Card> GetCardByUUIDAsync(string uuid) => Task.FromResult(Get(uuid));
        public Task<Card> GetCardDetailsAsync(string uuid) => Task.FromResult(Get(uuid));
        public Task<Card> GetCardWithLegalitiesAsync(string uuid) => Task.FromResult(Get(uuid));
        public Task<Card> GetCardWithRulingsAsync(string uuid) => Task.FromResult(Get(uuid));
        public Task<Card> GetCardByFaceNameAndSetAsync(string faceName, string setCode) => throw new NotImplementedException();

        public Task<string> GetScryfallIdAsync(string cardUUID) => Task.FromResult(Get(cardUUID).ScryfallId);
        public Task<CardRuling[]> GetCardRulingsAsync(string uuid) => Task.FromResult(Array.Empty<CardRuling>());

        public Task<string[]> GetOtherFaceIdsAsync(string uuid) => Task.FromResult(Array.Empty<string>());
        public Task<Card[]> GetCardWithOtherFacesAsync(string uuid) => Task.FromResult(new[] { Get(uuid) });
        public Task<Card[]> GetFullCardPackageAsync(string uuid) => Task.FromResult(new[] { Get(uuid) });

        public Task<Dictionary<string, Card>> GetCardsByUUIDsAsync(string[] uuids)
        {
            var result = new Dictionary<string, Card>(StringComparer.OrdinalIgnoreCase);
            foreach (var u in uuids)
            {
                if (_cards.TryGetValue(u, out var c))
                    result[u] = c;
            }
            return Task.FromResult(result);
        }

        public Task<IReadOnlyList<ImportLookupRow>> GetImportLookupRowsAsync() =>
            Task.FromResult<IReadOnlyList<ImportLookupRow>>([]);

        public Task<Card[]> SearchCardsAsync(string searchText, int limit = 100) => throw new NotImplementedException();
        public Task<Card[]> SearchCardsAdvancedAsync(MTGSearchHelper searchHelper) => throw new NotImplementedException();
        public Task<int> GetCountAdvancedAsync(MTGSearchHelper searchHelper) => throw new NotImplementedException();
        public MTGSearchHelper CreateSearchHelper() => throw new NotImplementedException();
        public Task<IReadOnlyList<SetInfo>> GetAllSetsAsync() => Task.FromResult<IReadOnlyList<SetInfo>>([]);
        public Task<bool> HasFtsAsync() => Task.FromResult(false);
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

        private Card Get(string uuid)
        {
            if (_cards.TryGetValue(uuid, out var card))
                return card;
            return new Card { UUID = uuid, Name = uuid, CardType = "Instant", Text = "" };
        }
    }
}

