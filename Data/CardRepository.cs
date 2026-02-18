using Microsoft.Data.Sqlite;
using MTGFetchMAUI.Core;
using MTGFetchMAUI.Models;

namespace MTGFetchMAUI.Data;

/// <summary>
/// Async card data access using Microsoft.Data.Sqlite.
/// Port of TCardRepository from CardRepository.pas.
/// </summary>
public class CardRepository : ICardRepository
{
    private readonly DatabaseManager _db;
    private readonly SemaphoreSlim _lock = new(1, 1);

    public CardRepository(DatabaseManager databaseManager)
    {
        _db = databaseManager;
    }

    public Task<Card> GetCardByUUIDAsync(string uuid) => GetCardWithLegalitiesAsync(uuid);

    public async Task<Card> GetCardWithLegalitiesAsync(string uuid)
    {
        return await WithMTGReaderAsync(
            SQLQueries.SelectFullCard + SQLQueries.WhereUuidEquals,
            cmd => cmd.Parameters.AddWithValue("@uuid", uuid),
            async reader => await reader.ReadAsync() ? CardMapper.MapCard(reader) : new Card());
    }

    public async Task<Card> GetCardDetailsAsync(string uuid)
    {
        var card = await GetCardWithLegalitiesAsync(uuid);

        if (card.Layout is CardLayout.Adventure or CardLayout.Split)
        {
            var otherFaces = await GetCardWithOtherFacesAsync(uuid);
            if (otherFaces.Length > 1)
                card.Text = otherFaces[0].Text + Environment.NewLine + "---" + Environment.NewLine + otherFaces[1].Text;
        }

        return card;
    }

    public async Task<Card> GetCardWithRulingsAsync(string uuid)
    {
        var card = await GetCardWithLegalitiesAsync(uuid);
        if (!string.IsNullOrEmpty(card.UUID))
            card.Rulings = (await GetCardRulingsAsync(uuid)).ToList();
        return card;
    }

    public async Task<Card> GetCardByFaceNameAndSetAsync(string faceName, string setCode)
    {
        return await WithMTGReaderAsync(
            SQLQueries.SelectFullCard + " WHERE c.faceName = @fname AND c.setCode = @set LIMIT 1",
            cmd =>
            {
                cmd.Parameters.AddWithValue("@fname", faceName);
                cmd.Parameters.AddWithValue("@set", setCode);
            },
            async reader => await reader.ReadAsync() ? CardMapper.MapCard(reader) : new Card());
    }

    public async Task<string> GetScryfallIdAsync(string cardUUID)
    {
        return await WithMTGReaderAsync(
            SQLQueries.SelectScryfallId,
            cmd => cmd.Parameters.AddWithValue("@uuid", cardUUID),
            async reader => await reader.ReadAsync() ? reader.GetString(0) : "");
    }

    public async Task<CardRuling[]> GetCardRulingsAsync(string uuid)
    {
        var rulings = new List<CardRuling>();

        await WithMTGReaderAsync(
            SQLQueries.SelectRulings,
            cmd => cmd.Parameters.AddWithValue("@uuid", uuid),
            async reader =>
            {
                while (await reader.ReadAsync())
                {
                    var dateStr = reader.GetString(0);
                    var text = reader.GetString(1);
                    DateTime.TryParse(dateStr, out var date);
                    rulings.Add(new CardRuling(date, text));
                }
                return 0;
            });

        return rulings.ToArray();
    }

    public async Task<string[]> GetOtherFaceIdsAsync(string uuid)
    {
        return await WithMTGReaderAsync(
            SQLQueries.SelectOtherFaces,
            cmd => cmd.Parameters.AddWithValue("@uuid", uuid),
            async reader =>
            {
                if (!await reader.ReadAsync()) return Array.Empty<string>();
                var raw = reader.IsDBNull(0) ? "" : reader.GetString(0);
                return CardMapper.ParseOtherFaceIds(raw);
            });
    }

    public async Task<Card[]> GetCardWithOtherFacesAsync(string uuid)
    {
        var otherIds = await GetOtherFaceIdsAsync(uuid);
        var allIds = new List<string> { uuid };
        allIds.AddRange(otherIds);

        var dict = await GetCardsByUUIDsAsync(allIds.ToArray());
        return SortCardsBySide(dict.Values.ToArray());
    }

