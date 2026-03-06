using AetherVault.Models;
using AetherVault.Services;
using AetherVault.Services.DeckBuilder;
using AetherVault.Services.ImportExport;
using AetherVault.ViewModels;
using System.Text;

namespace AetherVault.Pages;

public partial class DecksPage : ContentPage
{
    private readonly DecksViewModel _viewModel;
    private readonly DeckBuilderService _deckService;
    private readonly DeckImporter _deckImporter;
    private readonly DeckExporter _deckExporter;
    private readonly IToastService _toastService;

    public DecksPage(
        DecksViewModel viewModel,
        DeckBuilderService deckService,
        DeckImporter deckImporter,
        DeckExporter deckExporter,
        IToastService toastService)
    {
        InitializeComponent();
        _viewModel = viewModel;
        _deckService = deckService;
        _deckImporter = deckImporter;
        _deckExporter = deckExporter;
        _toastService = toastService;
        BindingContext = viewModel;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _viewModel.LoadDecksAsync();
    }

    private async void OnNewDeckClicked(object? sender, EventArgs e)
    {
        var modal = new CreateDeckPage(_deckService);
        await Navigation.PushModalAsync(modal);
        int? newId = await modal.WaitForResultAsync();
        if (newId.HasValue)
        {
            await _viewModel.LoadDecksAsync();
            await Shell.Current.GoToAsync($"deckdetail?deckId={newId.Value}");
        }
    }

    private async void OnImportDecksClicked(object? sender, EventArgs e)
    {
        try
        {
            var customFileType = new FilePickerFileType(new Dictionary<DevicePlatform, IEnumerable<string>>
{
    { DevicePlatform.iOS, new[] { "public.comma-separated-values-text" } },
    { DevicePlatform.Android, new[] { "text/csv", "text/comma-separated-values", "application/csv" } },
    { DevicePlatform.WinUI, new[] { ".csv" } },
    { DevicePlatform.MacCatalyst, new[] { "public.comma-separated-values-text" } },
});

            var result = await FilePicker.Default.PickAsync(new PickOptions
            {
                PickerTitle = "Select a deck CSV file to import",
                FileTypes = customFileType,
            });

            if (result == null)
                return;

            _viewModel.IsBusy = true;
            _viewModel.StatusIsError = false;
            _viewModel.StatusMessage = "Importing decks...";

            void OnProgress(string message, int _)
            {
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    _viewModel.StatusMessage = message;
                });
            }

            using var stream = await result.OpenReadAsync();
            var importResult = await Task.Run(async () => await _deckImporter.ImportCsvAsync(stream, OnProgress));

            if (importResult.Errors.Count > 0)
            {
                Logger.LogStuff($"Deck import completed with {importResult.Errors.Count} errors. First: {importResult.Errors[0]}", LogLevel.Warning);
            }

            if (importResult.Warnings.Count > 0)
            {
                Logger.LogStuff($"Deck import completed with {importResult.Warnings.Count} warnings. First: {importResult.Warnings[0]}", LogLevel.Warning);
            }

            await _viewModel.LoadDecksAsync();
            _toastService.Show($"Imported {importResult.ImportedDecks} deck{(importResult.ImportedDecks == 1 ? "" : "s")} ({importResult.ImportedCards} cards).");
        }
        catch (Exception ex)
        {
            Logger.LogStuff($"Failed to import decks: {ex.Message}", LogLevel.Error);
            _viewModel.StatusIsError = true;
            _viewModel.StatusMessage = $"Import failed: {ex.Message}";
            _toastService.Show("Deck import failed.");
        }
        finally
        {
            _viewModel.IsBusy = false;
        }
    }

    private async void OnExportAllDecksClicked(object? sender, EventArgs e)
    {
        try
        {
            _viewModel.IsBusy = true;
            _viewModel.StatusIsError = false;
            _viewModel.StatusMessage = "Exporting decks...";

            var csvText = await _deckExporter.ExportAllDecksToCsvAsync();
            if (string.IsNullOrWhiteSpace(csvText))
            {
                _toastService.Show("No decks to export.");
                _viewModel.StatusMessage = "";
                return;
            }

            var cacheFile = Path.Combine(FileSystem.CacheDirectory, "decks_export.csv");
            await File.WriteAllTextAsync(cacheFile, csvText, Encoding.UTF8);

            await Share.Default.RequestAsync(new ShareFileRequest
            {
                Title = "Export Decks",
                File = new ShareFile(cacheFile)
            });
        }
        catch (Exception ex)
        {
            Logger.LogStuff($"Failed to export decks: {ex.Message}", LogLevel.Error);
            _viewModel.StatusIsError = true;
            _viewModel.StatusMessage = $"Export failed: {ex.Message}";
        }
        finally
        {
            _viewModel.IsBusy = false;
        }
    }

    private async void OnRenameDeckButtonClicked(object? sender, EventArgs e)
    {
        if (sender is Button btn && btn.BindingContext is DeckEntity deck)
        {
            string? newName = await DisplayPromptAsync(
                "Rename deck",
                "Enter a new name:",
                initialValue: deck.Name,
                maxLength: 80);

            if (!string.IsNullOrWhiteSpace(newName) && newName != deck.Name)
                await _viewModel.RenameDeckAsync(deck, newName.Trim());
        }
    }

    private async void OnDeleteDeckButtonClicked(object? sender, EventArgs e)
    {
        if (sender is Button btn && btn.BindingContext is DeckEntity deck)
        {
            await ConfirmAndDeleteDeckAsync(deck);
        }
    }

    private async Task ConfirmAndDeleteDeckAsync(DeckEntity deck)
    {
        bool confirmed = await DisplayAlertAsync(
            "Delete deck",
            $"Delete \"{deck.Name}\"? This cannot be undone.",
            "Delete", "Cancel");

        if (confirmed)
            await _viewModel.DeleteDeckAsync(deck);
    }

    private async void OnDeckSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection?.FirstOrDefault() is DeckEntity deck)
        {
            await _viewModel.DeckTappedCommand.ExecuteAsync(deck);
        }

        if (sender is CollectionView cv)
        {
            cv.SelectedItem = null;
        }
    }
}
