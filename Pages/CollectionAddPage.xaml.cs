namespace AetherVault.Pages;

public record CollectionAddResult(int NewQuantity, bool IsFoil, bool IsEtched);

public partial class CollectionAddPage : ContentPage
{
    private int _quantity;
    private int _currentInCollection;
    private int _minQuantity;
    private readonly TaskCompletionSource<CollectionAddResult?> _tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>Set by caller after resolving from DI.</summary>
    public string CardName { get; set; } = "";

    /// <summary>Set by caller (e.g. set code and number).</summary>
    public string SetInfo { get; set; } = "";

    /// <summary>Current quantity in collection; 0 if not in collection.</summary>
    public int CurrentQty { get; set; }

    public Task<CollectionAddResult?> Result => _tcs.Task;

    public CollectionAddPage()
    {
        InitializeComponent();
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _currentInCollection = CurrentQty;
        if (CurrentQty > 0)
        {
            _quantity = CurrentQty;
            _minQuantity = 0;
            QuantitySelector.HeaderText = "Set quantity";
        }
        else
        {
            _quantity = 1;
            _minQuantity = 1;
            QuantitySelector.HeaderText = "Quantity";
        }
        QuantitySelector.Quantity = _quantity;
        QuantitySelector.Minimum = _minQuantity;
        QuantitySelector.Maximum = 999;
        QuantitySelector.QuantityChanged += OnQuantitySelectorQuantityChanged;
        TitleLabel.Text = CardName;
        SetLabel.Text = SetInfo;
        CollectionInfoLabel.Text = CurrentQty > 0
            ? $"Currently in collection: {CurrentQty}"
            : "Not in collection yet";
        UpdateQuantityUI();
    }

    private void OnQuantitySelectorQuantityChanged(object? sender, int newQuantity)
    {
        _quantity = newQuantity;
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
        QuantitySelector.QuantityChanged -= OnQuantitySelectorQuantityChanged;
        if (!_tcs.Task.IsCompleted)
            _tcs.TrySetResult(null);
    }

    private void UpdateQuantityUI()
    {
        RemoveWarningLabel.IsVisible = _quantity == 0 && _currentInCollection > 0;

        if (_quantity == 0)
            ConfirmBtn.Text = "Remove from Collection";
        else if (_currentInCollection > 0)
            ConfirmBtn.Text = "Update Quantity";
        else
            ConfirmBtn.Text = "Add to Collection";
    }

    private async void OnConfirmClicked(object? sender, EventArgs e)
    {
        var result = new CollectionAddResult(
            QuantitySelector.Quantity,
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
