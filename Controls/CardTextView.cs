using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;

namespace MTGFetchMAUI.Controls;

public class CardTextView : View
{
    public static readonly BindableProperty CardTextProperty = BindableProperty.Create(
        nameof(CardText), typeof(string), typeof(CardTextView), string.Empty);

    public static readonly BindableProperty TextColorProperty = BindableProperty.Create(
        nameof(TextColor), typeof(Color), typeof(CardTextView), Colors.Black);

    public static readonly BindableProperty TextSizeProperty = BindableProperty.Create(
        nameof(TextSize), typeof(double), typeof(CardTextView), 14.0);

    public static readonly BindableProperty KeywordColorProperty = BindableProperty.Create(
        nameof(KeywordColor), typeof(Color), typeof(CardTextView), Colors.Black);

    public static readonly BindableProperty SymbolSizeProperty = BindableProperty.Create(
        nameof(SymbolSize), typeof(double), typeof(CardTextView), 16.0);

    public string CardText
    {
        get => (string)GetValue(CardTextProperty);
        set => SetValue(CardTextProperty, value);
    }

    public Color TextColor
    {
        get => (Color)GetValue(TextColorProperty);
        set => SetValue(TextColorProperty, value);
    }

    public double TextSize
    {
        get => (double)GetValue(TextSizeProperty);
        set => SetValue(TextSizeProperty, value);
    }

    public Color KeywordColor
    {
        get => (Color)GetValue(KeywordColorProperty);
        set => SetValue(KeywordColorProperty, value);
    }

    public double SymbolSize
    {
        get => (double)GetValue(SymbolSizeProperty);
        set => SetValue(SymbolSizeProperty, value);
    }
}
