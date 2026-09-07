namespace AetherVault.Constants;

/// <summary>
/// Centralized user-facing status and error messages for consistency and easier localization.
/// Use for StatusMessage and toast/alert text shown in the UI.
/// </summary>
public static class UserMessages
{
    // ── Errors (operation failed) ─────────────────────────────────────

    public static string LoadFailed(string detail) => $"Load failed: {detail}";
    public static string DeleteFailed(string detail) => $"Delete failed: {detail}";
    public static string ExportFailed(string detail) => $"Export failed: {detail}";
    public static string ImportFailed(string detail) => $"Import failed: {detail}";
    public static string SearchFailed(string detail) => string.IsNullOrEmpty(detail) ? "Search failed." : $"Search failed: {detail}";

    /// <summary>Database file is invalid or unreadable.</summary>
    public const string DatabaseCorrupted = "Database is corrupted. Please retry the download.";

    /// <summary>Connection to the database could not be opened.</summary>
    public const string FailedToOpenDatabase = "Failed to open database.";

    /// <summary>Network download of the database failed.</summary>
    public const string DownloadFailed = "Download failed. Please check your internet connection.";

    /// <summary>Deck was not found (e.g. deleted elsewhere).</summary>
    public const string DeckNotFound = "Deck not found.";

    /// <summary>Undo last add to deck failed.</summary>
    public static string CouldNotUndoLastAdd(string? detail = null) =>
        string.IsNullOrEmpty(detail) ? "Could not undo last add." : $"Could not undo last add: {detail}";

    /// <summary>Generic "could not" prefix for card/deck operations.</summary>
    public static string CouldNotAddCardToDeck(string? detail = null) =>
        string.IsNullOrEmpty(detail) ? "Could not add card to deck." : detail;

    public static string CouldNotSetCommander(string? detail = null) =>
        string.IsNullOrEmpty(detail) ? "Could not set commander." : detail;

    public static string CouldNotUpdateQuantity(string? detail = null) =>
        string.IsNullOrEmpty(detail) ? "Could not update quantity." : detail;

    /// <summary>Card details could not be loaded (e.g. when selecting from picker).</summary>
    public const string CouldNotLoadCardDetails = "Could not load card details. Please try again.";

    // ── Loading / progress ────────────────────────────────────────────

    public const string CheckingDatabase = "Checking database...";
    public const string DownloadingDatabase = "Downloading database...";
    public const string Initializing = "Initializing...";
    public const string LoadingDeck = "Loading deck...";
    public const string LoadingCollection = "Loading collection...";
    public const string LoadingCollectionPrices = "Preparing collection prices...";
    public const string Searching = "Searching...";
    public const string ImportingCollection = "Importing collection...";
    public const string ImportingDecks = "Importing decks...";
    public const string ExportingCollection = "Exporting collection...";
    public const string ExportingDecks = "Exporting decks...";
    public const string ExportingDeck = "Exporting deck...";
    public const string SuggestingLands = "Suggesting lands...";
    public const string ClearingCache = "Clearing cache...";

    // ── Success / neutral ─────────────────────────────────────────────

    public const string DatabaseReady = "Database ready";
    public const string CacheCleared = "Cache cleared";
    public const string EnterSearchTerm = "Enter a search term";
    public const string DatabaseNotFound = "Database not found. Please download.";
    public const string DatabaseNotConnected = "Database not connected.";

    /// <summary>Search or picker returned zero cards.</summary>
    public const string NoCardsFound = "No cards found.";

    /// <summary>Land suggestion added no new lands.</summary>
    public const string NoLandsAdded = "No lands were added (deck may already have enough lands).";

    /// <summary>Generic error with detail (e.g. for StatsViewModel).</summary>
    public static string Error(string detail) => $"Error: {detail}";

    /// <summary>Clear status (empty string).</summary>
    public const string StatusClear = "";

    // ── Formatted status (deck/card operations) ─────────────────────────

    public static string UndidLastAdd(int quantity, string cardName, string section) =>
        $"Undid last add: removed {quantity}× {cardName} from {section}.";

    /// <summary>Deck editor undo succeeded.</summary>
    public const string DeckEditUndone = "Undone.";

    /// <summary>Deck editor could not apply undo mutations.</summary>
    public static string CouldNotUndoDeckEdit(string? detail = null) =>
        string.IsNullOrEmpty(detail) ? "Could not undo." : $"Could not undo: {detail}";

    public static string UpdatedQuantity(string itemName) => $"Updated {itemName} quantity.";

    public static string AddedLandsToMain(int count) => $"Added {count} basic lands to Main.";

    public static string FoundCards(int count) => count == 0 ? NoCardsFound : $"Found {count} cards";

