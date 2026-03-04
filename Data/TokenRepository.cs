using AetherVault.Models;
using Dapper;

namespace AetherVault.Data;

public class TokenRepository : ITokenRepository
{
    private readonly DatabaseManager _db;
    private readonly SemaphoreSlim _lock = new(1, 1);

    public TokenRepository(DatabaseManager databaseManager)
    {
        _db = databaseManager;
    }

    public async Task<TokenEntity?> GetTokenByUuidAsync(string uuid)
    {
        await _lock.WaitAsync();
        try
        {
            return await _db.MTGConnection.QueryFirstOrDefaultAsync<TokenEntity>(
                SQLQueries.SelectTokenByUuid, new { uuid });
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<TokenIdentifierEntity?> GetTokenIdentifierByUuidAsync(string uuid)
    {
        await _lock.WaitAsync();
        try
        {
            return await _db.MTGConnection.QueryFirstOrDefaultAsync<TokenIdentifierEntity>(
                SQLQueries.SelectTokenIdentifierByUuid, new { uuid });
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<IEnumerable<TokenEntity>> GetTokensBySetCodeAsync(string setCode)
    {
        await _lock.WaitAsync();
        try
        {
            return await _db.MTGConnection.QueryAsync<TokenEntity>(
                SQLQueries.SelectTokensBySetCode, new { setCode });
        }
        finally
        {
            _lock.Release();
        }
    }
}
