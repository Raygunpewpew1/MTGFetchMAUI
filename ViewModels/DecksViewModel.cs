using AetherVault.Data;
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
    private readonly IDeckRepository _deckRepository;
    private readonly DeckImporter _deckImporter;
    private readonly DeckExporter _deckExporter;

    [ObservableProperty]
    public partial ObservableCollection<DeckEntity> Decks { get; set; } = [];

    [ObservableProperty]
    public partial bool IsEmpty { get; set; }

    public DecksViewModel(DeckBuilderService deckService, IDeckRepository deckRepository, DeckImporter deckImporter, DeckExporter deckExporter)
    {
        _deckService = deckService;
        _deckRepository = deckRepository;
        _deckImporter = deckImporter;
        _deckExporter = deckExporter;
    }

    [RelayCommand]
    public async Task LoadDecksAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        StatusIsError = false;
        StatusMessage = UserMessages.StatusClear;

        try
        {
            var list = await _deckService.GetDecksAsync();
            var counts = await _deckRepository.GetDeckCardCountsAsync(list.Select(d => d.Id));
            foreach (var deck in list)
                deck.CardCount = counts.GetValueOrDefault(deck.Id);

            var collection = new ObservableCollection<DeckEntity>(list);
            var isEmpty = collection.Count == 0;
            var statusMessage = isEmpty ? UserMessages.StatusClear : FormatDeckCount(collection.Count);
            MainThread.BeginInvokeOnMainThread(() =>
            {
                Decks = collection;
                IsEmpty = isEmpty;
                StatusMessage = statusMessage;
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
    public async Task ImportDecksAsync()
    {
        if (IsBusy) return;
        try
        {
            var result = await FilePickerHelper.PickCsvFileAsync("Select a deck CSV file to import");
            if (result == null) return;

            IsBusy = true;
            StatusIsError = false;
            StatusMessage = UserMessages.ImportingDecks;

            void OnProgress(string message, int _)
            {
                MainThread.BeginInvokeOnMainThread(() => { StatusMessage = message; });
            }

            using var stream = await result.OpenReadAsync();
            var importResult = await Task.Run(async () => await _deckImporter.ImportCsvAsync(stream, OnProgress));

            if (importResult.Errors.Count > 0)
                Logger.LogStuff($"Deck import completed with {importResult.Errors.Count} errors. First: {importResult.Errors[0]}", LogLevel.Warning);
            if (importResult.Warnings.Count > 0)
                Logger.LogStuff($"Deck import completed with {importResult.Warnings.Count} warnings. First: {importResult.Warnings[0]}", LogLevel.Warning);

            await LoadDecksAsync();
            StatusIsError = false;
            StatusMessage = UserMessages.ImportedDecksToast(importResult.ImportedDecks, importResult.ImportedCards);
        }
        catch (Exception ex)
        {
            Logger.LogStuff($"Failed to import decks: {ex.Message}", LogLevel.Error);
            StatusIsError = true;
            StatusMessage = UserMessages.ImportFailed(ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    public async Task ExportDecksAsync()
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

    [RelayCommand]
    private async Task DeckTappedAsync(DeckEntity deck)
    {
        await Shell.Current.GoToAsync($"deckdetail?deckId={deck.Id}");
    }
}
