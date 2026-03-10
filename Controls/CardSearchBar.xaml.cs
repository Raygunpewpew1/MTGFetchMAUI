using System.Windows.Input;

namespace AetherVault.Controls;

/// <summary>
/// Reusable search bar: TextField + Filters button + ViewMode button.
/// Bind FiltersCommand to use command (e.g. SearchPage); otherwise subscribe to FiltersTapped (e.g. CardSearchPickerPage).
/// BindingContext should be the search ViewModel (SearchViewModel / CardSearchPickerViewModel).
/// </summary>
public partial class CardSearchBar : ContentView
{
    public static readonly BindableProperty FiltersCommandProperty = BindableProperty.Create(
        nameof(FiltersCommand), typeof(ICommand), typeof(CardSearchBar), null);

    public ICommand? FiltersCommand
    {
        get => (ICommand?)GetValue(FiltersCommandProperty);
        set => SetValue(FiltersCommandProperty, value);
    }

    /// <summary>Raised when Filters button is tapped and FiltersCommand is null.</summary>
    public event EventHandler? FiltersTapped;

    public CardSearchBar()
    {
        InitializeComponent();
    }

    /// <summary>Focuses the search entry (e.g. from page OnAppearing).</summary>
    public void FocusSearch()
    {
        SearchEntry.Focus();
    }

    private void OnFiltersButtonClicked(object? sender, EventArgs e)
    {
        if (FiltersCommand != null && FiltersCommand.CanExecute(null))
        {
            FiltersCommand.Execute(null);
        }
        else
        {
            FiltersTapped?.Invoke(this, EventArgs.Empty);
        }
    }
}
