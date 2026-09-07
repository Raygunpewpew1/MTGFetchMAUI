using AetherVault.Core;
using AetherVault.Services;
using AetherVault.Services.DeckBuilder;
using AetherVault.Services.ImportExport;
using AetherVault.ViewModels;
using System.Text;

namespace AetherVault.Pages;

[QueryProperty(nameof(DeckId), "deckId")]
public partial class DeckDetailPage : ContentPage
{
    private readonly DeckDetailViewModel _viewModel;
    private readonly IServiceProvider _serviceProvider;
    private readonly DeckBuilderService _deckService;
    private readonly DeckExporter _deckExporter;
    private readonly DeckImporter _deckImporter;
    private readonly CardGalleryContext _galleryContext;
    private readonly DeckSynergyNavigationContext _deckSynergyNavigationContext;

    /// <summary>Reuse the add-cards modal so each open does not re-parse the full XAML tree (major win on Android).</summary>
    private DeckAddCardsPage? _cachedAddCardsPage;

    public string DeckId
    {
        set
        {
            if (int.TryParse(value, out int id))
                _ = _viewModel.LoadAsync(id);
        }
    }

    public DeckDetailPage(
        DeckDetailViewModel viewModel,
        IServiceProvider serviceProvider,
        DeckBuilderService deckService,
        DeckExporter deckExporter,
        DeckImporter deckImporter,
        CardGalleryContext galleryContext,
        DeckSynergyNavigationContext deckSynergyNavigationContext)
    {
        InitializeComponent();
        _viewModel = viewModel;
        _serviceProvider = serviceProvider;
        _deckService = deckService;
        _deckExporter = deckExporter;
        _deckImporter = deckImporter;
        _galleryContext = galleryContext;
        _deckSynergyNavigationContext = deckSynergyNavigationContext;
        BindingContext = viewModel;
    }

    private void OnDeckPricePreferencesChanged(object? sender, EventArgs e) =>
        MainThread.BeginInvokeOnMainThread(() => _viewModel.OnPriceDisplayPreferencesChanged());

    private async void OnDeckDetailMoreClicked(object? sender, EventArgs e)
    {
        const string cancel = "Cancel";
        string pick = await DisplayActionSheetAsync(
            UserMessages.DeckDetailMoreMenuTitle,
            cancel,
            null,
            UserMessages.DeckDetailMoreHubPicture,
            UserMessages.DeckDetailMoreBuyCards,
            UserMessages.DeckDetailMoreImportCsv,
            UserMessages.DeckDetailMoreExportCsv);

        if (pick == UserMessages.DeckDetailMoreHubPicture)
            await OpenDeckHubPictureFlowAsync();
        else if (pick == UserMessages.DeckDetailMoreBuyCards)
            await OpenDeckBuyCardsFlowAsync();
        else if (pick == UserMessages.DeckDetailMoreImportCsv)
            await ImportDeckCsvAsync();
        else if (pick == UserMessages.DeckDetailMoreExportCsv)
            await ExportDeckAsync();
    }

    private async Task OpenDeckBuyCardsFlowAsync()
    {
        const string cancel = "Cancel";
        string pick = await DisplayActionSheetAsync(
            UserMessages.DeckBuySheetTitle,
            cancel,
            null,
            UserMessages.DeckBuyCopyMassEntry,
            UserMessages.DeckBuyOpenTcgMassEntry);

        if (pick == UserMessages.DeckBuyCopyMassEntry)
        {
            string text = _viewModel.BuildDeckBuyListForMassEntry();
            if (string.IsNullOrWhiteSpace(text))
            {
                await DisplayAlertAsync(UserMessages.DeckBuySheetTitle, "Deck is empty.", "OK");
                return;
            }

            await Clipboard.Default.SetTextAsync(text);
            await DisplayAlertAsync(UserMessages.DeckBuyCopiedTitle, UserMessages.DeckBuyCopiedBody, "OK");
        }
        else if (pick == UserMessages.DeckBuyOpenTcgMassEntry)
        {
            var uri = new Uri("https://www.tcgplayer.com/massentry?productline=Magic");
            await Launcher.Default.OpenAsync(uri);
        }
    }

