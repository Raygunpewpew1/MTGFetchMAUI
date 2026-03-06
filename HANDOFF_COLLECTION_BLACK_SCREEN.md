# Handoff: Collection/Decks black screen bug

## What happens
After **clearing the collection** (or sometimes after **import**), the Collection tab shows a **black screen** instead of the "Your collection is empty" message. Same issue can occur on **Decks** tab when empty.

## What we know (from logs)
The **ViewModel flow is correct**:
- `LoadCollectionAsync` loads `_allItems.Count=0`
- Empty branch runs: `IsCollectionEmpty=true` set on main thread
- `UpdateContentHostContent` runs with `IsCollectionEmpty=True`, **setting Content to EmptyState**
- `CollectionLoaded` is **not** fired (`willInvokeCollectionLoaded=False`)
- We no longer call `SetCollectionAsync([])` when empty (to avoid grid pipeline)

So the UI is told to show the empty-state view and the grid is not invoked. **The black screen still appears** — so the cause is likely **platform/rendering**, not ViewModel logic.

## Key files
- **View:** `Pages/CollectionPage.xaml` + `Pages/CollectionPage.xaml.cs`  
  - Content is swapped in code: `CollectionContentHost.Content` = either `_emptyStateView` (built in `BuildEmptyStateView()`) or `CollectionGrid`.
- **ViewModel:** `ViewModels/CollectionViewModel.cs`  
  - Empty branch in `ApplyFilterAndSortAsync()` sets `IsCollectionEmpty=true` via `MainThread.InvokeOnMainThreadAsync`; no `SetCollectionAsync([])` when empty.
- **Grid control:** `Controls/CardGrid.cs` (Skia), `Controls/CardGridRenderer.cs` (empty list clears to transparent).
- **Logging:** Search log for `[CollectionUI]` (see `DEBUG_COLLECTION_UI.md`). Log file: app's LocalApplicationData, `mtgfetch.log`.

## What to try next (for new chat)
1. **Android-only?** Confirm if the issue is only on Android (e.g. test on Windows if possible).
2. **Empty-state view not visible:** The empty state is a C#-built `Grid` + `VerticalStackLayout` with labels and `BackgroundColor = Background` (#121212). On Android, when this view is assigned as `ContentView.Content`, it may not be laid out or drawn (e.g. wrong parent, zero size, or native view order). Try:
   - Forcing layout: after `CollectionContentHost.Content = _emptyStateView`, call something like `_emptyStateView.Measure(...)` / `Arrange(...)` or trigger a layout pass.
   - Using XAML empty state instead of C#-built: define the empty state in XAML and swap between two named views so the same view isn’t reparented.
   - Wrapping in a `Border` or `Frame` with explicit `HeightRequest`/`WidthRequest` or `MinimumHeightRequest` so the empty state has non-zero size.
3. **Shell / container:** Check if the Shell or the tab content host draws a black background when the page content is a single child that’s swapped; try setting an explicit `BackgroundColor` on the Shell content area or the page root.
4. **Repro without clearing:** See if the black screen also appears when opening the app with an already-empty collection (no clear action), to see if it’s specific to the clear path or any empty state.

## Copy this into the new chat
```
Collection tab shows a black screen after clearing the collection (and sometimes after import) instead of the "Your collection is empty" UI. Logs show the ViewModel correctly sets IsCollectionEmpty=true and UpdateContentHostContent sets Content to EmptyState; the bug appears to be that the empty-state view doesn’t show on Android. See HANDOFF_COLLECTION_BLACK_SCREEN.md and DEBUG_COLLECTION_UI.md. Key files: CollectionPage.xaml/.cs, CollectionViewModel.cs, CardGrid/CardGridRenderer.
```
