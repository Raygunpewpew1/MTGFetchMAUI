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

        // 1. Reset State
        this.Opacity = 1;           // Ensure root is visible
        Dimmer.Opacity = 0;         // Hide dimmer initially
        IsVisible = true;           // Make control part of visual tree

        // 2. Push Sheet Off-Screen
        // Use a large value initially to guarantee it's hidden while layout happens
        SheetContainer.TranslationY = 2000;

        // 3. Wait for Layout
        // Mandatory yield to UI thread to let Android measure the views
        await Task.Delay(50);

        // 4. Calculate proper start position
        // Determine the height to slide from
        double screenHeight = DeviceDisplay.MainDisplayInfo.Height / DeviceDisplay.MainDisplayInfo.Density;
        double startY = this.Height > 0 ? this.Height : screenHeight;
        if (startY <= 0) startY = 1000; // Safe fallback

        // Set the actual start position (off-screen)
        SheetContainer.TranslationY = startY;

        // 5. Animate In
        // Dimmer fades in, Sheet slides up to 0 (its natural layout position)
        await Task.WhenAll(
            Dimmer.FadeTo(1, 250, Easing.CubicOut),
            SheetContainer.TranslateTo(0, 0, 400, Easing.SpringOut)
        );

        return await _tcs.Task;
    }

    private async Task HideAsync(int? result)
    {
        // Calculate off-screen position
        double screenHeight = DeviceDisplay.MainDisplayInfo.Height / DeviceDisplay.MainDisplayInfo.Density;
        double endY = this.Height > 0 ? this.Height : screenHeight;
        if (endY <= 0) endY = 1000;

        // Animate Out
        // Slide to (0, endY)
        await Task.WhenAll(
            SheetContainer.TranslateTo(0, endY, 250, Easing.CubicIn),
            Dimmer.FadeTo(0, 200, Easing.CubicIn)
        );

        IsVisible = false;
        _tcs?.TrySetResult(result);
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
