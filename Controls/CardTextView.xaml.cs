using System.Text.RegularExpressions;
using Microsoft.Maui.Layouts;

namespace MTGFetchMAUI.Controls;

public partial class CardTextView : ContentView
{
    private static readonly Regex SymbolPattern = new(@"\{([^}]+)\}", RegexOptions.Compiled);

    private static readonly string[] DefaultKeywords =
    [
        "Flying", "First strike", "Double strike", "Deathtouch", "Haste",
        "Hexproof", "Indestructible", "Lifelink", "Menace", "Reach",
        "Trample", "Vigilance", "Flash", "Defender", "Ward", "Shroud",
        "Fear", "Intimidate", "Prowess", "Convoke", "Delve",
        "Equip", "Enchant", "Exile", "Transform", "Mill",
        "Scry", "Surveil", "Protection", "Flashback", "Kicker",
        "Cycling", "Cascade", "Infect", "Toxic"
    ];

    private Regex? _keywordRegex;
    private readonly HashSet<string> _keywords = new(DefaultKeywords, StringComparer.OrdinalIgnoreCase);

    public static readonly BindableProperty CardTextProperty = BindableProperty.Create(
        nameof(CardText), typeof(string), typeof(CardTextView), string.Empty, propertyChanged: OnTextChanged);

    public string CardText
    {
        get => (string)GetValue(CardTextProperty);
        set => SetValue(CardTextProperty, value);
    }

    public static readonly BindableProperty TextSizeProperty = BindableProperty.Create(
        nameof(TextSize), typeof(double), typeof(CardTextView), 14d, propertyChanged: OnStyleChanged);

    public double TextSize
    {
        get => (double)GetValue(TextSizeProperty);
        set => SetValue(TextSizeProperty, value);
    }

    public static readonly BindableProperty TextColorProperty = BindableProperty.Create(
        nameof(TextColor), typeof(Color), typeof(CardTextView), Colors.White, propertyChanged: OnStyleChanged);

    public Color TextColor
    {
        get => (Color)GetValue(TextColorProperty);
        set => SetValue(TextColorProperty, value);
    }

    public static readonly BindableProperty KeywordColorProperty = BindableProperty.Create(
        nameof(KeywordColor), typeof(Color), typeof(CardTextView), Colors.Gold, propertyChanged: OnStyleChanged);

    public Color KeywordColor
    {
        get => (Color)GetValue(KeywordColorProperty);
        set => SetValue(KeywordColorProperty, value);
    }

    public static readonly BindableProperty SymbolSizeProperty = BindableProperty.Create(
        nameof(SymbolSize), typeof(double), typeof(CardTextView), 16d, propertyChanged: OnStyleChanged);

    public double SymbolSize
    {
        get => (double)GetValue(SymbolSizeProperty);
        set => SetValue(SymbolSizeProperty, value);
    }

    public CardTextView()
    {
        InitializeComponent();
        EnsureRegex();
        if (DeviceInfo.Idiom == DeviceIdiom.Tablet || DeviceInfo.Idiom == DeviceIdiom.Desktop)
        {
            TextSize = 18d;
        }
    }

    private static void OnTextChanged(BindableObject bindable, object oldValue, object newValue)
        => ((CardTextView)bindable).Render();

    private static void OnStyleChanged(BindableObject bindable, object oldValue, object newValue)
        => ((CardTextView)bindable).Render();

    private void EnsureRegex()
    {
        if (_keywordRegex != null) return;
        var sorted = _keywords.OrderByDescending(k => k.Length);
        var pattern = @"\b(" + string.Join("|", sorted.Select(Regex.Escape)) + @")\b";
        _keywordRegex = new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.Compiled);
    }

    private void Render()
    {
        ParagraphsStack.Children.Clear();
        if (string.IsNullOrEmpty(CardText)) return;

        var paragraphs = CardText.Split('\n');
        foreach (var p in paragraphs)
        {
            if (string.IsNullOrWhiteSpace(p)) continue; // Skip empty lines or render as spacer?

            var flex = new FlexLayout
            {
                Wrap = FlexWrap.Wrap,
                Direction = FlexDirection.Row,
                AlignItems = FlexAlignItems.Center,
                AlignContent = FlexAlignContent.Start,
                HorizontalOptions = LayoutOptions.Fill
            };

            // 1. Process Symbols
            int lastIndex = 0;
            foreach (Match match in SymbolPattern.Matches(p))
            {
                if (match.Index > lastIndex)
                {
                    string textPart = p[lastIndex..match.Index];
                    AddTextToFlex(flex, textPart);
                }

                // Add symbol
                string sym = match.Groups[1].Value;
                flex.Children.Add(new ManaSymbolView
                {
                    Symbol = sym,
                    WidthRequest = SymbolSize,
                    HeightRequest = SymbolSize,
                    Margin = new Thickness(1, 0)
                });

                lastIndex = match.Index + match.Length;
            }

            if (lastIndex < p.Length)
            {
                AddTextToFlex(flex, p[lastIndex..]);
            }

            ParagraphsStack.Children.Add(flex);
        }
    }

    private void AddTextToFlex(FlexLayout flex, string text)
    {
        if (string.IsNullOrEmpty(text)) return;

        // 2. Process Keywords
        if (_keywordRegex != null)
        {
            int lastIndex = 0;
            foreach (Match match in _keywordRegex.Matches(text))
            {
                if (match.Index > lastIndex)
                {
                    AddWordsToFlex(flex, text[lastIndex..match.Index], false);
                }

                AddWordsToFlex(flex, match.Value, true);
                lastIndex = match.Index + match.Length;
            }

            if (lastIndex < text.Length)
            {
                AddWordsToFlex(flex, text[lastIndex..], false);
            }
        }
        else
        {
            AddWordsToFlex(flex, text, false);
        }
    }

    private void AddWordsToFlex(FlexLayout flex, string text, bool isKeyword)
    {
        // Split by whitespace but keep delimiters to preserve spacing?
        // Simple approach: Split by space, add space back to words.
        // "Hello world" -> "Hello ", "world"

        string[] parts = text.Split(' ');
        for (int i = 0; i < parts.Length; i++)
        {
            string word = parts[i];
            if (i < parts.Length - 1) word += " "; // Add space back unless it's the last word

            // If the original text ended with space, the split might have an empty entry at end?
            // "Hello " -> "Hello", ""

            if (string.IsNullOrEmpty(word)) continue;

            var label = new Label
            {
                Text = word,
                FontSize = TextSize,
                TextColor = isKeyword ? KeywordColor : TextColor,
                FontAttributes = isKeyword ? FontAttributes.Bold : FontAttributes.None,
                LineBreakMode = LineBreakMode.NoWrap,
                VerticalTextAlignment = TextAlignment.Center // Align with symbols
            };
            flex.Children.Add(label);
        }
    }
}