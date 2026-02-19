namespace MTGFetchMAUI.Controls;

public partial class CollectionAddSheet : ContentView
{
    private int _quantity = 1;
    private int _currentInCollection = 0;
    private TaskCompletionSource<int?>? _tcs;

    public CollectionAddSheet()
    {
        InitializeComponent();
    }

    public async Task<int?> ShowAsync(string cardName, string setInfo, int currentQty)
    {
        _quantity = 1;
        _currentInCollection = currentQty;
        _tcs = new TaskCompletionSource<int?>();

        TitleLabel.Text = cardName;
        SetLabel.Text = setInfo;
        CollectionInfoLabel.Text = currentQty > 0
            ? $"Currently in collection: {currentQty}"
            : "Not in collection yet";

        UpdateQuantityUI();

        // Prepare for animation
        this.Opacity = 0;
        SheetContainer.TranslationY = 400;
        IsVisible = true;

        // Small delay to ensure layout is ready
        await Task.Delay(16);

        // Start animations in parallel
        await Task.WhenAll(
            this.FadeToAsync(1, 250, Easing.CubicOut),
            SheetContainer.TranslateToAsync(0, 0, 400, Easing.SpringOut)
        );

        return await _tcs.Task;
    }

    private async Task HideAsync(int? result)
    {
        await Task.WhenAll(
            SheetContainer.TranslateToAsync(0, 400, 250, Easing.CubicIn),
            this.FadeToAsync(0, 200, Easing.CubicIn)
        );
        IsVisible = false;
        _tcs?.SetResult(result);
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

    private void OnQuickAdd1(object? sender, EventArgs e)
    {
        _quantity++;
        UpdateQuantityUI();
    }

    private void OnQuickAdd4(object? sender, EventArgs e)
    {
        _quantity += 4;
        UpdateQuantityUI();
    }

    private void OnSetTo4(object? sender, EventArgs e)
    {
        _quantity = 4;
        UpdateQuantityUI();
    }

    private async void OnConfirmClicked(object? sender, EventArgs e)
    {
        await HideAsync(_quantity);
    }

    private async void OnCancelClicked(object? sender, EventArgs e)
    {
        await HideAsync(null);
    }

    private async void OnBackgroundTapped(object? sender, EventArgs e)
    {
        await HideAsync(null);
    }
}
