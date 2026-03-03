using AetherVault.Controls;
using AetherVault.Data;
using AetherVault.Models;
using AetherVault.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AetherVault.ViewModels;

/// <summary>
/// ViewModel for the Binder detail page.
/// Displays cards tagged to a specific binder using the shared CardGrid control.
/// </summary>
[QueryProperty(nameof(BinderIdString), "binderId")]
[QueryProperty(nameof(BinderName), "binderName")]
public partial class BinderDetailViewModel : BaseViewModel
{
    private readonly IBinderRepository _binderRepository;
    private readonly CardManager _cardManager;
    private readonly CardGalleryContext _galleryContext;

    private int _binderId;
    private CardGrid? _grid;
    private CollectionItem[] _allItems = [];

    [ObservableProperty]
    public partial string BinderName { get; set; } = "";

    [ObservableProperty]
    public partial bool IsEmpty { get; set; } = true;

    public string BinderIdString
    {
        set
        {
            if (int.TryParse(value, out var id))
                _binderId = id;
        }
    }

    public BinderDetailViewModel(
        IBinderRepository binderRepository,
        CardManager cardManager,
        CardGalleryContext galleryContext)
    {
        _binderRepository = binderRepository;
        _cardManager = cardManager;
        _galleryContext = galleryContext;
    }

    public void AttachGrid(CardGrid grid)
    {
        _grid = grid;
    }

    protected override void OnViewModeUpdated(ViewMode value)
    {
        if (_grid != null) _grid.ViewMode = value;
    }

    public async Task LoadBinderAsync()
    {
        if (IsBusy || _binderId == 0) return;

        if (!await _cardManager.EnsureInitializedAsync())
        {
            StatusMessage = "Database not connected.";
            return;
        }

        IsBusy = true;
        StatusIsError = false;
        StatusMessage = "Loading binder...";

        try
        {
            _allItems = await _binderRepository.GetBinderCardsAsync(_binderId);
            _grid?.SetCollection(_allItems);
            IsEmpty = _allItems.Length == 0;

            var total = _allItems.Sum(i => i.Quantity);
            StatusMessage = _allItems.Length == 0
                ? ""
                : $"{total} cards ({_allItems.Length} unique)";
        }
        catch (Exception ex)
        {
            StatusIsError = true;
            StatusMessage = $"Load failed: {ex.Message}";
            Logger.LogStuff($"Binder load error: {ex.Message}", LogLevel.Error);
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task<Card?> GetCardDetailsAsync(string uuid)
    {
        try { return await _cardManager.GetCardDetailsAsync(uuid); }
        catch { return null; }
    }

    public void SetGalleryContext(IEnumerable<string> uuids, string currentUuid)
    {
        _galleryContext.SetContext(uuids, currentUuid);
    }

    public async Task RemoveCardFromBinderAsync(string cardUuid)
    {
        try
        {
            await _binderRepository.RemoveCardFromBinderAsync(_binderId, cardUuid);
            await LoadBinderAsync();
        }
        catch (Exception ex)
        {
            Logger.LogStuff($"Failed to remove card from binder: {ex.Message}", LogLevel.Error);
        }
    }

    public async Task DeleteSelfAsync()
    {
        try
        {
            await _binderRepository.DeleteBinderAsync(_binderId);
        }
        catch (Exception ex)
        {
            Logger.LogStuff($"Failed to delete binder: {ex.Message}", LogLevel.Error);
        }
    }
}
