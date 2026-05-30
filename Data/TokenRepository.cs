using AetherVault.Models;
using Dapper;

namespace AetherVault.Data;

public class TokenRepository : ITokenRepository
{
    private readonly DatabaseManager _db;

    public TokenRepository(DatabaseManager databaseManager)
    {
        _db = databaseManager;
    }

    public async Task<TokenEntity?> GetTokenByUuidAsync(string uuid) =>
        await WithMtgConnectionAsync(async () =>
            await _db.MtgConnection.QueryFirstOrDefaultAsync<TokenEntity>(
                SqlQueries.SelectTokenByUuid, new { uuid }));

    public async Task<TokenIdentifierEntity?> GetTokenIdentifierAsync(string uuid) =>
        await WithMtgConnectionAsync(async () =>
            await _db.MtgConnection.QueryFirstOrDefaultAsync<TokenIdentifierEntity>(
                SqlQueries.SelectTokenIdentifierByUuid, new { uuid }));

    public async Task<IEnumerable<TokenEntity>> GetTokensBySetCodeAsync(string setCode) =>
        await WithMtgConnectionAsync(async () =>
            await _db.MtgConnection.QueryAsync<TokenEntity>(
                SqlQueries.SelectTokensBySetCode, new { setCode }));

    private async Task<T> WithMtgConnectionAsync<T>(Func<Task<T>> action)
    {
        await _db.ConnectionLock.WaitAsync();
        try
        {
            if (!_db.IsConnected)
                throw new InvalidOperationException("Database not connected.");
            return await action();
        }
        finally
        {
            _db.ConnectionLock.Release();
        }
    }
}