    public static string CardsAddedToSection(int quantity, string cardName, string section) =>
        $"{quantity}× {cardName} added to {section}.";

    // ── Toasts (short feedback) ────────────────────────────────────────

    public static string ImportedDecksToast(int deckCount, int cardCount) =>
        $"Imported {deckCount} deck{(deckCount == 1 ? "" : "s")} ({cardCount} cards).";

    public const string DeckImportFailed = "Deck import failed.";

    /// <summary>Moxfield-only URL import; ManaBox needs export/paste.</summary>
    public const string ImportDeckFromUrlTitle = "Import from link";

    public const string ImportDeckFromUrlPrompt = "Paste a public Moxfield deck URL.";

    public const string ManaBoxDeckUrlHint =
        "ManaBox links are not supported in-app yet. In ManaBox, open the menu (⋯) → Export, then import that file or use Paste list here.";

    public const string UnsupportedDeckUrlHint =
        "Unsupported link. Use a Moxfield deck URL, or Import file / Paste list.";
    public const string LoadingDeckList = "Loading deck list...";
    public const string ImportingMtgJsonDeck = "Importing deck...";
    public static string MtgJsonDeckImportedToast(string deckName, int cardCount) =>
        $"Imported \"{deckName}\" ({cardCount} cards).";
    public const string MtgJsonDeckImportFailed = "Could not import deck.";
    public const string NoDecksToExport = "No decks to export.";

    /// <summary>Decks hub — action sheet title for import/export/discovery.</summary>
    public const string DeckToolsActionSheetTitle = "Import or export decks";

    public const string DeckToolsImportFile = "Import deck file";
    public const string DeckToolsImportLink = "Import from link";
    public const string DeckToolsPasteList = "Paste decklist";
    public const string DeckToolsBrowseMtgJson = "Browse Preconstructed Decks";
    public const string DeckToolsExportAll = "Export all decks";

    /// <summary>Deck detail — overflow menu title.</summary>
    public const string DeckDetailMoreMenuTitle = "Deck actions";

    public const string DeckDetailMoreImportCsv = "Import into this deck";
    public const string DeckDetailMoreExportCsv = "Export this deck";

    public const string DeckDetailMoreBuyCards = "Buy cards…";

    public const string DeckBuySheetTitle = "Buy cards";
    public const string DeckBuyCopyMassEntry = "Copy list for TCGPlayer Mass Entry";
    public const string DeckBuyOpenTcgMassEntry = "Open TCGPlayer Mass Entry";
    public const string DeckBuyCopiedTitle = "Copied";
    public const string DeckBuyCopiedBody = "Paste the clipboard into TCGPlayer Mass Entry (Magic).";

    /// <summary>Deck detail / deck list — custom tile art on the Decks tab.</summary>
    public const string DeckDetailMoreHubPicture = "Deck hub picture…";

    public const string DeckHubPictureSheetTitle = "Deck hub picture";
    public const string DeckHubPictureChooseCard = "Choose card…";
    public const string DeckHubPictureClear = "Clear custom picture";
    public const string DeckHubPicturePickerTitle = "Choose deck picture";
    public const string DeckHubPictureNothingToClear = "No custom deck picture is set.";
    public const string DeckHubPictureButtonSemanticHint = "Deck tile picture — search for a card or clear custom art";

    /// <summary>Deck editor — floating add affordance (opens add-cards flow).</summary>
    public const string DeckDetailFabAddCards = "Add cards";

    /// <summary>Toast when a Trim next-step chip turns on selection mode.</summary>
    public const string DeckTrimSelectionHint = "Select cards, then use the toolbar to remove or move them.";

    /// <summary>Commander header slot — action sheet title and entries.</summary>
    public const string DeckCommanderActionsTitle = "Commander";
    public const string DeckChangeCommander = "Change commander";
    public const string DeckRemoveCommander = "Remove commander";
    public const string DeckChooseCommander = "Choose a commander";

    /// <summary>Add-cards modal — staging summary when empty.</summary>
    public const string DeckAddStagingEmpty = "Nothing staged.";

    public static string DeckAddStagingSummary(int distinctTypes, int totalCards) =>
        $"{distinctTypes} · {totalCards} cards";

    public const string DeckAddApplyToMain = "Add to main";

    public const string DeckAddApplyToSideboard = "Add to sideboard";

    public const string DeckAddTargetPickerTitle = "Add to";

    public const string DeckAddTargetMainShort = "Main";

    public const string DeckAddTargetSideboardShort = "Sideboard";

    public const string DeckAddHintCommander = "Tap a card to set commander.";

    public const string DeckAddRowHintStage = "Tap to stage or unstage.";

    public const string DeckAddRowHintCommander = "Tap to set commander.";

