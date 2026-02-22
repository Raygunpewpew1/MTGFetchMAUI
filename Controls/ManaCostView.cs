using Microsoft.Maui.Controls;

namespace MTGFetchMAUI.Controls;

public class ManaCostView : HorizontalStackLayout
{
    public static readonly BindableProperty ManaTextProperty = BindableProperty.Create(
        nameof(ManaText), typeof(string), typeof(ManaCostView), string.Empty, propertyChanged: OnManaTextChanged);

    public static readonly BindableProperty SymbolSizeProperty = BindableProperty.Create(
        nameof(SymbolSize), typeof(double), typeof(ManaCostView), 18.0, propertyChanged: OnSymbolSizeChanged);

    public string ManaText
    {
        get => (string)GetValue(ManaTextProperty);
        set => SetValue(ManaTextProperty, value);
    }

    public double SymbolSize
    {
        get => (double)GetValue(SymbolSizeProperty);
        set => SetValue(SymbolSizeProperty, value);
    }

    public ManaCostView()
    {
        Spacing = 2;
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
        if (bindable is ManaCostView view)
        {
            double size = (double)newValue;
            foreach (var child in view.Children)
            {
                if (child is Image img)
                {
                    img.HeightRequest = size;
                    img.WidthRequest = size;
                }
            }
        }
    }

    private void UpdateSymbols(string manaText)
    {
        Children.Clear();
        if (string.IsNullOrEmpty(manaText)) return;

        int i = 0;
        while (i < manaText.Length)
        {
            if (manaText[i] == '{')
            {
                int end = manaText.IndexOf('}', i);
                if (end > i)
                {
                    string symbol = manaText.Substring(i + 1, end - i - 1);
                    AddSymbol(symbol);
                    i = end + 1;
                    continue;
                }
            }
            i++;
        }
    }

    private void AddSymbol(string symbol)
    {
        // Normalize symbol name for resource lookup
        // Example: {2/U} -> 2/U -> 2_u -> mana_2_u.png
        string normalized = symbol.Replace("/", "_").ToLowerInvariant();
        string source = $"mana_{normalized}.png";

        var img = new Image
        {
            Source = source,
            HeightRequest = SymbolSize,
            WidthRequest = SymbolSize,
            Aspect = Aspect.AspectFit,
            VerticalOptions = LayoutOptions.Center
        };
        Children.Add(img);
    }
}
