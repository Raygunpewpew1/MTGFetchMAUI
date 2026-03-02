namespace MTGFetchMAUI.Data;

/// <summary>
/// Centralized SQL query definitions.
/// Port of SQLQueries.pas.
/// </summary>
public static class SQLQueries
{
    // ============================================================================
    // SCHEMA DEFINITIONS
    // ============================================================================

    public const string CreateCollectionTable =
        """
        CREATE TABLE IF NOT EXISTS my_collection (
            card_uuid TEXT PRIMARY KEY,
            quantity INTEGER DEFAULT 1,
            date_added DATETIME DEFAULT CURRENT_TIMESTAMP,
            card_data BLOB,
            sort_order INTEGER DEFAULT 0
        )
        """;

    public const string CreateDecksTable =
        """
        CREATE TABLE IF NOT EXISTS Decks (
            Id INTEGER PRIMARY KEY AUTOINCREMENT,
            Name TEXT NOT NULL,
            Format TEXT NOT NULL,
            Description TEXT,
            CoverCardId TEXT,
            DateCreated DATETIME DEFAULT CURRENT_TIMESTAMP,
            DateModified DATETIME DEFAULT CURRENT_TIMESTAMP,
            CommanderId TEXT,
            CommanderName TEXT DEFAULT '',
            PartnerId TEXT,
            ColorIdentity TEXT
        )
        """;

    public const string DecksTableInfo = "PRAGMA table_info(Decks)";
    public const string DecksAddCommanderName = "ALTER TABLE Decks ADD COLUMN CommanderName TEXT DEFAULT ''";

    public const string CreateDeckCardsTable =
        """
        CREATE TABLE IF NOT EXISTS DeckCards (
            DeckId INTEGER,
            CardId TEXT,
            Quantity INTEGER DEFAULT 1,
            Section TEXT DEFAULT 'Main',
            DateAdded DATETIME DEFAULT CURRENT_TIMESTAMP,
            PRIMARY KEY (DeckId, CardId, Section),
            FOREIGN KEY (DeckId) REFERENCES Decks(Id) ON DELETE CASCADE
        )
        """;

    // ============================================================================
    // THUMBNAIL CACHE SCHEMA
    // ============================================================================

    //public const string CreateThumbnailCacheTable =
    //    """
    //    CREATE TABLE IF NOT EXISTS thumbnail_cache (
    //        cache_key TEXT PRIMARY KEY,
    //        scryfall_id TEXT NOT NULL,
    //        image_size TEXT NOT NULL,
    //        image_data BLOB NOT NULL,
    //        file_size INTEGER NOT NULL,
    //        created_at DATETIME DEFAULT CURRENT_TIMESTAMP,
    //        last_accessed DATETIME DEFAULT CURRENT_TIMESTAMP
    //    )
    //    """;

    //public const string CreateThumbnailIndexAccessed =
    //    "CREATE INDEX IF NOT EXISTS idx_thumb_accessed ON thumbnail_cache(last_accessed)";

    //public const string ThumbnailInsert =
    //    "INSERT OR REPLACE INTO thumbnail_cache " +
    //    "(cache_key, scryfall_id, image_size, image_data, file_size, " +
    //    "created_at, last_accessed) " +
    //    "VALUES (@cache_key, @scryfall_id, @image_size, @image_data, @file_size, " +
    //    "CURRENT_TIMESTAMP, CURRENT_TIMESTAMP)";

    //public const string ThumbnailGet =
    //    "SELECT image_data FROM thumbnail_cache WHERE cache_key = @cache_key";

    //public const string ThumbnailExists =
    //    "SELECT 1 FROM thumbnail_cache WHERE cache_key = @cache_key";

    //public const string ThumbnailUpdateAccess =
    //    "UPDATE thumbnail_cache SET last_accessed = CURRENT_TIMESTAMP " +
    //    "WHERE cache_key = @cache_key";

    //public const string ThumbnailDeleteByKey =
    //    "DELETE FROM thumbnail_cache WHERE cache_key = @cache_key";

    //public const string ThumbnailClear =
    //    "DELETE FROM thumbnail_cache";