    private async Task OpenDeckHubPictureFlowAsync()
    {
        if (_viewModel.Deck == null) return;

        var choose = UserMessages.DeckHubPictureChooseCard;
        var clear = UserMessages.DeckHubPictureClear;
        const string cancel = "Cancel";
        string pick = await DisplayActionSheetAsync(
            UserMessages.DeckHubPictureSheetTitle,
            cancel,
            null,
            choose,
            clear);

        if (pick == clear)
        {
            if (string.IsNullOrEmpty(_viewModel.Deck.CoverCardId))
            {
                await DisplayAlertAsync(
                    UserMessages.DeckHubPictureSheetTitle,
                    UserMessages.DeckHubPictureNothingToClear,
                    "OK");
                return;
            }

            var cleared = await _viewModel.ClearDeckHubCoverAsync();
            if (cleared.IsError)
                await DisplayAlertAsync(UserMessages.DeckHubPictureSheetTitle, cleared.Message, "OK");
            return;
        }

        if (pick != choose) return;

        var picker = _serviceProvider.GetRequiredService<CardSearchPickerPage>();
        picker.Title = UserMessages.DeckHubPicturePickerTitle;
        await Navigation.PushModalAsync(picker);
        var card = await picker.WaitForResultAsync();
        if (card == null) return;

        var setResult = await _viewModel.SetDeckHubCoverFromCardAsync(card);
        if (setResult.IsError)
            await DisplayAlertAsync(UserMessages.DeckHubPictureSheetTitle, setResult.Message, "OK");
    }

    private async void OnAddCardsClicked(object? sender, EventArgs e)
    {
        await OpenAddCardsModalAsync();
    }

    private async Task OpenAddCardsModalAsync()
    {
        _cachedAddCardsPage ??= _serviceProvider.GetRequiredService<DeckAddCardsPage>();
        var nav = Navigation;
        _cachedAddCardsPage.Init(_viewModel, async () => await nav.PopModalAsync());
        await Navigation.PushModalAsync(_cachedAddCardsPage);
    }

    private void OnAddCardsModalRequested()
    {
        // Open immediately on the UI thread (event is raised from RelayCommand). Extra BeginInvoke
        // deferred the modal behind other work and worsened perceived latency with add-card search.
        if (MainThread.IsMainThread)
            _ = OpenAddCardsModalAsync();
        else
            MainThread.BeginInvokeOnMainThread(() => _ = OpenAddCardsModalAsync());
    }

    /// <summary>Header commander slot: choose when empty, otherwise view / change / remove.</summary>
    private async void OnCommanderSlotTapped(object? sender, EventArgs e)
    {
        if (_viewModel.HasNoCommander)
        {
            _viewModel.RequestAddCardsForSection(0);
            return;
        }

        var commander = _viewModel.FirstCommander;
        if (commander == null) return;

        const string cancel = "Cancel";
        string pick = await DisplayActionSheetAsync(
            UserMessages.DeckCommanderActionsTitle,
            cancel,
            UserMessages.DeckRemoveCommander,
            UserMessages.DeckGridViewDetails,
            UserMessages.DeckChangeCommander);

        if (pick == UserMessages.DeckGridViewDetails)
            _viewModel.ShowCardQuickDetailCommand.Execute(commander);
        else if (pick == UserMessages.DeckChangeCommander)
            _viewModel.RequestAddCardsForSection(0);
        else if (pick == UserMessages.DeckRemoveCommander)
            await _viewModel.RemoveCardCommand.ExecuteAsync(commander);
    }

    private async Task OnValidationDetailsAlertRequested(string body) =>
        await DisplayAlertAsync(UserMessages.ValidationDetailsTitle, body, "OK");

    private void OnDeckRowMenuRequested(DeckCardDisplayItem item) =>
        MainThread.BeginInvokeOnMainThread(() => _ = DeckRowMenuAsync(item));

