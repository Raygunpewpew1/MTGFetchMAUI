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

    public static string UpdatedQuantity(string itemName) => $"Updated {itemName} quantity.";

    public static string AddedLandsToMain(int count) => $"Added {count} basic lands to Main.";

    public static string FoundCards(int count) => count == 0 ? NoCardsFound : $"Found {count} cards";

    public static string CardsAddedToSection(int quantity, string cardName, string section) =>
        $"{quantity}× {cardName} added to {section}.";

    // ── Toasts (short feedback) ────────────────────────────────────────

    public static string ImportedDecksToast(int deckCount, int cardCount) =>
        $"Imported {deckCount} deck{(deckCount == 1 ? "" : "s")} ({cardCount} cards).";

    public const string DeckImportFailed = "Deck import failed.";
    public const string LoadingDeckList = "Loading deck list...";
    public const string ImportingMtgJsonDeck = "Importing deck...";
    public static string MtgJsonDeckImportedToast(string deckName, int cardCount) =>
        $"Imported \"{deckName}\" ({cardCount} cards).";
    public const string MtgJsonDeckImportFailed = "Could not import deck.";
    public const string NoDecksToExport = "No decks to export.";
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
}
