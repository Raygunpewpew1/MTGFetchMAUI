namespace MTGFetchMAUI.Controls;

public partial class ManaSymbolView : ContentView
{
    public static readonly BindableProperty SymbolProperty = BindableProperty.Create(
        nameof(Symbol),
        typeof(string),
        typeof(ManaSymbolView),
        defaultValue: string.Empty,
        propertyChanged: OnSymbolChanged);

    public string Symbol
    {
        get => (string)GetValue(SymbolProperty);
        set => SetValue(SymbolProperty, value);
    }

    public ManaSymbolView()
    {
        InitializeComponent();
    }

    private static void OnSymbolChanged(BindableObject bindable, object oldValue, object newValue)
    {
        if (bindable is ManaSymbolView view)
        {
            view.UpdateImage(newValue as string);
        }
    }

    private void UpdateImage(string? symbol)
    {
        if (string.IsNullOrEmpty(symbol))
        {
            SymbolImage.Source = null;
            return;
        }

        // Normalize: {2/W} -> 2_w
        var normalized = symbol.Trim('{', '}').Replace("/", "_").ToLowerInvariant();
        SymbolImage.Source = $"mana_{normalized}.png";
    }
}