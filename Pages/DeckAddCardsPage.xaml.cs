using AetherVault.ViewModels;

namespace AetherVault.Pages;

public partial class DeckAddCardsPage : ContentPage
{
    private readonly DeckAddCardsViewModel _addCardsVm;
    private Func<Task>? _dismissModal;

    public DeckAddCardsPage(DeckAddCardsViewModel addCardsVm)
    {
        InitializeComponent();
        _addCardsVm = addCardsVm;
        AddResultsCardGrid.CardClicked += OnAddResultCardClicked;
    }

    private void OnAddResultCardClicked(string cardUuid) => _addCardsVm.OnResultCardClicked(cardUuid);

    /// <summary>Strategy chip: pick a commander archetype for the suggestion engine.</summary>
    private async void OnStrategyChipClicked(object? sender, EventArgs e)
    {
        const string cancel = "Cancel";
        string pick = await DisplayActionSheetAsync(
            Constants.UserMessages.DeckAddStrategySheetTitle,
            cancel,
            null,
            _addCardsVm.CommanderArchetypePickerItems);
        if (string.IsNullOrEmpty(pick) || pick == cancel) return;

        int index = Array.IndexOf(_addCardsVm.CommanderArchetypePickerItems, pick);
        if (index >= 0)
            _addCardsVm.CommanderArchetypePickerIndex = index;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _addCardsVm.AttachGrid(AddResultsCardGrid);
        AddResultsCardGrid.OnResume();
        _addCardsVm.NotifyAddCardsSheetAppeared();
    }

    /// <summary>Pops the modal using the same navigation object that opened it.</summary>
    public void Init(DeckDetailViewModel deckVm, Func<Task> dismissModal)
    {
        _addCardsVm.PrepareModalTarget(deckVm.ConsumePendingAddModalTargetSection());
        _addCardsVm.AttachHost(deckVm);

        BindingContext = _addCardsVm;
        _dismissModal = dismissModal;
        _addCardsVm.AddCardsModalDismissAction = async () =>
        {
            _addCardsVm.ClearAddCardSearch();
            await dismissModal();
        };
    }

    protected override void OnDisappearing()
    {
        _addCardsVm.AddCardsModalDismissAction = null;
        _addCardsVm.DetachHost();
        _addCardsVm.DetachGrid();
        _addCardsVm.ClearAddCardSearch();
        base.OnDisappearing();
    }

    private async void OnDoneClicked(object? sender, EventArgs e)
    {
        _addCardsVm.ClearAddCardSearch();

        if (_dismissModal != null)
            await _dismissModal();
        else
            await Navigation.PopModalAsync();
    }
}