    /// <summary>Row ⋯ menu: options depend on the row's section (Commander / Main / Sideboard).</summary>
    private async Task DeckRowMenuAsync(DeckCardDisplayItem item)
    {
        const string cancel = "Cancel";
        bool isCommander = string.Equals(item.Entity.Section, DeckCardSections.Commander, StringComparison.OrdinalIgnoreCase);
        bool isMain = string.Equals(item.Entity.Section, DeckCardSections.Main, StringComparison.OrdinalIgnoreCase);

        if (isCommander)
        {
            string commanderPick = await DisplayActionSheetAsync(
                UserMessages.DeckCommanderActionsTitle,
                cancel,
                UserMessages.DeckRemoveCommander,
                UserMessages.DeckGridViewDetails,
                UserMessages.DeckChangeCommander);
            if (commanderPick == UserMessages.DeckGridViewDetails)
                _viewModel.ShowCardQuickDetailCommand.Execute(item);
            else if (commanderPick == UserMessages.DeckChangeCommander)
                _viewModel.RequestAddCardsForSection(0);
            else if (commanderPick == UserMessages.DeckRemoveCommander)
                await _viewModel.RemoveCardCommand.ExecuteAsync(item);
            return;
        }

        string move = isMain ? UserMessages.DeckGridMoveToSideboard : UserMessages.DeckGridMoveToMain;
        var pick = await DisplayActionSheetAsync(
            UserMessages.DeckGridCardActionsTitle,
            cancel,
            null,
            move,
            UserMessages.DeckGridRemoveCard,
            UserMessages.DeckGridViewDetails);
        if (pick == move)
        {
            if (isMain)
                await _viewModel.MoveCardRowToSideboardCommand.ExecuteAsync(item);
            else
                await _viewModel.MoveCardRowToMainCommand.ExecuteAsync(item);
        }
        else if (pick == UserMessages.DeckGridRemoveCard)
            await _viewModel.RemoveCardCommand.ExecuteAsync(item);
        else if (pick == UserMessages.DeckGridViewDetails)
            _viewModel.ShowCardQuickDetailCommand.Execute(item);
    }

    private async void OnRequestShowQuickDetail(DeckCardDisplayItem item)
    {
        var uuids = _viewModel.GetOrderedUuidsForCurrentSection();
        if (uuids.Count == 0)
            uuids = [item.CardUuid];
        _galleryContext.SetContext(uuids, item.CardUuid);
        if (_viewModel.Deck != null)
        {
            _deckSynergyNavigationContext.SetDeckContext(
                _viewModel.Deck.Id,
                _viewModel.GetDeckEntitiesSnapshotForSynergy(),
                _viewModel.GetDeckCardMapSnapshotForSynergy());
        }

        await Shell.Current.GoToAsync($"carddetail?uuid={Uri.EscapeDataString(item.CardUuid)}");
    }

    private async Task ImportDeckCsvAsync()
    {
        if (_viewModel.Deck == null) return;

        try
        {
            var pick = await FilePickerHelper.PickDeckImportFileAsync("Select a deck file to import (CSV or TXT)");
            if (pick == null) return;

            _viewModel.IsBusy = true;
            _viewModel.StatusIsError = false;
            _viewModel.StatusMessage = UserMessages.ImportingDecks;

            using var stream = await pick.OpenReadAsync();
            var importResult = await Task.Run(async () =>
                await _deckImporter.ImportFromFileStreamAsync(stream, pick.FileName));

            if (importResult.Errors.Count > 0)
                Logger.LogStuff($"Deck import: {importResult.Errors.Count} errors. First: {importResult.Errors[0]}", LogLevel.Warning);
            if (importResult.Warnings.Count > 0)
                Logger.LogStuff($"Deck import: {importResult.Warnings.Count} warnings. First: {importResult.Warnings[0]}", LogLevel.Warning);

            _viewModel.StatusIsError = importResult.Errors.Count > 0;
            _viewModel.StatusMessage = importResult.Errors.Count > 0
                ? UserMessages.ImportFailed(importResult.Errors[0])
                : UserMessages.ImportedDecksToast(importResult.ImportedDecks, importResult.ImportedCards);

            await _viewModel.ReloadAsync(preserveState: true);
        }
        catch (Exception ex)
        {
            Logger.LogStuff($"Deck detail import failed: {ex.Message}", LogLevel.Error);
            _viewModel.StatusIsError = true;
            _viewModel.StatusMessage = UserMessages.ImportFailed(ex.Message);
        }
        finally
        {
            _viewModel.IsBusy = false;
        }
    }

