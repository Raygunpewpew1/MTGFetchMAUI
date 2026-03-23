using AetherVault.ViewModels;

namespace AetherVault.Pages;

public partial class DeckAddCardsPage : ContentPage
{
    private Func<Task>? _dismissModal;

    public DeckAddCardsPage()
    {
        InitializeComponent();
    }

    /// <summary>Pops the modal using the same navigation object that opened it.</summary>
    public void Init(DeckDetailViewModel viewModel, Func<Task> dismissModal)
    {
        BindingContext = viewModel;
        _dismissModal = dismissModal;
    }

    protected override void OnDisappearing()
    {
        if (BindingContext is DeckDetailViewModel vm)
            vm.ClearAddCardSearch();
        base.OnDisappearing();
    }

    private async void OnDoneClicked(object? sender, EventArgs e)
    {
        if (BindingContext is DeckDetailViewModel vm)
            vm.ClearAddCardSearch();

        if (_dismissModal != null)
            await _dismissModal();
        else
            await Navigation.PopModalAsync();
    }
}
