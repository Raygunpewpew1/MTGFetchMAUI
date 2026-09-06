using AetherVault.Core;
using AetherVault.Models;
using Dapper;
using System.Data.Common;

namespace AetherVault.Data;

/// <summary>
/// Read-only access to the MTG card database (cards + tokens). All queries go through the MTG connection.
/// Uses Dapper for execution and CardMapper to turn rows into Card objects. Never write to this DB from here.
/// </summary>
public class CardRepository : ICardRepository
{
    private readonly DatabaseManager _db;

    public CardRepository(DatabaseManager databaseManager)
    {
        _db = databaseManager;
    }

    public Task<Card> GetCardByUuidAsync(string uuid) => GetCardWithLegalitiesAsync(uuid);

    /// <summary>Loads a single card by UUID from cards/tokens and maps it to a Card model.</summary>
    public async Task<Card> GetCardWithLegalitiesAsync(string uuid)
    {
        return await WithMtgReaderAsync(
            SqlQueries.BaseCardsAndTokens + SqlQueries.WhereUuidEquals,
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
            var otherFaces = await GetOtherFacesAsync(uuid);
            if (otherFaces.Length > 1)
                card.Text = otherFaces[0].Text + Environment.NewLine + "---" + Environment.NewLine + otherFaces[1].Text;
        }

        return card;
    }

    public async Task<Card> GetCardWithRulingsAsync(string uuid)
    {
        var card = await GetCardWithLegalitiesAsync(uuid);
        if (!string.IsNullOrEmpty(card.Uuid))
            card.Rulings = (await GetCardRulingsAsync(uuid)).ToList();
        return card;
    }

    public async Task<Card> GetCardByFaceAndSetAsync(string faceName, string setCode)
    {
        return await WithMtgReaderAsync(
            SqlQueries.SelectFullCard + " WHERE c.faceName = @fname AND c.setCode = @set LIMIT 1",
            new { fname = faceName, set = setCode },
            async reader =>
            {
                var o = new CardMapper.CardOrdinals(reader);
                return await reader.ReadAsync() ? CardMapper.MapCard(reader, o) : new Card();
            });
    }

