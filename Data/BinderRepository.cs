using AetherVault.Models;
using Dapper;

namespace AetherVault.Data;

/// <summary>
/// CRUD operations for the Binders and BinderCards tables.
/// </summary>
public class BinderRepository : IBinderRepository
{
    private class BinderRow
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public string Description { get; set; } = "";
        public string DateCreated { get; set; } = "";
        public string DateModified { get; set; } = "";
        public int CardCount { get; set; }
    }

    private class BinderCardRow
    {
        public string card_uuid { get; set; } = "";
        public int quantity { get; set; }
        public string date_added { get; set; } = "";
        public int? sort_order { get; set; }
        public int? is_foil { get; set; }
        public int? is_etched { get; set; }
    }

    private readonly DatabaseManager _db;
    private readonly ICardRepository _cardRepo;
    private readonly SemaphoreSlim _lock = new(1, 1);

    public BinderRepository(DatabaseManager databaseManager, ICardRepository cardRepository)
    {
        _db = databaseManager;
        _cardRepo = cardRepository;
    }

    public async Task<BinderEntity[]> GetAllBindersAsync()
    {
        await _lock.WaitAsync();
        try
        {
            var rows = await _db.CollectionConnection.QueryAsync<BinderRow>(SQLQueries.BinderGetAll);
            return rows.Select(r => new BinderEntity
            {
                Id = r.Id,
                Name = r.Name,
                Description = r.Description,
                DateCreated = DateTime.TryParse(r.DateCreated, out var dc) ? dc : DateTime.Now,
                DateModified = DateTime.TryParse(r.DateModified, out var dm) ? dm : DateTime.Now,
                CardCount = r.CardCount
            }).ToArray();
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<int> CreateBinderAsync(string name, string description = "")
    {
        await _lock.WaitAsync();
        try
        {
            await _db.CollectionConnection.ExecuteAsync(
                SQLQueries.BinderInsert, new { Name = name, Description = description });
            return await _db.CollectionConnection.QueryFirstAsync<int>(SQLQueries.DeckGetLastId);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task DeleteBinderAsync(int binderId)
    {
        await _lock.WaitAsync();
        try
        {
            await _db.CollectionConnection.ExecuteAsync(
                SQLQueries.BinderDelete, new { Id = binderId });
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task RenameBinderAsync(int binderId, string newName)
    {
        await _lock.WaitAsync();
        try
        {
            await _db.CollectionConnection.ExecuteAsync(
                SQLQueries.BinderRename, new { Id = binderId, Name = newName });
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task AddCardToBinderAsync(int binderId, string cardUuid)
    {
        await _lock.WaitAsync();
        try
        {
            await _db.CollectionConnection.ExecuteAsync(
                SQLQueries.BinderCardAdd, new { BinderId = binderId, CardUUID = cardUuid });
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task RemoveCardFromBinderAsync(int binderId, string cardUuid)
    {
        await _lock.WaitAsync();
        try
        {
            await _db.CollectionConnection.ExecuteAsync(
                SQLQueries.BinderCardRemove, new { BinderId = binderId, CardUUID = cardUuid });
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<CollectionItem[]> GetBinderCardsAsync(int binderId)
    {
        IEnumerable<BinderCardRow> rows;
        await _lock.WaitAsync();
        try
        {
            rows = await _db.CollectionConnection.QueryAsync<BinderCardRow>(
                SQLQueries.BinderCardGetAll, new { BinderId = binderId });
        }
        finally
        {
            _lock.Release();
        }

        var rowList = rows.ToList();
        if (rowList.Count == 0) return [];

        var uuids = rowList.Select(r => r.card_uuid).ToArray();
        var cardCache = await _cardRepo.GetCardsByUUIDsAsync(uuids);

        var items = new List<CollectionItem>();
        foreach (var row in rowList)
        {
            if (cardCache.TryGetValue(row.card_uuid, out var card))
            {
                DateTime dateAdded = DateTime.TryParse(row.date_added, out var d) ? d : DateTime.Now;
                items.Add(new CollectionItem
                {
                    CardUUID = row.card_uuid,
                    Quantity = row.quantity,
                    IsFoil = row.is_foil.HasValue && row.is_foil.Value != 0,
                    IsEtched = row.is_etched.HasValue && row.is_etched.Value != 0,
                    DateAdded = dateAdded,
                    SortOrder = row.sort_order ?? 0,
                    Card = card
                });
            }
        }
        return [.. items];
    }

    public async Task<int[]> GetCardBinderIdsAsync(string cardUuid)
    {
        await _lock.WaitAsync();
        try
        {
            var ids = await _db.CollectionConnection.QueryAsync<int>(
                SQLQueries.BinderCardGetBinderIds, new { CardUUID = cardUuid });
            return ids.ToArray();
        }
        finally
        {
            _lock.Release();
        }
    }
}
