using CommunityToolkit.Maui.Views;

namespace MTGFetchMAUI.Controls;

public partial class CollectionAddSheet : Popup
{
    private int _quantity = 1;
    private int _currentInCollection = 0;

    public CollectionAddSheet(string cardName, string setInfo, int currentQty)
    {
        InitializeComponent();

        _quantity = 1;
        _currentInCollection = currentQty;

        TitleLabel.Text = cardName;
        SetLabel.Text = setInfo;
        CollectionInfoLabel.Text = currentQty > 0
            ? $"Currently in collection: {currentQty}"
            : "Not in collection yet";

        UpdateQuantityUI();
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
        int newTotal = _currentInCollection + _quantity;
        // In v14, CloseAsync requires a token. We cast the result to object because this is a non-generic Popup.
        await CloseAsync(newTotal, CancellationToken.None);
    }

    private async void OnCancelClicked(object? sender, EventArgs e)
    {
        await CloseAsync(null, CancellationToken.None);
    }
}
