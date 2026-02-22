namespace MTGFetchMAUI.Controls;

public class CardTextView : View
{
    public static readonly BindableProperty CardTextProperty = BindableProperty.Create(
        nameof(CardText),
        typeof(string),
        typeof(CardTextView),
        defaultValue: string.Empty);

    public string CardText
    {
        get => (string)GetValue(CardTextProperty);
        set => SetValue(CardTextProperty, value);
    }

    public static readonly BindableProperty TextSizeProperty = BindableProperty.Create(
        nameof(TextSize),
        typeof(double),
        typeof(CardTextView),
        defaultValue: 14d);

    public double TextSize
    {
        get => (double)GetValue(TextSizeProperty);
        set => SetValue(TextSizeProperty, value);
    }

    public static readonly BindableProperty TextColorProperty = BindableProperty.Create(
        nameof(TextColor),
        typeof(Color),
        typeof(CardTextView),
        defaultValue: Colors.White);

    public Color TextColor
    {
        get => (Color)GetValue(TextColorProperty);
        set => SetValue(TextColorProperty, value);
    }

    public static readonly BindableProperty KeywordColorProperty = BindableProperty.Create(
        nameof(KeywordColor),
        typeof(Color),
        typeof(CardTextView),
        defaultValue: Colors.Gold);

    public Color KeywordColor
    {
        get => (Color)GetValue(KeywordColorProperty);
        set => SetValue(KeywordColorProperty, value);
    }

    public static readonly BindableProperty SymbolSizeProperty = BindableProperty.Create(
        nameof(SymbolSize),
        typeof(double),
        typeof(CardTextView),
        defaultValue: 16d);

    public double SymbolSize
    {
        get => (double)GetValue(SymbolSizeProperty);
        set => SetValue(SymbolSizeProperty, value);
    }

    public CardTextView()
    {
        if (DeviceInfo.Idiom == DeviceIdiom.Tablet || DeviceInfo.Idiom == DeviceIdiom.Desktop)
        {
            TextSize = 18d;
        }
    }
}