    //public const string ThumbnailStats =
    //    "SELECT COUNT(*) AS cnt, COALESCE(SUM(file_size), 0) AS total_size " +
    //    "FROM thumbnail_cache";

    //public const string ThumbnailEvictLru =
    //    "DELETE FROM thumbnail_cache WHERE cache_key IN (" +
    //    "  SELECT cache_key FROM thumbnail_cache " +
    //    "  ORDER BY last_accessed ASC LIMIT @evict_count" +
    //    ")";

    // ============================================================================
    // PRICE DATABASE SCHEMA & QUERIES
    // ============================================================================

    public const string CreatePricesTable =
        """
        CREATE TABLE IF NOT EXISTS card_prices (
            uuid       TEXT NOT NULL,
            source     TEXT NOT NULL,
            provider   TEXT NOT NULL,
            price_type TEXT NOT NULL,
            finish     TEXT NOT NULL,
            currency   TEXT NOT NULL,
            price      REAL NOT NULL,
            PRIMARY KEY (uuid, source, provider, price_type, finish)
        )
        """;

    public const string CreatePriceHistoryTable =
        """
        CREATE TABLE IF NOT EXISTS card_price_history (
            uuid       TEXT NOT NULL,
            source     TEXT NOT NULL,
            provider   TEXT NOT NULL,
            price_type TEXT NOT NULL,
            finish     TEXT NOT NULL,
            date       TEXT NOT NULL,
            currency   TEXT NOT NULL,
            price      REAL NOT NULL,
            PRIMARY KEY (uuid, source, provider, price_type, finish, date)
        )
        """;

    public const string CreatePricesIndex =
        "CREATE INDEX IF NOT EXISTS idx_prices_uuid ON card_prices(uuid)";

    public const string CreatePriceHistoryIndex =
        "CREATE INDEX IF NOT EXISTS idx_history_uuid ON card_price_history(uuid)";

    public const string DropPricesIndex = "DROP INDEX IF EXISTS idx_prices_uuid";
    public const string DropPriceHistoryIndex = "DROP INDEX IF EXISTS idx_history_uuid";

    // Detects old wide-column schema (pre-refactor). Used for one-time migration.
    public const string PricesSchemaCheck =
        "SELECT COUNT(*) FROM pragma_table_info('card_prices') WHERE name = 'tcg_retail_normal'";

    // ATTACH-based sync from a downloaded AllPricesToday.sqlite (attached as 'today').
    // 'priceType' is the camelCase column name used by MTGJSON; we map it to snake_case locally.
    public const string PricesSyncFromAttached =
        """
        INSERT OR REPLACE INTO card_prices (uuid, source, provider, price_type, finish, currency, price)
        SELECT uuid, source, provider, priceType, finish, currency, price
        FROM today.prices
        WHERE price IS NOT NULL AND price > 0
        """;

    public const string PriceHistorySyncFromAttached =
        """
        INSERT OR IGNORE INTO card_price_history (uuid, source, provider, price_type, finish, date, currency, price)
        SELECT uuid, source, provider, priceType, finish, date, currency, price
        FROM today.prices
        WHERE price IS NOT NULL AND price > 0
        """;

    // Trim history older than N days. Use string.Format to inject the retention constant.
    public const string PriceHistoryTrimOld =
        "DELETE FROM card_price_history WHERE date < date('now', '-{0} days')";

    public const string PricesGetByUuid =
        "SELECT * FROM card_prices WHERE uuid = @uuid AND source = 'paper'";

    public const string PricesGetHistoryByUuid =
        "SELECT * FROM card_price_history WHERE uuid = @uuid AND source = 'paper' ORDER BY date ASC";

    public const string PricesCount =
        "SELECT COUNT(*) FROM card_prices";

    public const string PricesDeleteAll =
        "DELETE FROM card_prices";

    public const string PricesGetBulkByUuids =
        "SELECT * FROM card_prices WHERE uuid IN ({0}) AND source = 'paper'";

    // ============================================================================
    // CARD QUERIES
    // ============================================================================

