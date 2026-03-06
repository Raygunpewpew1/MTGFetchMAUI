# Handoff: Collection/Decks black screen bug

## What happens
After **clearing the collection**, **import** (file picker), or **adding a card in deck**, the Collection or Decks UI shows a **black screen** instead of the empty state or content. Toast and ViewModel state are correct; the bug is rendering.

## What we know (from code + logcat)

### ViewModel / app logic
- `LoadCollectionAsync` and empty/has-data branches run correctly; `IsCollectionEmpty` is set on the main thread.
- We do **not** call `SetCollectionAsync([])` when empty (grid pipeline avoided).
- Collection page: empty state is **XAML** (`CollectionEmptyState`); when empty we **remove** `CardGrid` from the tree so no Skia view is present; when we have data we add it back.
- `CardGridRenderer`: when list is empty or canvas has zero size we clear to **theme background** (#121212), not transparent, so the canvas never leaves a black surface.

### Logcat findings (Android)
1. **Modal/dialog close**  
   When the Clear confirmation or another modal is dismissed: `Destroying surface` → `Changing focus` to MainActivity. We added a **post-modal invalidate** (~220 ms delay) to run after that transition; it did not fix the issue.

2. **Return from file picker (Import)**  
   - `IntermediateActivity` is shown briefly, then finishes.  
   - MainActivity is resumed; WindowManager sets visibility true.  
   - **"Start draw after previous draw not visible"** — the system requests one draw.  
   - First frame is produced and committed (layer 5238).  
   - That frame is drawn **while the window was previously not visible**; the view hierarchy may not be laid out/updated yet (e.g. Shell/tab content not ready), so **the first frame can be black**.  
   - Layer is then **"hidden!!"** during the transition reparent; transition animates to full screen.  
   - No second draw is requested when the transition finishes, so the black first frame can remain visible.

**Conclusion:** The first frame after resume (post–file picker or post–dialog) is drawn before MAUI content is ready; we need to **force a second draw** after the activity/window is visible and the transition has settled.

## What’s already been tried
- Content swap (C#-built empty state) → black.
- Visibility toggling (grid + empty state in XAML, `IsVisible` binding) → black.
- Putting grid on top (second child), deferred layout (120 ms, then 220 ms), post-modal invalidate.
- Removing `CardGrid` from the tree when empty (no Skia in tree).
- Skia clearing to theme background when empty/zero-size.
- Invalidating page root and content area after reload and after modal close.

None of these fixed the issue.

## Key files
- **Collection:** `Pages/CollectionPage.xaml` (empty state + grid; grid add/remove in code), `Pages/CollectionPage.xaml.cs` (`UpdateContentHostForEmptyState`, `RunContentLayoutPass`, `ScheduleDeferredContentLayoutPass`, `SchedulePostModalInvalidate`).
- **Deck detail:** `Pages/DeckDetailPage.xaml/.cs` (deferred layout 220 ms, invalidate root + `DeckDetailRoot` + `CommanderArtCanvas`); `ViewModels/DeckDetailViewModel.cs` (`ReloadCompleted` event).
- **Skia:** `Controls/CardGridRenderer.cs` (theme background when empty); `Controls/CardGrid.cs`.
- **Android:** `Platforms/Android/MainActivity.cs` (`WindowFocusGained` static event).
- **Logging:** `[CollectionUI]` in app log (see `DEBUG_COLLECTION_UI.md`).

## What to try next (for new chat)
1. **Force a second draw after window focus / activity resume**  
   In `MainActivity`, when `WindowFocusGained` fires (or in an activity lifecycle handler for resume), schedule a **delayed** invalidate (e.g. 150–250 ms) so it runs **after** the “Start draw after previous draw not visible” frame and after the transition. In that delayed run, on the main thread, get the current MAUI page (e.g. `Shell.Current?.CurrentPage` or the top page) and call `(page.Content as View)?.InvalidateMeasure()` and/or trigger a layout pass so the next frame redraws with correct content. Optionally also invalidate the Shell or window root so the whole tree is marked dirty.

2. **Confirm scope**  
   Reproduce on Windows if possible to see if this is Android-only. Capture logcat again when the black screen is visible (without switching apps) and look for `Not drawing`, `hidden`, or zero-size layers.

3. **Minimal repro**  
   After clear/import, temporarily replace the Collection content area with a single bright `Label` (e.g. “Empty state test”). If that label shows, the bug is in our empty-state or grid view; if the area stays black, the issue is higher (Shell/tab host or first-frame draw).

## Copy this into the new chat
```
Collection/Decks show a black screen after clear, import (file picker), or adding a card in deck. ViewModel and toasts are correct; the bug is rendering. Logcat shows: when returning from IntermediateActivity (file picker) or after modal close, MainActivity does "Start draw after previous draw not visible" and submits one frame; that first frame can be black because the view hierarchy isn't ready. We need to force a second draw after activity/window focus with a short delay. See HANDOFF_COLLECTION_BLACK_SCREEN.md. Key: MainActivity.WindowFocusGained, delayed invalidate of current page/Shell content; CollectionPage, DeckDetailPage, CardGridRenderer already have various mitigations tried.
```
