namespace MTGFetchMAUI.Controls;

public partial class ManaCostView : ContentView
{
    public static readonly BindableProperty ManaTextProperty = BindableProperty.Create(
        nameof(ManaText),
        typeof(string),
        typeof(ManaCostView),
        defaultValue: string.Empty,
        propertyChanged: OnManaTextChanged);

    public string ManaText
    {
        get => (string)GetValue(ManaTextProperty);
        set => SetValue(ManaTextProperty, value);
    }

    public static readonly BindableProperty SymbolSizeProperty = BindableProperty.Create(
        nameof(SymbolSize),
        typeof(double),
        typeof(ManaCostView),
        defaultValue: 20d,
        propertyChanged: OnSymbolSizeChanged);

    public double SymbolSize
    {
        get => (double)GetValue(SymbolSizeProperty);
        set => SetValue(SymbolSizeProperty, value);
    }

    public static readonly BindableProperty SpacingProperty = BindableProperty.Create(
        nameof(Spacing),
        typeof(double),
        typeof(ManaCostView),
        defaultValue: 2d,
        propertyChanged: OnSpacingChanged);

    public double Spacing
    {
        get => (double)GetValue(SpacingProperty);
        set => SetValue(SpacingProperty, value);
    }

    public ManaCostView()
    {
        InitializeComponent();
        SymbolsStack.Spacing = Spacing;
    }

    private static void OnManaTextChanged(BindableObject bindable, object oldValue, object newValue)
    {
        if (bindable is ManaCostView view)
        {
            view.UpdateSymbols((string)newValue);
        }
    }

    private static void OnSymbolSizeChanged(BindableObject bindable, object oldValue, object newValue)
    {
        if (bindable is ManaCostView view && newValue is double size)
        {
            foreach (var child in view.SymbolsStack.Children)
            {
                if (child is View v)
                {
                    v.WidthRequest = size;
                    v.HeightRequest = size;
                }
            }
        }
    }

    private static void OnSpacingChanged(BindableObject bindable, object oldValue, object newValue)
    {
        if (bindable is ManaCostView view && newValue is double spacing)
        {
            view.SymbolsStack.Spacing = spacing;
        }
    }

    private void UpdateSymbols(string? manaText)
    {
        SymbolsStack.Children.Clear();
        if (string.IsNullOrEmpty(manaText)) return;

        // manaText is like "{2}{W}{U}"
        int start = 0;
        while (start < manaText.Length)
        {
            int braceStart = manaText.IndexOf('{', start);
            if (braceStart == -1) break;

            int braceEnd = manaText.IndexOf('}', braceStart);
            if (braceEnd == -1) break;

            if (braceEnd > braceStart + 1)
            {
                string symbol = manaText.Substring(braceStart + 1, braceEnd - braceStart - 1);

                var sv = new ManaSymbolView
                {
                    Symbol = symbol,
                    WidthRequest = SymbolSize,
                    HeightRequest = SymbolSize
                };
                SymbolsStack.Children.Add(sv);
            }

            start = braceEnd + 1;
        }
    }
}