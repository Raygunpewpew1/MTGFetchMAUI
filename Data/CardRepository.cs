using AetherVault.Core;
using AetherVault.Models;
using Dapper;
using System.Data.Common;

namespace AetherVault.Data;

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
            SQLQueries.BaseCardsAndTokens + SQLQueries.WhereUuidEquals,
            new { uuid },
            async reader =>
            {
                var o = new CardMapper.CardOrdinals(reader);
                return await reader.ReadAsync() ? CardMapper.MapCard(reader, o) : new Card();
            });
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
            new { fname = faceName, set = setCode },
            async reader =>
            {
                var o = new CardMapper.CardOrdinals(reader);
                return await reader.ReadAsync() ? CardMapper.MapCard(reader, o) : new Card();
            });
    }

    public async Task<string> GetScryfallIdAsync(string cardUUID)
    {
        await _lock.WaitAsync();
        try
        {
            var result = await _db.MTGConnection.QueryFirstOrDefaultAsync<string>(
                SQLQueries.SelectScryfallId, new { uuid = cardUUID });
            return result ?? "";
        }
        finally
        {
            _lock.Release();
        }
    }

    private class CardRulingRow
    {
        public string date { get; set; } = "";
        public string text { get; set; } = "";
    }

    public async Task<CardRuling[]> GetCardRulingsAsync(string uuid)
    {
        await _lock.WaitAsync();
        try
        {
            var rows = await _db.MTGConnection.QueryAsync<CardRulingRow>(
                SQLQueries.SelectRulings, new { uuid });

            var rulings = new List<CardRuling>();
            foreach (var row in rows)
            {
                DateTime.TryParse(row.date, out var date);
                rulings.Add(new CardRuling(date, row.text));
            }

            return [.. rulings];
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<string[]> GetOtherFaceIdsAsync(string uuid)
    {
        await _lock.WaitAsync();
        try
        {
            var raw = await _db.MTGConnection.QueryFirstOrDefaultAsync<string>(
                SQLQueries.SelectOtherFaces, new { uuid });
            return CardMapper.ParseOtherFaceIds(raw ?? "");
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<Card[]> GetCardWithOtherFacesAsync(string uuid)
    {
        var otherIds = await GetOtherFaceIdsAsync(uuid);
        var allIds = new[] { uuid }.Concat(otherIds).ToArray();
        var dict = await GetCardsByUUIDsAsync(allIds);
        return SortCardsBySide([.. dict.Values]);
    }

    public async Task<Card[]> GetFullCardPackageAsync(string uuid)
    {
        var mainCard = await GetCardWithRulingsAsync(uuid);
        if (string.IsNullOrEmpty(mainCard.UUID)) return [];

        var cards = new List<Card> { mainCard };

        // CASE A: Meld cards (linked by name in CardParts JSON)
        if (mainCard.Layout == CardLayout.Meld && !string.IsNullOrEmpty(mainCard.CardParts))
        {
            var cardParts = CardMapper.ParseJsonArrayToStrings(mainCard.CardParts);
            if (cardParts.Length > 0)
                cards.AddRange(await GetMeldPartCardsAsync(cardParts, mainCard.SetCode, mainCard.UUID));
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

        // Include tokens/related cards
        if (mainCard.RelatedCards != null && mainCard.RelatedCards.Length > 0)
        {
            var dict = await GetCardsByUUIDsAsync(mainCard.RelatedCards);
            foreach(var kvp in dict)
            {
                // Ensure we don't duplicate (e.g. if a related card was already included)
                if (!cards.Any(c => c.UUID == kvp.Key))
                {
                    cards.Add(kvp.Value);
                }
            }
        }

        return SortCardsBySide([.. cards]);
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
            var sql = SQLQueries.BaseCardsAndTokens + " WHERE c.uuid IN (" + string.Join(",", paramNames) + ")";

            var dynamicParams = new DynamicParameters();
            for (int j = 0; j < chunk.Length; j++)
            {
                dynamicParams.Add(paramNames[j], chunk[j]);
            }

            await WithMTGReaderAsync(sql,
                dynamicParams,
                async reader =>
                {
                    var o = new CardMapper.CardOrdinals(reader);
                    while (await reader.ReadAsync())
                    {
                        var card = CardMapper.MapCard(reader, o);
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

        var dynamicParams = new DynamicParameters();
        foreach (var (name, value) in parameters)
        {
            dynamicParams.Add(name, value);
        }

        await WithMTGReaderAsync(sql,
            dynamicParams,
            async reader =>
            {
                var o = new CardMapper.CardOrdinals(reader);
                while (await reader.ReadAsync())
                    cards.Add(CardMapper.MapCard(reader, o));
                return 0;
            });

        return [.. cards];
    }

    public async Task<int> GetCountAdvancedAsync(MTGSearchHelper searchHelper)
    {
        var (sql, parameters) = searchHelper.BuildCount();

        await _lock.WaitAsync();
        try
        {
            var dynamicParams = new DynamicParameters();
            foreach (var (name, value) in parameters)
            {
                dynamicParams.Add(name, value);
            }

            return await _db.MTGConnection.ExecuteScalarAsync<int>(sql, dynamicParams);
        }
        finally
        {
            _lock.Release();
        }
    }

    public MTGSearchHelper CreateSearchHelper() => new();

    public async Task<IReadOnlyList<SetInfo>> GetAllSetsAsync()
    {
        await _lock.WaitAsync();
        try
        {
            var list = await _db.MTGConnection.QueryAsync<SetInfo>(SQLQueries.SelectSetsForFilter);
            return [.. list];
        }
        finally
        {
            _lock.Release();
        }
    }

    // ── Private helpers ─────────────────────────────────────────────

    private async Task<Card[]> GetMeldPartCardsAsync(string[] cardParts, string setCode, string mainUUID)
    {
        var cards = new List<Card>();
        var dynamicParams = new DynamicParameters();
        dynamicParams.Add("@set", setCode);
        dynamicParams.Add("@mainUUID", mainUUID);

        var conditions = new List<string>();
        for (int i = 0; i < cardParts.Length; i++)
        {
            dynamicParams.Add($"@n{i}", cardParts[i].Trim());
            conditions.Add($"(c.faceName = @n{i} OR c.name = @n{i})");
        }

        var sql = SQLQueries.SelectFullCard +
            $" WHERE c.setCode = @set AND ({string.Join(" OR ", conditions)}) AND c.uuid <> @mainUUID";

        await WithMTGReaderAsync(sql,
            dynamicParams,
            async reader =>
            {
                var o = new CardMapper.CardOrdinals(reader);
                while (await reader.ReadAsync())
                    cards.Add(CardMapper.MapCard(reader, o));
                return 0;
            });

        return [.. cards];
    }

    private static Card[] SortCardsBySide(Card[] cards) =>
        [.. cards.OrderBy(c => c.Side)];

    /// <summary>
    /// Executes a query against the MTG database and processes results with an async reader.
    /// Uses SemaphoreSlim for non-blocking thread safety.
    /// Uses Dapper ExecuteReaderAsync underneath.
    /// </summary>
    private async Task<T> WithMTGReaderAsync<T>(
        string sql,
        object? param,
        Func<DbDataReader, Task<T>> readFunc)
    {
        await _lock.WaitAsync();
        try
        {
            using var reader = await _db.MTGConnection.ExecuteReaderAsync(sql, param) as DbDataReader
                ?? throw new InvalidOperationException("Failed to create DbDataReader.");
            return await readFunc(reader);
        }
        finally
        {
            _lock.Release();
        }
    }
}