    private async Task ExportDeckAsync()
    {
        if (_viewModel.Deck == null) return;

        try
        {
            _viewModel.IsBusy = true;
            _viewModel.StatusIsError = false;
            _viewModel.StatusMessage = UserMessages.ExportingDeck;

            var csvText = await _deckExporter.ExportDeckToCsvAsync(_viewModel.Deck.Id);
            if (string.IsNullOrWhiteSpace(csvText))
            {
                _viewModel.StatusIsError = false;
                _viewModel.StatusMessage = UserMessages.NothingToExport;
                return;
            }

            var safeName = string.Join("_",
                (_viewModel.Deck.Name ?? "deck")
                    .Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries))
                .Trim();

            if (string.IsNullOrWhiteSpace(safeName)) safeName = "deck";

            var cacheFile = Path.Combine(FileSystem.CacheDirectory, $"{safeName}_export.csv");
            await File.WriteAllTextAsync(cacheFile, csvText, Encoding.UTF8);

            await Share.Default.RequestAsync(new ShareFileRequest
            {
                Title = "Export Deck",
                File = new ShareFile(cacheFile)
            });
        }
        catch (Exception ex)
        {
            Logger.LogStuff($"Failed to export deck: {ex.Message}", LogLevel.Error);
            _viewModel.StatusIsError = true;
            _viewModel.StatusMessage = UserMessages.ExportFailed(ex.Message);
        }
        finally
        {
            _viewModel.IsBusy = false;
        }
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _viewModel.ReloadCompleted += RunDeferredLayoutPass;
        _viewModel.RequestShowQuickDetail += OnRequestShowQuickDetail;
        _viewModel.ValidationDetailsAlertRequested += OnValidationDetailsAlertRequested;
        _viewModel.AddCardsModalRequested += OnAddCardsModalRequested;
        _viewModel.DeckRowMenuRequested += OnDeckRowMenuRequested;
        PricePreferences.PriceDisplayPreferencesChanged += OnDeckPricePreferencesChanged;
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        PricePreferences.PriceDisplayPreferencesChanged -= OnDeckPricePreferencesChanged;
        _viewModel.ReloadCompleted -= RunDeferredLayoutPass;
        _viewModel.RequestShowQuickDetail -= OnRequestShowQuickDetail;
        _viewModel.ValidationDetailsAlertRequested -= OnValidationDetailsAlertRequested;
        _viewModel.AddCardsModalRequested -= OnAddCardsModalRequested;
        _viewModel.DeckRowMenuRequested -= OnDeckRowMenuRequested;
    }

    /// <summary>Delay so invalidate runs after WindowManager destroys modal surface (logcat: Destroying surface → focus change).</summary>
    private const int DeferredLayoutDelayMs = 220;

    private void RunDeferredLayoutPass()
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            if (Window == null) return;
            try
            {
                (Content as View)?.InvalidateMeasure();
                DeckDetailRoot.InvalidateMeasure();
            }
            catch (Exception ex)
            {
                Logger.LogStuff($"[DeckDetail] RunDeferredLayoutPass: {ex.Message}", LogLevel.Warning);
            }
        });
        _ = Task.Run(async () =>
        {
            await Task.Delay(DeferredLayoutDelayMs);
            MainThread.BeginInvokeOnMainThread(() =>
            {
                if (Window == null) return;
                try
                {
                    (Content as View)?.InvalidateMeasure();
                    DeckDetailRoot.InvalidateMeasure();
                }
                catch (Exception ex)
                {
                    Logger.LogStuff($"[DeckDetail] RunDeferredLayoutPass (delayed): {ex.Message}", LogLevel.Warning);
                }
            });
        });
    }
}