    public const string DeckAddResultsEmpty =
        "No results yet. Type to search, or tap a chip for suggestions, deck themes, and popular lists.";

    /// <summary>Add-cards — chip that loads scored suggestions for this deck.</summary>
    public const string DeckAddSuggestionsChip = "For your deck";

    /// <summary>Add-cards — caption shown above results when they came from the suggestion engine.</summary>
    public const string DeckAddSuggestionsCaption = "Suggested for this deck — tap a card to pick it";

    /// <summary>Add-cards — strategy chip label (opens strategy action sheet).</summary>
    public static string DeckAddStrategyChip(string strategyName) => $"Strategy: {strategyName}";

    /// <summary>Add-cards — strategy action sheet title.</summary>
    public const string DeckAddStrategySheetTitle = "Suggestion strategy";

    /// <summary>Add-cards — collapsed header for the staged card list.</summary>
    public const string DeckAddStagingReviewTitle = "Review staged cards";

    public const string DeckStatsSynergySubtypesTitle = "Top subtypes (main + commander)";

    public const string DeckStatsSynergyKeywordsTitle = "Top keywords";

    public const string DeckStatsSynergyRolesTitle = "Deck roles (heuristic)";

    public const string DeckAddSynergyChipsTitle = "Search by deck theme";

    public const string DeckAddClearThemeFilter = "Clear filter";

    public const string DeckAddQuickListsTitle = "Popular lists";

    public const string DeckAddListFilterPrefix = "List: ";

    public const string DeckAddThemeFilterActiveCaption = "Theme filter active";

    public const string DeckAddStrategyTitle = "Deck strategy (suggestions)";

    public const string DeckAddLoadSuggestions = "Suggest cards";

    public const string DeckAddSuggestionsNeedCommander = "Set a commander to load suggestions.";

    public const string DeckAddSuggestionsNone = "No suggestions match right now. Try another strategy or add cards first.";

    public const string DeckGridCardActionsTitle = "Card";

    public const string DeckGridMoveToSideboard = "Move to sideboard";

    public const string DeckGridMoveToMain = "Move to main";

    public const string DeckGridRemoveCard = "Remove from deck";

    public const string DeckGridViewDetails = "View details";

    public const string DeckDataTruthHelpTitle = "About this data";

    public const string ValidationDetailsTitle = "Validation details";

    public const string ValidationDetailsButton = "Details";

    /// <summary>Shown when validation returns an error with no message text.</summary>
    public const string DeckValidationUnknownError = "Validation error.";

    public const string AddCommander = "Add commander";
    public const string NothingToExport = "Nothing to export.";
    public const string ExportFailedToast = "Export failed.";

    public static string CardAddedToCollection(int quantity, string cardName) =>
        quantity > 0 ? $"{quantity}x {cardName} in collection" : $"{cardName} removed from collection";

    public static string CardRemovedFromCollection(string cardName) => $"{cardName} removed from collection";

    public static string CardAddedToDeck(int quantity, string cardName, string deckName) =>
        $"{quantity}× {cardName} added to {deckName}.";

    // ── Dialog titles and messages ─────────────────────────────────────

    public const string UpdateAvailableTitle = "Update Available";
    public static string UpdateAvailableMessage(string version) =>
        $"A new database version ({version}) is available. Would you like to download it?";

    public const string DatabaseErrorTitle = "Database Error";
    public const string DatabaseErrorMessage = "The local card database appears to be corrupted. Download a fresh copy?";

    public const string DownloadFailedTitle = "Download Failed";
    public const string DownloadFailedContinueMessage = "Could not download the update. Continue with existing database?";

    public const string RemoveTitle = "Remove";
    public static string RemoveFromCollectionMessage(string cardName) => $"Remove {cardName} from collection?";

    public const string ClearCacheTitle = "Clear Cache";
    public const string ClearCacheMessage = "Clear all cached card images?";

    public const string NoDeckTitle = "No Deck";
    public const string PleaseSelectDeck = "Please select a deck.";

    public const string RenameDeckTitle = "Rename deck";
    public const string RenameDeckPrompt = "Enter a new name:";

    public const string DeleteDeckTitle = "Delete deck";
    public static string DeleteDeckMessage(string deckName) => $"Delete \"{deckName}\"? This cannot be undone.";

    public const string ClearCollectionTitle = "Clear collection";
    public const string ClearCollectionMessage = "Remove all cards from your collection? This cannot be undone.";

    // ── Card detail: save / share ───────────────────────────────────────

    public const string SaveCardImageSuccess = "Saved card art to Photos (AetherVault folder).";
    public const string SaveCardImageFailed = "Could not save card art.";
    public const string SaveCardImageNoId = "No image available for this card.";
    public const string ShareCardFailed = "Could not open share.";
}
