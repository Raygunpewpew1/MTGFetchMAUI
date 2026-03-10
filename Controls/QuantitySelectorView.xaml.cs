namespace AetherVault.Controls;

/// <summary>
/// Reusable quantity selector with minus/plus buttons and optional header.
/// Raise <see cref="QuantityChanged"/> when value changes so parents can sync (e.g. UpdateQuantityUI).
/// </summary>
public partial class QuantitySelectorView : ContentView
{
    public static readonly BindableProperty QuantityProperty = BindableProperty.Create(
        nameof(Quantity), typeof(int), typeof(QuantitySelectorView), 1,
        BindingMode.TwoWay,
        propertyChanged: (b, _, newVal) => ((QuantitySelectorView)b).OnQuantityChangedInternal((int)newVal!));

    public static readonly BindableProperty MinimumProperty = BindableProperty.Create(
        nameof(Minimum), typeof(int), typeof(QuantitySelectorView), 1);

    public static readonly BindableProperty MaximumProperty = BindableProperty.Create(
        nameof(Maximum), typeof(int), typeof(QuantitySelectorView), 99);

    public static readonly BindableProperty HeaderTextProperty = BindableProperty.Create(
        nameof(HeaderText), typeof(string), typeof(QuantitySelectorView), "Quantity");

    public int Quantity
    {
        get => (int)GetValue(QuantityProperty);
        set => SetValue(QuantityProperty, value);
    }

    public int Minimum
    {
        get => (int)GetValue(MinimumProperty);
        set => SetValue(MinimumProperty, value);
    }

    public int Maximum
    {
        get => (int)GetValue(MaximumProperty);
        set => SetValue(MaximumProperty, value);
    }

    public string? HeaderText
    {
        get => (string?)GetValue(HeaderTextProperty);
        set => SetValue(HeaderTextProperty, value);
    }

    /// <summary>Raised after Quantity is updated (e.g. from +/- buttons). Parent can sync state and update UI.</summary>
    public event EventHandler<int>? QuantityChanged;

    /// <summary>Raised when the quantity label is tapped; parent can show a prompt and set Quantity.</summary>
    public event EventHandler? EditRequested;

    public QuantitySelectorView()
    {
        InitializeComponent();
    }

    private void OnQuantityChangedInternal(int newQuantity)
    {
        UpdateButtonsEnabled();
        QuantityChanged?.Invoke(this, newQuantity);
    }

    private void UpdateButtonsEnabled()
    {
        int q = Quantity;
        MinusBtn.IsEnabled = q > Minimum;
        MinusBtn.Opacity = q > Minimum ? 1.0 : 0.4;
        PlusBtn.IsEnabled = q < Maximum;
        PlusBtn.Opacity = q < Maximum ? 1.0 : 0.4;
    }

    private void OnMinusClicked(object? sender, EventArgs e)
    {
        if (Quantity > Minimum)
        {
            Quantity = Quantity - 1;
        }
    }

    private void OnPlusClicked(object? sender, EventArgs e)
    {
        if (Quantity < Maximum)
        {
            Quantity = Quantity + 1;
        }
    }

    private void OnQuantityLabelTapped(object? sender, TappedEventArgs e)
    {
        EditRequested?.Invoke(this, EventArgs.Empty);
    }

    protected override void OnBindingContextChanged()
    {
        base.OnBindingContextChanged();
        UpdateButtonsEnabled();
    }
}
