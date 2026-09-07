using AetherVault.ViewModels;

namespace AetherVault.Views;

/// <summary>
/// Unified deck list for DeckDetailPage: next-step chips, find-in-deck, and one grouped
/// CollectionView (Commander → main type groups → Sideboard).
/// </summary>
public partial class DeckCardsTabView : ContentView
{
    public DeckCardsTabView()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Row tap opens quick detail via CollectionView SelectionChanged (avoids gesture fights on Android; see AGENTS.md).
    /// Rows use ⋯ on the thumbnail for move/remove; no SwipeView.
    /// </summary>
    private void OnDeckItemSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is not CollectionView cv) return;
        if (e.CurrentSelection.FirstOrDefault() is not DeckCardDisplayItem item) return;
        cv.SelectedItem = null;
        if (BindingContext is DeckDetailViewModel vm)
            vm.DeckListItemTappedCommand.Execute(item);
    }
}