    public async Task<Card?> GetCardByNameAndSetAsync(string name, string setCode)
    {
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(setCode))
            return null;
        var card = await WithMtgReaderAsync(
            SqlQueries.BaseCardsAndTokens + SqlQueries.WhereNameAndSet,
            new { name = name.Trim(), set = setCode.Trim() },
            async reader =>
            {
                var o = new CardMapper.CardOrdinals(reader);
                return await reader.ReadAsync() ? CardMapper.MapCard(reader, o) : null;
            });
        return card?.Uuid != null ? card : null;
    }

    public async Task<Card?> GetCardByScryfallIdAsync(string scryfallId)
    {
        if (string.IsNullOrWhiteSpace(scryfallId))
            return null;
        var card = await WithMtgReaderAsync(
            SqlQueries.BaseCardsAndTokens + SqlQueries.WhereScryfallId,
            new { sid = scryfallId.Trim() },
            async reader =>
            {
                var o = new CardMapper.CardOrdinals(reader);
                return await reader.ReadAsync() ? CardMapper.MapCard(reader, o) : null;
            });
        return card?.Uuid != null ? card : null;
    }

    public async Task<string> GetScryfallIdAsync(string cardUuid) =>
        await WithMtgConnectionAsync(async () =>
        {
            var result = await _db.MtgConnection.QueryFirstOrDefaultAsync<string>(
                SqlQueries.SelectScryfallId, new { uuid = cardUuid });
            return result ?? "";
        });

    private class CardRulingRow
    {
        public string Date { get; set; } = "";
        public string Text { get; set; } = "";
    }

    public async Task<CardRuling[]> GetCardRulingsAsync(string uuid) =>
        await WithMtgConnectionAsync(async () =>
        {
            var rows = await _db.MtgConnection.QueryAsync<CardRulingRow>(
                SqlQueries.SelectRulings, new { uuid });

            var rulings = new List<CardRuling>();
            foreach (var row in rows)
            {
                DateTime.TryParse(row.Date, out var date);
                rulings.Add(new CardRuling(date, row.Text));
            }

            return (CardRuling[])[.. rulings];
        });

    public async Task<string[]> GetOtherFaceIdsAsync(string uuid) =>
        await WithMtgConnectionAsync(async () =>
        {
            var raw = await _db.MtgConnection.QueryFirstOrDefaultAsync<string>(
                SqlQueries.SelectOtherFaces, new { uuid });
            return CardMapper.ParseOtherFaceIds(raw ?? "");
        });

    public async Task<Card[]> GetOtherFacesAsync(string uuid)
    {
        var otherIds = await GetOtherFaceIdsAsync(uuid);
        var allIds = new[] { uuid }.Concat(otherIds).ToArray();
        var dict = await GetCardsAsync(allIds);
        return SortCardsBySide([.. dict.Values]);
    }

    public async Task<Card[]> GetFullCardPackageAsync(string uuid)
    {
        var mainCard = await GetCardWithRulingsAsync(uuid);
        if (string.IsNullOrEmpty(mainCard.Uuid)) return [];

        var cards = new List<Card> { mainCard };

        // CASE A: Meld cards (linked by name in CardParts JSON)
        if (mainCard.Layout == CardLayout.Meld && !string.IsNullOrEmpty(mainCard.CardParts))
        {
            var cardParts = CardMapper.ParseJsonArrayToStrings(mainCard.CardParts);
            if (cardParts.Length > 0)
                cards.AddRange(await GetMeldPartCardsAsync(cardParts, mainCard.SetCode, mainCard.Uuid));
        }
        // CASE B: Standard multi-face (Transform, Adventure, Split, etc.)
        else
        {
            var otherIds = CardMapper.ParseOtherFaceIds(mainCard.OtherFaceIds);
            if (otherIds.Length > 0)
            {
                var dict = await GetCardsAsync(otherIds);
                cards.AddRange(dict.Values);
            }
        }

        // Include tokens/related cards
        if (mainCard.RelatedCards != null && mainCard.RelatedCards.Length > 0)
        {
            var dict = await GetCardsAsync(mainCard.RelatedCards);
            foreach (var kvp in dict)
            {
                // Ensure we don't duplicate (e.g. if a related card was already included)
                if (!cards.Any(c => c.Uuid == kvp.Key))
                {
                    cards.Add(kvp.Value);
                }
            }
        }

        return SortCardsBySide([.. cards]);
    }

    public async Task<Dictionary<string, Card>> GetCardsAsync(string[] uuids)
    {
        var result = new Dictionary<string, Card>(StringComparer.OrdinalIgnoreCase);
        if (uuids.Length == 0) return result;

        const int fullChunk = 500;
        for (int i = 0; i < uuids.Length; i += fullChunk)
        {
            var chunk = uuids.Skip(i).Take(fullChunk).ToArray();
            var paramNames = chunk.Select((_, idx) => $"@u{idx}").ToArray();
            var sql = SqlQueries.BaseCardsAndTokens + " WHERE c.uuid IN (" + string.Join(",", paramNames) + ")";

            var dynamicParams = new DynamicParameters();
            for (int j = 0; j < chunk.Length; j++)
            {
                dynamicParams.Add(paramNames[j], chunk[j]);
            }

            await WithMtgReaderAsync(sql,
                dynamicParams,
                async reader =>
                {
                    var o = new CardMapper.CardOrdinals(reader);
                    while (await reader.ReadAsync())
                    {
                        var card = CardMapper.MapCard(reader, o);
                        result[card.Uuid] = card;
                    }
                    return 0;
                });
        }

        return result;
    }

    public async Task<IReadOnlyList<ImportLookupRow>> GetImportLookupRowsAsync() =>
        await WithMtgConnectionAsync(async () =>
        {
            var rows = await _db.MtgConnection.QueryAsync<ImportLookupRow>(SqlQueries.SelectImportLookupRows);
            return (IReadOnlyList<ImportLookupRow>)[.. rows];
        });

    public async Task<Card[]> SearchCardsAsync(string searchText, int limit = 100)
    {
        var helper = CreateSearchHelper();
        helper.SearchCards()
            .WhereNameContains(searchText)
            .WherePrimarySideOnly()
            .OrderBy("c.name")
            .Limit(limit);
        return await SearchAdvancedAsync(helper);
    }

    public async Task<Card[]> SearchAdvancedAsync(MtgSearchHelper searchHelper)
    {
        var cards = new List<Card>();
        var (sql, parameters) = searchHelper.Build();

        var dynamicParams = new DynamicParameters();
        foreach (var (name, value) in parameters)
        {
            dynamicParams.Add(name, value);
        }

        await WithMtgReaderAsync(sql,
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

    public async Task<(Card[] cards, int totalCount)> SearchAdvancedWithResultTotalAsync(MtgSearchHelper searchHelper)
    {
        var cards = new List<Card>();
        var (sql, parameters) = searchHelper.BuildWithResultTotal();

        var dynamicParams = new DynamicParameters();
        foreach (var (name, value) in parameters)
        {
            dynamicParams.Add(name, value);
        }

        return await WithMtgReaderAsync<(Card[] cards, int totalCount)>(
            sql,
            dynamicParams,
            async reader =>
            {
                var o = new CardMapper.CardOrdinals(reader);
                int totalIdx = -1;
                for (int col = 0; col < reader.FieldCount; col++)
                {
                    if (string.Equals(reader.GetName(col), SqlQueries.ResultTotalCountColumnName, StringComparison.OrdinalIgnoreCase))
                    {
                        totalIdx = col;
                        break;
                    }
                }

                int totalCount = 0;
                bool gotTotal = false;
                while (await reader.ReadAsync())
                {
                    if (!gotTotal && totalIdx >= 0)
                    {
                        totalCount = Convert.ToInt32(reader.GetInt64(totalIdx));
                        gotTotal = true;
                    }

                    cards.Add(CardMapper.MapCard(reader, o));
                }

                if (!gotTotal)
                    totalCount = cards.Count;

                return (cards: [.. cards], totalCount: totalCount);
            });
    }

    public async Task<int> CountAdvancedAsync(MtgSearchHelper searchHelper)
    {
        var (sql, parameters) = searchHelper.BuildCount();
        return await WithMtgConnectionAsync(async () =>
        {
            var dynamicParams = new DynamicParameters();
            foreach (var (name, value) in parameters)
                dynamicParams.Add(name, value);

            return await _db.MtgConnection.ExecuteScalarAsync<int>(sql, dynamicParams);
        });
    }

    public MtgSearchHelper CreateSearchHelper() => new();

    public async Task<IReadOnlyList<SetInfo>> GetAllSetsAsync() =>
        await WithMtgConnectionAsync(async () =>
        {
            var list = await _db.MtgConnection.QueryAsync<SetInfo>(SqlQueries.SelectSetsForFilter);
            return (IReadOnlyList<SetInfo>)[.. list];
        });

    public async Task<IReadOnlyList<SetBrowseRow>> GetSetsBrowseAsync() =>
        await WithMtgConnectionAsync(async () =>
        {
            var list = await _db.MtgConnection.QueryAsync<SetBrowseRow>(SqlQueries.SelectSetsBrowse);
            return (IReadOnlyList<SetBrowseRow>)[.. list];
        });

    public async Task<bool> HasFtsAsync()
    {
        try
        {
            return await WithMtgConnectionAsync(async () =>
            {
                var valFull = await _db.MtgConnection.QueryFirstOrDefaultAsync<int>(SqlQueries.FtsExistsCheck);
                return valFull == 1;
            });
        }
        catch
        {
            return false;
        }
    }

    public async Task<IReadOnlyList<OtherPrintingSummary>> GetOtherPrintingsByOracleIdAsync(string oracleId, string currentUuid)
    {
        if (string.IsNullOrWhiteSpace(oracleId))
            return [];

        return await WithMtgConnectionAsync(async () =>
        {
            var rows = await _db.MtgConnection.QueryAsync<OtherPrintingDbRow>(
                SqlQueries.SelectOtherPrintingsByOracleId,
                new { OracleId = oracleId.Trim() });

            var list = new List<OtherPrintingSummary>();
            foreach (var row in rows)
            {
                if (string.Equals(row.Uuid, currentUuid, StringComparison.OrdinalIgnoreCase))
                    continue;

                list.Add(new OtherPrintingSummary
                {
                    Uuid = row.Uuid ?? "",
                    SetCode = row.SetCode ?? "",
                    SetName = row.SetName ?? "",
                    Number = row.Number ?? "",
                    Rarity = EnumExtensions.ParseCardRarity(row.Rarity),
                    SetReleaseDate = row.SetReleaseDate ?? "",
                    IsCurrent = false
                });
            }

            return (IReadOnlyList<OtherPrintingSummary>)list;
        });
    }

    private sealed class OtherPrintingDbRow
    {
        public string? Uuid { get; set; }
        public string? SetCode { get; set; }
        public string? SetName { get; set; }
        public string? Number { get; set; }
        public string? Rarity { get; set; }
        public string? SetReleaseDate { get; set; }
    }

    // ── Private helpers ─────────────────────────────────────────────

    private async Task<Card[]> GetMeldPartCardsAsync(string[] cardParts, string setCode, string mainUuid)
    {
        var cards = new List<Card>();
        var dynamicParams = new DynamicParameters();
        dynamicParams.Add("@set", setCode);
        dynamicParams.Add("@mainUUID", mainUuid);

        var conditions = new List<string>();
        for (int i = 0; i < cardParts.Length; i++)
        {
            dynamicParams.Add($"@n{i}", cardParts[i].Trim());
            conditions.Add($"(c.faceName = @n{i} OR c.name = @n{i})");
        }

        var sql = SqlQueries.SelectFullCard +
            $" WHERE c.setCode = @set AND ({string.Join(" OR ", conditions)}) AND c.uuid <> @mainUUID";

        await WithMtgReaderAsync(sql,
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
    private async Task<T> WithMtgReaderAsync<T>(
        string sql,
        object? param,
        Func<DbDataReader, Task<T>> readFunc) =>
        await WithMtgConnectionAsync(async () =>
        {
            using var reader = await _db.MtgConnection.ExecuteReaderAsync(sql, param) as DbDataReader
                ?? throw new InvalidOperationException("Failed to create DbDataReader.");
            return await readFunc(reader);
        });

    /// <summary>
    /// Serializes MTG reads with <see cref="DatabaseManager.ConnectionLock"/> so
    /// <see cref="DatabaseManager.DisconnectAsync"/> cannot dispose connections mid-query.
    /// </summary>
    private async Task<T> WithMtgConnectionAsync<T>(Func<Task<T>> action) =>
        await _db.WithConnectedAsync(action);
}