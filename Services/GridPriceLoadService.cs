using AetherVault.Controls;

namespace AetherVault.Services;

/// <summary>
/// Loads price data for the visible card range and updates the grid in bulk.
/// </summary>
public sealed class GridPriceLoadService : IGridPriceLoadService
{
    private readonly CardManager _cardManager;

    public GridPriceLoadService(CardManager cardManager)
    {
        _cardManager = cardManager;
    }

    /// <inheritdoc />
    public void LoadVisiblePrices(CardGrid? grid, int start, int end)
    {
        if (grid == null) return;

        _ = Task.Run(async () =>
        {
            var uuids = new HashSet<string>();
            for (int i = start; i <= end; i++)
            {
                var card = grid.GetCardStateAt(i);
                if (card == null || card.PriceData != null) continue;
                uuids.Add(card.Id.Value);
            }

            if (uuids.Count == 0) return;

            var pricesMap = await _cardManager.GetCardPricesBulkAsync(uuids);
            if (pricesMap.Count > 0)
            {
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    grid.UpdateCardPricesBulk(pricesMap);
                });
            }
        });
    }
}
