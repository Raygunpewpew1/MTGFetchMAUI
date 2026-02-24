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
            PartnerId TEXT,
            ColorIdentity TEXT
        )
        """;

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

    public const string CreateThumbnailCacheTable =
        """
        CREATE TABLE IF NOT EXISTS thumbnail_cache (
            cache_key TEXT PRIMARY KEY,
            scryfall_id TEXT NOT NULL,
            image_size TEXT NOT NULL,
            image_data BLOB NOT NULL,
            file_size INTEGER NOT NULL,
            created_at DATETIME DEFAULT CURRENT_TIMESTAMP,
            last_accessed DATETIME DEFAULT CURRENT_TIMESTAMP
        )
        """;

    public const string CreateThumbnailIndexAccessed =
        "CREATE INDEX IF NOT EXISTS idx_thumb_accessed ON thumbnail_cache(last_accessed)";

    public const string ThumbnailInsert =
        "INSERT OR REPLACE INTO thumbnail_cache " +
        "(cache_key, scryfall_id, image_size, image_data, file_size, " +
        "created_at, last_accessed) " +
        "VALUES (@cache_key, @scryfall_id, @image_size, @image_data, @file_size, " +
        "CURRENT_TIMESTAMP, CURRENT_TIMESTAMP)";

    public const string ThumbnailGet =
        "SELECT image_data FROM thumbnail_cache WHERE cache_key = @cache_key";

    public const string ThumbnailExists =
        "SELECT 1 FROM thumbnail_cache WHERE cache_key = @cache_key";

    public const string ThumbnailUpdateAccess =
        "UPDATE thumbnail_cache SET last_accessed = CURRENT_TIMESTAMP " +
        "WHERE cache_key = @cache_key";

    public const string ThumbnailDeleteByKey =
        "DELETE FROM thumbnail_cache WHERE cache_key = @cache_key";

    public const string ThumbnailClear =
        "DELETE FROM thumbnail_cache";

    public const string ThumbnailStats =
        "SELECT COUNT(*) AS cnt, COALESCE(SUM(file_size), 0) AS total_size " +
        "FROM thumbnail_cache";

    public const string ThumbnailEvictLru =
        "DELETE FROM thumbnail_cache WHERE cache_key IN (" +
        "  SELECT cache_key FROM thumbnail_cache " +
        "  ORDER BY last_accessed ASC LIMIT @evict_count" +
        ")";

    // ============================================================================
    // PRICE DATABASE SCHEMA & QUERIES
    // ============================================================================

    public const string CreatePricesTable =
        """
        CREATE TABLE IF NOT EXISTS card_prices (
            card_uuid TEXT PRIMARY KEY,
            tcg_retail_normal REAL DEFAULT 0,
            tcg_retail_foil REAL DEFAULT 0,
            tcg_buylist_normal REAL DEFAULT 0,
            tcg_currency TEXT DEFAULT 'USD',
            cm_retail_normal REAL DEFAULT 0,
            cm_retail_foil REAL DEFAULT 0,
            cm_buylist_normal REAL DEFAULT 0,
            cm_currency TEXT DEFAULT 'EUR',
            ck_retail_normal REAL DEFAULT 0,
            ck_retail_foil REAL DEFAULT 0,
            ck_buylist_normal REAL DEFAULT 0,
            ck_currency TEXT DEFAULT 'USD',
            mp_retail_normal REAL DEFAULT 0,
            mp_retail_foil REAL DEFAULT 0,
            mp_buylist_normal REAL DEFAULT 0,
            mp_currency TEXT DEFAULT 'USD',
            last_updated DATETIME DEFAULT CURRENT_TIMESTAMP
        )
        """;

    public const string CreatePriceHistoryTable =
        """
        CREATE TABLE IF NOT EXISTS card_price_history (
            card_uuid TEXT,
            price_date INTEGER,
            vendor TEXT,
            price_type TEXT,
            price_value REAL,
            PRIMARY KEY (card_uuid, price_date, vendor, price_type)
        )
        """;

    public const string CreatePricesIndex =
        "CREATE INDEX IF NOT EXISTS idx_prices_uuid ON card_prices(card_uuid)";

    public const string CreatePriceHistoryIndex =
        "CREATE INDEX IF NOT EXISTS idx_history_uuid ON card_price_history(card_uuid)";

    // NOTE: PricesInsert is now generated dynamically for bulk insert, but we keep a template here if needed
    // or we can remove it. For now, we will use dynamic SQL generation in the importer.

    public const string PricesGetByUuid =
        "SELECT * FROM card_prices WHERE card_uuid = @uuid";

    public const string PricesGetHistoryByUuid =
        "SELECT * FROM card_price_history WHERE card_uuid = @uuid ORDER BY price_date ASC";

    public const string PricesCount =
        "SELECT COUNT(*) FROM card_prices";

    public const string PricesDeleteAll =
        "DELETE FROM card_prices";

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
            cl.vintage
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
        "UPDATE my_collection SET quantity = @qty WHERE card_uuid = @uuid";

    public const string CollectionInsertCard =
        "INSERT INTO my_collection (card_uuid, quantity, sort_order) VALUES (@uuid, @qty, (SELECT COALESCE(MAX(sort_order), 0) + 1 FROM my_collection))";

    public const string CollectionDeleteCard =
        "DELETE FROM my_collection WHERE card_uuid = @uuid";

    public const string CollectionGetAll =
        "SELECT card_uuid, quantity, date_added, sort_order FROM my_collection ORDER BY sort_order ASC, date_added DESC";

    public const string CollectionReorderItem =
        "UPDATE my_collection SET sort_order = @sortOrder WHERE card_uuid = @uuid";

    public const string CollectionCheckExists =
        "SELECT 1 FROM my_collection WHERE card_uuid = @uuid";

    // ============================================================================
    // DECK QUERIES
    // ============================================================================

    public const string DeckInsert =
        "INSERT INTO Decks (Name, Format, Description, CoverCardId, CommanderId, PartnerId, ColorIdentity) VALUES (@Name, @Format, @Description, @CoverCardId, @CommanderId, @PartnerId, @ColorIdentity)";

    public const string DeckUpdate =
        "UPDATE Decks SET Name = @Name, Description = @Description, CoverCardId = @CoverCardId, DateModified = CURRENT_TIMESTAMP, CommanderId = @CommanderId, PartnerId = @PartnerId, ColorIdentity = @ColorIdentity WHERE Id = @Id";

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
        cl.premodern, cl.standard, cl.standardbrawl, cl.timeless, cl.vintage
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