    public const string SelectFullCard =
        """
        SELECT
            c.*,
            ci.scryfallId,
            s.name AS setName,
            s.keyruneCode,
            cl.alchemy,
            cl.brawl,
            cl.commander,
            cl.duel,
            cl.future,
            cl.gladiator,
            cl.historic,
            cl.legacy,
            cl.modern,
            cl.oathbreaker,
            cl.oldschool,
            cl.pauper,
            cl.paupercommander,
            cl.penny,
            cl.pioneer,
            cl.predh,
            cl.premodern,
            cl.standard,
            cl.standardbrawl,
            cl.timeless,
            cl.vintage,
            cp.cardKingdom,
            cp.cardKingdomFoil,
            cp.cardKingdomEtched,
            cp.cardmarket,
            cp.tcgplayer,
            cp.tcgplayerEtched
        FROM cards c
        LEFT JOIN cardIdentifiers ci ON c.uuid = ci.uuid
        LEFT JOIN sets s ON c.setCode = s.code
        LEFT JOIN cardLegalities cl ON c.uuid = cl.uuid
        LEFT JOIN cardPurchaseUrls cp ON c.uuid = cp.uuid
        """;

    public const string WhereUuidEquals = " WHERE c.uuid = @uuid";

    public const string SelectScryfallId =
        "SELECT scryfallId FROM cardIdentifiers WHERE uuid = @uuid";

    public const string SelectRulings =
        "SELECT date, text FROM cardRulings WHERE uuid = @uuid ORDER BY date";

    public const string SelectOtherFaces =
        "SELECT otherFaceIds FROM cards WHERE uuid = @uuid";

    public const string SelectCardByUuid =
        "SELECT * FROM cards WHERE uuid = @uuid";

    // ============================================================================
    // COLLECTION QUERIES
    // ============================================================================

    public const string CollectionGetQuantity =
        "SELECT quantity FROM my_collection WHERE card_uuid = @uuid";

    public const string CollectionUpdateQuantity =
        "UPDATE my_collection SET quantity = @qty, is_foil = @isFoil, is_etched = @isEtched WHERE card_uuid = @uuid";

    public const string CollectionInsertCard =
        "INSERT INTO my_collection (card_uuid, quantity, is_foil, is_etched, sort_order) VALUES (@uuid, @qty, @isFoil, @isEtched, (SELECT COALESCE(MAX(sort_order), 0) + 1 FROM my_collection))";

    public const string CollectionDeleteCard =
        "DELETE FROM my_collection WHERE card_uuid = @uuid";

    public const string CollectionTableInfo = "PRAGMA table_info(my_collection)";
    public const string CollectionAddSortOrder = "ALTER TABLE my_collection ADD COLUMN sort_order INTEGER DEFAULT 0";
    public const string CollectionSeedSortOrder = "UPDATE my_collection SET sort_order = rowid WHERE sort_order = 0";
    public const string CollectionAddIsFoil = "ALTER TABLE my_collection ADD COLUMN is_foil INTEGER NOT NULL DEFAULT 0";
    public const string CollectionAddIsEtched = "ALTER TABLE my_collection ADD COLUMN is_etched INTEGER NOT NULL DEFAULT 0";

    public const string CollectionGetAll =
        "SELECT card_uuid, quantity, date_added, sort_order, is_foil, is_etched FROM my_collection ORDER BY sort_order ASC, date_added DESC";

    public const string CollectionReorderItem =
        "UPDATE my_collection SET sort_order = @sortOrder WHERE card_uuid = @uuid";

    public const string CollectionCheckExists =
        "SELECT 1 FROM my_collection WHERE card_uuid = @uuid";

    // ============================================================================
    // DECK QUERIES
    // ============================================================================

    public const string DeckInsert =
        "INSERT INTO Decks (Name, Format, Description, CoverCardId, CommanderId, CommanderName, PartnerId, ColorIdentity) VALUES (@Name, @Format, @Description, @CoverCardId, @CommanderId, @CommanderName, @PartnerId, @ColorIdentity)";

