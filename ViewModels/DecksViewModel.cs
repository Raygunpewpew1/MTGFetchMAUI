using AetherVault.Core;
using AetherVault.Models;
using AetherVault.Services;
using AetherVault.Services.DeckBuilder;
using AetherVault.Services.ImportExport;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using System.Text;

namespace AetherVault.ViewModels;

public partial class DecksViewModel : BaseViewModel
{
    private readonly DeckBuilderService _deckService;
    private readonly DeckImporter _deckImporter;
    private readonly DeckUrlImporter _deckUrlImporter;
    private readonly DeckExporter _deckExporter;
    private bool _deckHubLayoutPrefLoaded;

    private const string PrefDeckHubLayoutMode = DeckHubLayoutDefaults.PreferenceKey;

    [ObservableProperty]
    public partial ObservableCollection<DeckEntity> Decks { get; set; } = [];

    [ObservableProperty]
    public partial bool IsEmpty { get; set; }

    [ObservableProperty]
    public partial DeckHubLayoutMode DeckHubLayoutMode { get; set; }

    public bool IsDeckHubLayoutList => DeckHubLayoutMode == DeckHubLayoutMode.List;
    public bool IsDeckHubLayoutTiles => DeckHubLayoutMode == DeckHubLayoutMode.Tiles;
    public bool IsDeckHubListVisible => !IsEmpty && IsDeckHubLayoutList;
    public bool IsDeckHubTilesVisible => !IsEmpty && IsDeckHubLayoutTiles;

    public DecksViewModel(
        DeckBuilderService deckService,
        DeckImporter deckImporter,
        DeckUrlImporter deckUrlImporter,
        DeckExporter deckExporter)
    {
        _deckService = deckService;
        _deckImporter = deckImporter;
        _deckUrlImporter = deckUrlImporter;
        _deckExporter = deckExporter;
        EnsureDeckHubLayoutPreferenceLoaded();
    }

    partial void OnDeckHubLayoutModeChanged(DeckHubLayoutMode value)
    {
        OnPropertyChanged(nameof(IsDeckHubLayoutList));
        OnPropertyChanged(nameof(IsDeckHubLayoutTiles));
        OnPropertyChanged(nameof(IsDeckHubListVisible));
        OnPropertyChanged(nameof(IsDeckHubTilesVisible));
        if (!_deckHubLayoutPrefLoaded)
            return;

        try
        {
            Preferences.Default.Set(PrefDeckHubLayoutMode, value.ToString());
        }
        catch
        {
            // Preference write is best-effort.
        }
    }

    private void EnsureDeckHubLayoutPreferenceLoaded()
    {
        if (_deckHubLayoutPrefLoaded)
            return;

        _deckHubLayoutPrefLoaded = true;
        try
        {
            var defaultMode = DeckHubLayoutDefaults.GetDefaultForDevice(DeviceInfo.Idiom);
            string stored = Preferences.Default.Get(PrefDeckHubLayoutMode, defaultMode.ToString());
            if (Enum.TryParse(stored, out DeckHubLayoutMode layoutMode))
                DeckHubLayoutMode = layoutMode;
            else
                DeckHubLayoutMode = defaultMode;
        }
        catch
        {
            DeckHubLayoutMode = DeckHubLayoutDefaults.GetDefaultForDevice(DeviceInfo.Idiom);
        }
    }

    [RelayCommand]
    private void SelectDeckHubLayoutList() => DeckHubLayoutMode = DeckHubLayoutMode.List;

    [RelayCommand]
    private void SelectDeckHubLayoutTiles() => DeckHubLayoutMode = DeckHubLayoutMode.Tiles;

    [RelayCommand]
    public async Task LoadDecksAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        StatusIsError = false;
        StatusMessage = UserMessages.StatusClear;