    public async Task<Card[]> GetFullCardPackageAsync(string uuid)
    {
        var mainCard = await GetCardWithRulingsAsync(uuid);
        if (string.IsNullOrEmpty(mainCard.UUID)) return [];

        var cards = new List<Card> { mainCard };

        // CASE A: Meld Cards (linked by name in CardParts JSON)
        if (mainCard.Layout == CardLayout.Meld && !string.IsNullOrEmpty(mainCard.CardParts))
        {
            var cardParts = CardMapper.ParseJsonArrayToStrings(mainCard.CardParts);
            if (cardParts.Length > 0)
            {
                var meldCards = await GetMeldPartCardsAsync(cardParts, mainCard.SetCode, mainCard.UUID);
                cards.AddRange(meldCards);
            }
        }
        // CASE B: Standard multi-face (Transform, Adventure, Split, etc.)
        else
        {
            var otherIds = CardMapper.ParseOtherFaceIds(mainCard.OtherFaceIds);
            if (otherIds.Length > 0)
            {
                var dict = await GetCardsByUUIDsAsync(otherIds);
                cards.AddRange(dict.Values);
            }
        }

        return SortCardsBySide(cards.ToArray());
    }

    public async Task<Dictionary<string, Card>> GetCardsByUUIDsAsync(string[] uuids)
    {
        var result = new Dictionary<string, Card>();
        if (uuids.Length == 0) return result;

        const int chunkSize = 500;
        for (int i = 0; i < uuids.Length; i += chunkSize)
        {
            var chunk = uuids.Skip(i).Take(chunkSize).ToArray();
            var paramNames = chunk.Select((_, idx) => $"@u{idx}").ToArray();
            var sql = SQLQueries.SelectFullCard + " WHERE c.uuid IN (" + string.Join(",", paramNames) + ")";

            await WithMTGReaderAsync(sql, cmd =>
            {
                for (int j = 0; j < chunk.Length; j++)
                    cmd.Parameters.AddWithValue(paramNames[j], chunk[j]);
            },
            async reader =>
            {
                while (await reader.ReadAsync())
                {
                    var card = CardMapper.MapCard(reader);
                    result[card.UUID] = card;
                }
                return 0;
            });
        }

        return result;
    }

    public async Task<Card[]> SearchCardsAsync(string searchText, int limit = 100)
    {
        var helper = CreateSearchHelper();
        helper.SearchCards()
            .WhereNameContains(searchText)
            .WherePrimarySideOnly()
            .OrderBy("c.name")
            .Limit(limit);
        return await SearchCardsAdvancedAsync(helper);
    }

    public async Task<Card[]> SearchCardsAdvancedAsync(MTGSearchHelper searchHelper)
    {
        var cards = new List<Card>();
        var (sql, parameters) = searchHelper.Build();

        await WithMTGReaderAsync(sql, cmd =>
        {
            foreach (var (name, value) in parameters)
                cmd.Parameters.AddWithValue(name, value);
        },
        async reader =>
        {
            while (await reader.ReadAsync())
                cards.Add(CardMapper.MapCard(reader));
            return 0;
        });

        return cards.ToArray();
    }

    public MTGSearchHelper CreateSearchHelper() => new();

    // ── Private helpers ─────────────────────────────────────────────

    private async Task<Card[]> GetMeldPartCardsAsync(string[] cardParts, string setCode, string mainUUID)
    {
        var cards = new List<Card>();

        var conditions = new List<string>();
        var parameters = new List<(string name, object value)>
        {
            ("@set", setCode),
            ("@mainUUID", mainUUID)
        };

        for (int i = 0; i < cardParts.Length; i++)
        {
            conditions.Add($"(c.faceName = @n{i} OR c.name = @n{i})");
            parameters.Add(($"@n{i}", cardParts[i].Trim()));
        }

        var sql = SQLQueries.SelectFullCard +
            $" WHERE c.setCode = @set AND ({string.Join(" OR ", conditions)}) AND c.uuid <> @mainUUID";

        await WithMTGReaderAsync(sql, cmd =>
        {
            foreach (var (name, value) in parameters)
                cmd.Parameters.AddWithValue(name, value);
        },
        async reader =>
        {
            while (await reader.ReadAsync())
                cards.Add(CardMapper.MapCard(reader));
            return 0;
        });

        return cards.ToArray();
    }

    private static Card[] SortCardsBySide(Card[] cards)
    {
        return cards.OrderBy(c => c.Side).ToArray();
    }

    /// <summary>
    /// Executes a query against the MTG database and processes results with an async reader.
    /// Uses SemaphoreSlim for non-blocking thread safety.
    /// </summary>
    private async Task<T> WithMTGReaderAsync<T>(string sql, Action<SqliteCommand> configureParams, Func<SqliteDataReader, Task<T>> readFunc)
    {
        await _lock.WaitAsync();
        try
        {
            using var cmd = _db.MTGConnection.CreateCommand();
            cmd.CommandText = sql;
            configureParams(cmd);
            using var reader = await cmd.ExecuteReaderAsync() as SqliteDataReader
                ?? throw new InvalidOperationException("Failed to create SqliteDataReader.");
            return await readFunc(reader);
        }
        finally
        {
            _lock.Release();
        }
    }
}