    public const string DeckUpdate =
        "UPDATE Decks SET Name = @Name, Description = @Description, CoverCardId = @CoverCardId, DateModified = CURRENT_TIMESTAMP, CommanderId = @CommanderId, CommanderName = @CommanderName, PartnerId = @PartnerId, ColorIdentity = @ColorIdentity WHERE Id = @Id";

    public const string DeckGetLastId = "SELECT last_insert_rowid() AS Id";

    public const string DeckGet = "SELECT * FROM Decks WHERE Id = @Id";

    public const string DeckGetAll = "SELECT * FROM Decks ORDER BY DateModified DESC";

    public const string DeckDelete = "DELETE FROM Decks WHERE Id = @Id";

    public const string DeckDeleteCards = "DELETE FROM DeckCards WHERE DeckId = @Id";

    public const string DeckAddCard =
        """
        INSERT OR REPLACE INTO DeckCards (DeckId, CardId, Quantity, Section, DateAdded)
        VALUES (@DeckId, @CardId, @Quantity, @Section, @DateAdded)
        """;

    public const string DeckRemoveCard =
        "DELETE FROM DeckCards WHERE DeckId = @DeckId AND CardId = @CardId AND Section = @Section";

    public const string DeckUpdateCardQuantity =
        "UPDATE DeckCards SET Quantity = @Quantity WHERE DeckId = @DeckId AND CardId = @CardId AND Section = @Section";

    public const string DeckGetCards =
        "SELECT * FROM DeckCards WHERE DeckId = @DeckId";

    // ============================================================================
    // SEARCH HELPER FRAGMENTS
    // ============================================================================

    public const string SqlWhere = " WHERE ";
    public const string SqlAnd = " AND ";
    public const string SqlOrderBy = "ORDER BY ";
    public const string SqlDesc = " DESC";
    public const string SqlLimit = "LIMIT ";
    public const string SqlOffset = " OFFSET ";

    public const string CountWrapper = "SELECT COUNT(*) as cnt FROM (";
    public const string CountField = "cnt";

    public const string BaseCards =
        """
        SELECT c.*, ci.scryfallId, s.name as setName,
        cl.alchemy, cl.brawl, cl.commander, cl.duel, cl.future, cl.gladiator,
        cl.historic, cl.legacy, cl.modern, cl.oathbreaker, cl.oldschool,
        cl.pauper, cl.paupercommander, cl.penny, cl.pioneer, cl.predh,
        cl.premodern, cl.standard, cl.standardbrawl, cl.timeless, cl.vintage,
        cp.cardKingdom, cp.cardKingdomFoil, cp.cardKingdomEtched,
        cp.cardmarket, cp.tcgplayer, cp.tcgplayerEtched
        FROM cards c
        LEFT JOIN cardIdentifiers ci ON c.uuid = ci.uuid
        LEFT JOIN sets s ON c.setCode = s.code
        LEFT JOIN cardLegalities cl ON c.uuid = cl.uuid
        LEFT JOIN cardPurchaseUrls cp ON c.uuid = cp.uuid
        """;

    public const string BaseCollection =
        "SELECT c.*, mc.quantity FROM cards c INNER JOIN my_collection mc ON c.uuid = mc.card_uuid";

    public const string BaseSets = "SELECT * FROM sets";

    // Where condition fragments
    public const string CondName = "c.name LIKE @";
    public const string CondText = "c.text LIKE @";
    public const string CondType = "c.type LIKE @";
    public const string CondUuid = "c.uuid = @";
    public const string CondColors = "c.colors LIKE @";
    public const string CondManaValue = "c.manaValue = @";
    public const string CondManaValueBetween = "c.manaValue BETWEEN @";
    public const string CondPower = "c.power = @";
    public const string CondToughness = "c.toughness = @";
    public const string CondArtist = "c.artist LIKE @";
    public const string CondInCollection = "EXISTS (SELECT 1 FROM my_collection WHERE card_uuid = c.uuid)";
    public const string CondSidePrimary = "(c.side = 'a' OR c.side IS NULL)";
    public const string CondNoVariations = "(c.variations IS NULL OR c.variations = '' OR c.variations = '[]')";
}