        try
        {
            await RefreshDecksListAsync(updateStatusLineWithDeckCount: true);
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

    /// <summary>
    /// Reloads deck list from DB. When <paramref name="updateStatusLineWithDeckCount"/> is false, leaves
    /// status text unchanged (used after import while busy).
    /// </summary>
    private async Task RefreshDecksListAsync(bool updateStatusLineWithDeckCount)
    {
        var list = await _deckService.GetDecksAsync();
        var collection = new ObservableCollection<DeckEntity>(list);
        var isEmpty = collection.Count == 0;
        var countStatus = isEmpty ? UserMessages.StatusClear : FormatDeckCount(collection.Count);

        MainThread.BeginInvokeOnMainThread(() =>
        {
            Decks = collection;
            IsEmpty = isEmpty;
            OnPropertyChanged(nameof(IsDeckHubListVisible));
            OnPropertyChanged(nameof(IsDeckHubTilesVisible));
            if (updateStatusLineWithDeckCount)
                StatusMessage = countStatus;
        });
    }

    [RelayCommand]
    public async Task DeleteDeckAsync(DeckEntity deck)
    {
        try
        {
            await _deckService.DeleteDeckAsync(deck.Id);
            MainThread.BeginInvokeOnMainThread(() =>
            {
                Decks.Remove(deck);
                IsEmpty = Decks.Count == 0;
                OnPropertyChanged(nameof(IsDeckHubListVisible));
                OnPropertyChanged(nameof(IsDeckHubTilesVisible));
                StatusMessage = Decks.Count == 0 ? UserMessages.StatusClear : FormatDeckCount(Decks.Count);
            });
        }
        catch (Exception ex)
        {
            StatusIsError = true;
            StatusMessage = UserMessages.DeleteFailed(ex.Message);
        }
    }

    private static string FormatDeckCount(int count) => $"{count} deck{(count == 1 ? "" : "s")}";

    [RelayCommand]
    public async Task ImportDecks()
    {
        if (IsBusy) return;
        try
        {
            var result = await FilePickerHelper.PickDeckImportFileAsync("Select a deck file to import (CSV or TXT)");
            if (result == null) return;

            await RunDeckImportAsync(
                "file",
                progress => Task.Run(async () =>
                {
                    using var stream = await result.OpenReadAsync();
                    return await _deckImporter.ImportFromFileStreamAsync(stream, result.FileName, progress);
                }));
        }
        catch (Exception ex)
        {
            Logger.LogStuff($"Failed to import decks: {ex.Message}", LogLevel.Error);
            StatusIsError = true;
            StatusMessage = UserMessages.ImportFailed(ex.Message);
        }
    }

    public Task ImportDeckFromUrlAsync(string url) =>
        RunDeckImportAsync(
            "URL",
            progress => Task.Run(() => _deckUrlImporter.ImportFromUrlAsync(url, progress)));

    public Task ImportDeckFromPlainTextAsync(string text) =>
        RunDeckImportAsync(
            "Paste",
            progress => Task.Run(() =>
                _deckImporter.ImportFromPlainTextAsync(text, "Pasted deck", progress)));

    private async Task RunDeckImportAsync(
        string sourceLabel,
        Func<Action<string, int>, Task<DeckImportResult>> import)
    {
        if (IsBusy) return;
        IsBusy = true;
        StatusIsError = false;
        StatusMessage = UserMessages.ImportingDecks;

        void OnProgress(string message, int _)
        {
            MainThread.BeginInvokeOnMainThread(() => { StatusMessage = message; });
        }

        try
        {
            var importResult = await import(OnProgress);

            if (importResult.Errors.Count > 0)
                Logger.LogStuff($"{sourceLabel} import completed with {importResult.Errors.Count} errors. First: {importResult.Errors[0]}", LogLevel.Warning);
            if (importResult.Warnings.Count > 0)
                Logger.LogStuff($"{sourceLabel} import completed with {importResult.Warnings.Count} warnings. First: {importResult.Warnings[0]}", LogLevel.Warning);

            await RefreshDecksListAsync(updateStatusLineWithDeckCount: false);
            MainThread.BeginInvokeOnMainThread(() =>
            {
                StatusIsError = importResult.Errors.Count > 0;
                StatusMessage = importResult.Errors.Count > 0
                    ? UserMessages.ImportFailed(importResult.Errors[0])
                    : UserMessages.ImportedDecksToast(importResult.ImportedDecks, importResult.ImportedCards);
            });
        }
        catch (Exception ex)
        {
            Logger.LogStuff($"{sourceLabel} deck import failed: {ex.Message}", LogLevel.Error);
            StatusIsError = true;
            StatusMessage = UserMessages.ImportFailed(ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    public async Task ExportDecks()
    {
        if (IsBusy) return;
        try
        {
            IsBusy = true;
            StatusIsError = false;
            StatusMessage = UserMessages.ExportingDecks;

            var csvText = await _deckExporter.ExportAllDecksToCsvAsync();
            if (string.IsNullOrWhiteSpace(csvText))
            {
                StatusMessage = UserMessages.NoDecksToExport;
                return;
            }

            var cacheFile = Path.Combine(FileSystem.CacheDirectory, "decks_export.csv");
            await File.WriteAllTextAsync(cacheFile, csvText, Encoding.UTF8);

            await Share.Default.RequestAsync(new ShareFileRequest
            {
                Title = "Export Decks",
                File = new ShareFile(cacheFile)
            });

            StatusMessage = UserMessages.StatusClear;
        }
        catch (Exception ex)
        {
            Logger.LogStuff($"Failed to export decks: {ex.Message}", LogLevel.Error);
            StatusIsError = true;
            StatusMessage = UserMessages.ExportFailed(ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task<int> CreateDeckAsync(string name, Core.DeckFormat format, string description)
    {
        return await _deckService.CreateDeckAsync(name, format, description);
    }

    public async Task RenameDeckAsync(DeckEntity deck, string newName)
    {
        await _deckService.UpdateDeckNameAsync(deck.Id, newName);
        await LoadDecksAsync();
    }

    /// <summary>Sets hub tile art from the card search picker (Decks tab).</summary>
    public async Task ApplyDeckHubCoverFromPickerAsync(DeckEntity deck, Card card)
    {
        try
        {
            var r = await _deckService.SetDeckCoverAsync(deck.Id, card.Uuid);
            if (!r.IsSuccess)
            {
                StatusIsError = true;
                StatusMessage = r.Message;
                return;
            }

            StatusIsError = false;
            await RefreshDecksListAsync(updateStatusLineWithDeckCount: false);
        }
        catch (Exception ex)
        {
            StatusIsError = true;
            StatusMessage = UserMessages.LoadFailed(ex.Message);
            Logger.LogStuff($"Set deck cover failed: {ex.Message}", LogLevel.Error);
        }
    }

    /// <summary>Removes custom hub art so commander or placeholder is used again.</summary>
    public async Task ClearDeckHubCoverAsync(DeckEntity deck)
    {
        if (string.IsNullOrEmpty(deck.CoverCardId))
        {
            StatusIsError = false;
            StatusMessage = UserMessages.DeckHubPictureNothingToClear;
            return;
        }

        try
        {
            var r = await _deckService.SetDeckCoverAsync(deck.Id, null);
            if (!r.IsSuccess)
            {
                StatusIsError = true;
                StatusMessage = r.Message;
                return;
            }

            StatusIsError = false;
            await RefreshDecksListAsync(updateStatusLineWithDeckCount: false);
        }
        catch (Exception ex)
        {
            StatusIsError = true;
            StatusMessage = UserMessages.LoadFailed(ex.Message);
            Logger.LogStuff($"Clear deck cover failed: {ex.Message}", LogLevel.Error);
        }
    }

    [RelayCommand]
    private async Task DeckTappedAsync(DeckEntity deck)
    {
        await ShellDeckDetailNavigation.GoToAsync(deck.Id);
    }
}
