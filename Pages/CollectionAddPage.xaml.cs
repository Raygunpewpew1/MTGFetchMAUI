namespace MTGFetchMAUI.Pages;

public record CollectionAddResult(int NewQuantity, bool IsFoil, bool IsEtched);

public partial class CollectionAddPage : ContentPage
{
    private int _quantity;
    private readonly int _currentInCollection;
    private readonly int _minQuantity;
    private readonly TaskCompletionSource<CollectionAddResult?> _tcs;

    public Task<CollectionAddResult?> Result => _tcs.Task;

    public CollectionAddPage(string cardName, string setInfo, int currentQty)
    {
        InitializeComponent();

        _tcs = new TaskCompletionSource<CollectionAddResult?>();
        _currentInCollection = currentQty;

        // If already in collection: show current total, allow down to 0 (remove)
        // If not in collection: start at 1, minimum is 1
        if (currentQty > 0)
        {
            _quantity = currentQty;
            _minQuantity = 0;
            QuantityHeaderLabel.Text = "Set quantity";
        }
        else
        {
            _quantity = 1;
            _minQuantity = 1;
            QuantityHeaderLabel.Text = "Quantity";
        }

        TitleLabel.Text = cardName;
        SetLabel.Text = setInfo;
        CollectionInfoLabel.Text = currentQty > 0
            ? $"Currently in collection: {currentQty}"
            : "Not in collection yet";

        UpdateQuantityUI();
    }

    public Task<CollectionAddResult?> WaitForResultAsync()
    {
        return _tcs.Task;
    }

    protected override bool OnBackButtonPressed()
    {
        _tcs.TrySetResult(null);
        return base.OnBackButtonPressed();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        if (!_tcs.Task.IsCompleted)
            _tcs.TrySetResult(null);
    }

    private void UpdateQuantityUI()
    {
        QuantityLabel.Text = _quantity.ToString();
        MinusBtn.IsEnabled = _quantity > _minQuantity;
        MinusBtn.Opacity = _quantity > _minQuantity ? 1.0 : 0.4;
        RemoveWarningLabel.IsVisible = _quantity == 0 && _currentInCollection > 0;

        if (_quantity == 0)
            ConfirmBtn.Text = "Remove from Collection";
        else if (_currentInCollection > 0)
            ConfirmBtn.Text = "Update Quantity";
        else
            ConfirmBtn.Text = "Add to Collection";
    }

    private void OnMinusClicked(object? sender, EventArgs e)
    {
        if (_quantity > _minQuantity)
        {
            _quantity--;
            UpdateQuantityUI();
        }
    }

    private void OnPlusClicked(object? sender, EventArgs e)
    {
        _quantity++;
        UpdateQuantityUI();
    }

    private async void OnConfirmClicked(object? sender, EventArgs e)
    {
        var result = new CollectionAddResult(
            _quantity,
            FoilCheckBox.IsChecked,
            EtchedCheckBox.IsChecked);
        _tcs.TrySetResult(result);
        await Navigation.PopModalAsync();
    }

    private async void OnCancelClicked(object? sender, EventArgs e)
    {
        _tcs.TrySetResult(null);
        await Navigation.PopModalAsync();
    }
}
