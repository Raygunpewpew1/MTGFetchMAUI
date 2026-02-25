namespace MTGFetchMAUI.Pages;

public partial class CollectionAddPage : ContentPage
{
    private int _quantity = 1;
    private int _currentInCollection = 0;
    private TaskCompletionSource<int?> _tcs;

    public Task<int?> Result => _tcs.Task;

    public CollectionAddPage(string cardName, string setInfo, int currentQty)
    {
        InitializeComponent();

        _tcs = new TaskCompletionSource<int?>();
        _quantity = 1;
        _currentInCollection = currentQty;

        TitleLabel.Text = cardName;
        SetLabel.Text = setInfo;
        CollectionInfoLabel.Text = currentQty > 0
            ? $"Currently in collection: {currentQty}"
            : "Not in collection yet";

        UpdateQuantityUI();
    }

    public Task<int?> WaitForResultAsync()
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
        {
            _tcs.TrySetResult(null);
        }
    }

    private void UpdateQuantityUI()
    {
        QuantityLabel.Text = _quantity.ToString();
        ConfirmBtn.Text = _currentInCollection > 0 ? "Update Quantity" : "Add to Collection";
    }

    private void OnMinusClicked(object? sender, EventArgs e)
    {
        if (_quantity > 1)
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
        int newTotal = _currentInCollection + _quantity;
        _tcs.TrySetResult(newTotal);
        await Navigation.PopModalAsync();
    }

    private async void OnCancelClicked(object? sender, EventArgs e)
    {
        _tcs.TrySetResult(null);
        await Navigation.PopModalAsync();
    }
}
