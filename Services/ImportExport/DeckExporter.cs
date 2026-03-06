using AetherVault.Data;
using AetherVault.Models;
using CsvHelper;
using CsvHelper.Configuration;
using System.Globalization;

namespace AetherVault.Services.ImportExport;

public class DeckExporter
{
    private readonly IDeckRepository _deckRepo;
    private readonly ICardRepository _cardRepo;

    public DeckExporter(IDeckRepository deckRepo, ICardRepository cardRepo)
    {
        _deckRepo = deckRepo;
        _cardRepo = cardRepo;
    }

    public async Task<string> ExportDeckToCsvAsync(int deckId)
    {
        var deck = await _deckRepo.GetDeckAsync(deckId);
        if (deck == null) return "";

        var decks = new List<DeckEntity> { deck };
        var deckCardsByDeckId = new Dictionary<int, List<DeckCardEntity>>
        {
            [deck.Id] = await _deckRepo.GetDeckCardsAsync(deck.Id)
        };

        return await ExportInternalAsync(decks, deckCardsByDeckId);
    }

    public async Task<string> ExportAllDecksToCsvAsync()
    {
        var decks = await _deckRepo.GetAllDecksAsync();
        if (decks.Count == 0) return "";

        var deckCardsByDeckId = new Dictionary<int, List<DeckCardEntity>>();
        foreach (var deck in decks)
        {
            deckCardsByDeckId[deck.Id] = await _deckRepo.GetDeckCardsAsync(deck.Id);
        }

        return await ExportInternalAsync(decks, deckCardsByDeckId);
    }

    private async Task<string> ExportInternalAsync(
        IReadOnlyList<DeckEntity> decks,
        IReadOnlyDictionary<int, List<DeckCardEntity>> deckCardsByDeckId)
    {
        var allUuids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var deck in decks)
        {
            if (!deckCardsByDeckId.TryGetValue(deck.Id, out var cards)) continue;
            for (int i = 0; i < cards.Count; i++)
            {
                if (!string.IsNullOrWhiteSpace(cards[i].CardId))
                {
                    allUuids.Add(cards[i].CardId.Trim());
                }
            }

            if (!string.IsNullOrWhiteSpace(deck.CommanderId))
            {
                allUuids.Add(deck.CommanderId.Trim());
            }
        }

        Dictionary<string, Card> cardMap = allUuids.Count > 0
            ? await _cardRepo.GetCardsByUUIDsAsync(allUuids.ToArray())
            : [];

        var config = new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            HasHeaderRecord = true
        };

        using var stringWriter = new StringWriter();
        using var csv = new CsvWriter(stringWriter, config);

        // Header
        foreach (var header in DeckCsvV1.HeaderOrder)
        {
            csv.WriteField(header);
        }
        await csv.NextRecordAsync();

        foreach (var deck in decks)
        {
            if (!deckCardsByDeckId.TryGetValue(deck.Id, out var cards))
            {
                cards = [];
            }

            // Back-compat: ensure commander row exists if CommanderId is set.
            if (!string.IsNullOrWhiteSpace(deck.CommanderId))
            {
                bool hasCommanderRow = cards.Any(c =>
                    c.Section.Equals(DeckCsvV1.Sections.Commander, StringComparison.OrdinalIgnoreCase) &&
                    c.CardId.Equals(deck.CommanderId, StringComparison.OrdinalIgnoreCase));

                if (!hasCommanderRow)
                {
                    cards = [.. cards, new DeckCardEntity
                    {
                        DeckId = deck.Id,
                        CardId = deck.CommanderId,
                        Quantity = 1,
                        Section = DeckCsvV1.Sections.Commander,
                        DateAdded = deck.DateModified
                    }];
                }
            }

            for (int i = 0; i < cards.Count; i++)
            {
                var entity = cards[i];
                var uuid = entity.CardId?.Trim() ?? "";
                cardMap.TryGetValue(uuid, out var card);

                csv.WriteField(DeckCsvV1.Version); // Source
                csv.WriteField(deck.Name);
                csv.WriteField(deck.Format);
                csv.WriteField(DeckCsvV1.Sections.Normalize(entity.Section));
                csv.WriteField(entity.Quantity);
                csv.WriteField(uuid);
                csv.WriteField(card?.Name ?? "");
                csv.WriteField(card?.SetCode ?? "");
                csv.WriteField(card?.Number ?? "");
                csv.WriteField(card?.ScryfallId ?? "");
                await csv.NextRecordAsync();
            }
        }

        return stringWriter.ToString();
    }
}

