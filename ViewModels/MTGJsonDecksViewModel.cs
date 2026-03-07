using System.Collections.ObjectModel;
using AetherVault.Models;
using AetherVault.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AetherVault.ViewModels;

public partial class MTGJsonDecksViewModel : BaseViewModel
{
    private readonly MTGJsonDeckListService _deckListService;
    private readonly MTGJsonDeckImporter _importer;
    private readonly IToastService _toast;

    [ObservableProperty]
    private ObservableCollection<MtgJsonDeckListEntry> _decks = [];

    [ObservableProperty]
    private ObservableCollection<MtgJsonDeckListEntry> _filteredDecks = [];

    [ObservableProperty]
    private string _searchText = "";

    [ObservableProperty]
    private string _selectedDeckType = "All";

    [ObservableProperty]
    private List<string> _availableDeckTypes = ["All"];

    [ObservableProperty]
    private MtgJsonDeckListEntry? _selectedDeck;

    private List<MtgJsonDeckListEntry> _allDecks = [];

    public MTGJsonDecksViewModel(MTGJsonDeckListService deckListService, MTGJsonDeckImporter importer, IToastService toast)
    {
        _deckListService = deckListService;
        _importer = importer;
        _toast = toast;
    }

    [RelayCommand]
    public async Task RefreshAsync()
    {
        await LoadDeckListAsync(true);
    }

    [RelayCommand]
    public async Task LoadDeckListAsync(bool forceRefresh = false)
    {
        if (IsBusy) return;
        IsBusy = true;
        StatusIsError = false;
        StatusMessage = UserMessages.LoadingDeckList;

        try
        {
            var list = await _deckListService.GetDeckListAsync(forceRefresh);
            _allDecks = [.. list];
            var types = _allDecks.Select(d => d.Type ?? "").Where(t => !string.IsNullOrWhiteSpace(t)).Distinct().OrderBy(t => t).ToList();
            MainThread.BeginInvokeOnMainThread(() =>
            {
                AvailableDeckTypes = ["All", .. types];
                if (!AvailableDeckTypes.Contains(SelectedDeckType))
                    SelectedDeckType = "All";
                ApplyFilter();
                Decks = new ObservableCollection<MtgJsonDeckListEntry>(_allDecks);
                StatusMessage = _allDecks.Count == 0 ? "No decks in catalog." : $"{(uint)_allDecks.Count} decks";
            });
        }
        catch (Exception ex)
        {
            StatusIsError = true;
            StatusMessage = UserMessages.LoadFailed(ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void ApplyFilter()
    {
        IEnumerable<MtgJsonDeckListEntry> source = _allDecks;
        if (SelectedDeckType != "All")
            source = source.Where(d => string.Equals(d.Type, SelectedDeckType, StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(SearchText))
        {
            var q = SearchText.Trim();
            source = source.Where(d =>
                d.Name.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                (d.Type?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false) ||
                (d.Code?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false));
        }
        FilteredDecks = new ObservableCollection<MtgJsonDeckListEntry>(source.ToList());
    }

    partial void OnSearchTextChanged(string value) => ApplyFilter();
    partial void OnSelectedDeckTypeChanged(string value) => ApplyFilter();

    [RelayCommand]
    public async Task ImportDeckAsync(MtgJsonDeckListEntry? entry)
    {
        var deckEntry = entry ?? SelectedDeck;
        if (deckEntry == null)
        {
            _toast?.Show(UserMessages.PleaseSelectDeck);
            return;
        }
        if (IsBusy) return;
        IsBusy = true;
        StatusIsError = false;
        StatusMessage = UserMessages.ImportingMTGJsonDeck;

        try
        {
            var deck = await _deckListService.GetDeckAsync(deckEntry.FileName);
            if (deck == null)
            {
                StatusIsError = true;
                StatusMessage = UserMessages.MTGJsonDeckImportFailed;
                _toast?.Show(UserMessages.MTGJsonDeckImportFailed);
                return;
            }

            var progress = new Progress<string>(msg => MainThread.BeginInvokeOnMainThread(() => StatusMessage = msg));
            var result = await _importer.ImportDeckAsync(deck, progress);

            if (!result.Success)
            {
                StatusIsError = true;
                StatusMessage = UserMessages.MTGJsonDeckImportFailed;
                _toast?.Show(UserMessages.MTGJsonDeckImportFailed);
                return;
            }

            StatusMessage = UserMessages.StatusClear;
            _toast?.Show(UserMessages.MTGJsonDeckImportedToast(deck.Name, result.CardsAdded));
            if (result.MissingUuids.Count > 0)
                Logger.LogStuff($"MTGJSON import: {result.MissingUuids.Count} UUIDs not in local DB.", LogLevel.Warning);

            await Shell.Current.GoToAsync($"deckdetail?deckId={result.DeckId}");
        }
        catch (Exception ex)
        {
            Logger.LogStuff($"MTGJSON deck import failed: {ex.Message}", LogLevel.Error);
            StatusIsError = true;
            StatusMessage = UserMessages.ImportFailed(ex.Message);
            _toast?.Show(UserMessages.MTGJsonDeckImportFailed);
        }
        finally
        {
            IsBusy = false;
        }
    }
}
