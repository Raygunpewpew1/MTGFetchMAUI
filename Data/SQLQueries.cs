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
            card_data BLOB
        )
        """;

    public const string CreateCommanderDecksTable =
        """
        CREATE TABLE IF NOT EXISTS Commander_decks (
            deck_id INTEGER PRIMARY KEY AUTOINCREMENT,
            deck_name TEXT NOT NULL,
            commander_uuid TEXT NOT NULL,
            partner_uuid TEXT,
            format TEXT DEFAULT 'Commander',
            color_identity TEXT,
            archetype TEXT,
            power_level INTEGER DEFAULT 5,
            total_price REAL DEFAULT 0,
            date_created DATETIME DEFAULT CURRENT_TIMESTAMP,
            date_modified DATETIME DEFAULT CURRENT_TIMESTAMP,
            notes TEXT
        )
        """;

    public const string CreateCommanderDeckCardsTable =
        """
        CREATE TABLE IF NOT EXISTS Commander_deck_cards (
            deck_id INTEGER,
            card_uuid TEXT,
            quantity INTEGER DEFAULT 1,
            category TEXT,
            is_commander BOOLEAN DEFAULT 0,
            FOREIGN KEY (deck_id) REFERENCES Commander_decks(deck_id),
            PRIMARY KEY (deck_id, card_uuid)
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

    public const string CreatePricesIndex =
        "CREATE INDEX IF NOT EXISTS idx_prices_uuid ON card_prices(card_uuid)";

    public const string PricesInsert =
        """
        INSERT OR REPLACE INTO card_prices (
            card_uuid,
            tcg_retail_normal, tcg_retail_foil, tcg_buylist_normal, tcg_currency,
            cm_retail_normal, cm_retail_foil, cm_buylist_normal, cm_currency,
            ck_retail_normal, ck_retail_foil, ck_buylist_normal, ck_currency,
            mp_retail_normal, mp_retail_foil, mp_buylist_normal, mp_currency,
            last_updated
        ) VALUES (
            @uuid,
            @tcg_rn, @tcg_rf, @tcg_bn, @tcg_cur,
            @cm_rn, @cm_rf, @cm_bn, @cm_cur,
            @ck_rn, @ck_rf, @ck_bn, @ck_cur,
            @mp_rn, @mp_rf, @mp_bn, @mp_cur,
            CURRENT_TIMESTAMP
        )
        """;

    public const string PricesGetByUuid =
        "SELECT * FROM card_prices WHERE card_uuid = @uuid";

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
        "INSERT INTO my_collection (card_uuid, quantity) VALUES (@uuid, @qty)";

    public const string CollectionDeleteCard =
        "DELETE FROM my_collection WHERE card_uuid = @uuid";

    public const string CollectionGetAll =
        "SELECT card_uuid, quantity, date_added FROM my_collection ORDER BY date_added DESC";

    public const string CollectionCheckExists =
        "SELECT 1 FROM my_collection WHERE card_uuid = @uuid";

    // ============================================================================
    // COMMANDER DECK QUERIES
    // ============================================================================

    public const string DeckInsert =
        "INSERT INTO Commander_decks (deck_name, commander_uuid, color_identity) VALUES (@name, @commander, @colors)";

    public const string DeckGetLastId =
        "SELECT last_insert_rowid() AS deck_id";

    public const string DeckInsertCommander =
        "INSERT INTO Commander_deck_cards (deck_id, card_uuid, quantity, is_commander) VALUES (@deck_id, @card_uuid, 1, 1)";

    public const string DeckSelectWithNames =
        """
        SELECT d.*, c.name AS commander_name, p.name AS partner_name
        FROM Commander_decks d
        LEFT JOIN cards c ON d.commander_uuid = c.uuid
        LEFT JOIN cards p ON d.partner_uuid = p.uuid
        WHERE d.deck_id = @deck_id
        """;

    public const string DeckSelectCards =
        """
        SELECT dc.card_uuid, dc.quantity, dc.category, c.name
        FROM Commander_deck_cards dc
        JOIN cards c ON dc.card_uuid = c.uuid
        WHERE dc.deck_id = @deck_id AND dc.is_commander = 0
        ORDER BY c.name
        """;

    public const string DeckGetAllIds =
        "SELECT deck_id FROM Commander_decks ORDER BY date_modified DESC";

    public const string DeckDelete =
        "DELETE FROM Commander_decks WHERE deck_id = @deck_id";

    public const string DeckDeleteCards =
        "DELETE FROM Commander_deck_cards WHERE deck_id = @deck_id";

    public const string DeckAddCard =
        """
        INSERT OR REPLACE INTO Commander_deck_cards
        (deck_id, card_uuid, quantity, category)
        VALUES (@deck_id, @card_uuid, @quantity, @category)
        """;

    public const string DeckRemoveCard =
        "DELETE FROM Commander_deck_cards WHERE deck_id = @deck_id AND card_uuid = @card_uuid AND is_commander = 0";

    public const string DeckCountCards =
        "SELECT SUM(quantity) AS total FROM Commander_deck_cards WHERE deck_id = @deck_id";

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
    public const string CondNoVariations = "(c.variations IS NULL OR c.variations = '[]')";
}
