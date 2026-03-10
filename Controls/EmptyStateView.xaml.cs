namespace AetherVault.Controls;

/// <summary>
/// Reusable empty state: icon (FontAwesome glyph character) + message + optional secondary message.
/// Set IconGlyph from XAML with {x:Static fa:Solid.MagnifyingGlass} (and xmlns:fa).
/// </summary>
public partial class EmptyStateView : ContentView
{
    public static readonly BindableProperty MessageProperty = BindableProperty.Create(
        nameof(Message), typeof(string), typeof(EmptyStateView), string.Empty);

    public static readonly BindableProperty SecondaryMessageProperty = BindableProperty.Create(
        nameof(SecondaryMessage), typeof(string), typeof(EmptyStateView), string.Empty);

    public static readonly BindableProperty IconGlyphProperty = BindableProperty.Create(
        nameof(IconGlyph), typeof(string), typeof(EmptyStateView), string.Empty,
        propertyChanged: (b, _, newVal) => ((EmptyStateView)b).UpdateIconVisibility());

    public string Message
    {
        get => (string)GetValue(MessageProperty);
        set => SetValue(MessageProperty, value);
    }

    public string SecondaryMessage
    {
        get => (string)GetValue(SecondaryMessageProperty);
        set => SetValue(SecondaryMessageProperty, value);
    }

    /// <summary>FontAwesome Solid glyph character (e.g. from x:Static fa:Solid.MagnifyingGlass).</summary>
    public string IconGlyph
    {
        get => (string)GetValue(IconGlyphProperty);
        set => SetValue(IconGlyphProperty, value);
    }

    public EmptyStateView()
    {
        InitializeComponent();
    }

    private void UpdateIconVisibility()
    {
        if (IconLabel != null)
            IconLabel.IsVisible = !string.IsNullOrEmpty(IconGlyph);
    }

    protected override void OnParentSet()
    {
        base.OnParentSet();
        UpdateIconVisibility();
    }
}
