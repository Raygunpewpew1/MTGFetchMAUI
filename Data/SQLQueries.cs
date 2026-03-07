namespace AetherVault.Data;

/// <summary>
/// Centralized SQL query and schema definitions. All SQL used by repositories lives here so we avoid
/// scattered string literals and keep parameterized queries in one place. Never build SQL by concatenating user input.
/// </summary>
public static class SQLQueries
{
    // ============================================================================
    // SCHEMA DEFINITIONS (Collection DB — run on first connect)
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

    public const string SelectTokenFields =
        """
        SELECT
            artist, artistIds, asciiName, attractionLights, availability, boosterTypes,
            borderColor, colorIdentity, colorIndicator, colors, edhrecSaltiness,
            faceName, finishes, flavorName, flavorText, frameEffects, frameVersion,
            isFullArt, isFunny, isOversized, isPromo, isReprint, isTextless,
            keywords, language, layout, manaCost, name, number, orientation,
            originalText, otherFaceIds, power, printedType, producedMana,
            promoTypes, relatedCards, securityStamp, setCode, side, signature,
            sourceProducts, subtypes, supertypes, text, toughness, type, types,
            uuid, watermark
        FROM tokens
        """;

    public const string SelectTokenIdentifierFields =
        """
        SELECT
            uuid, scryfallId, scryfallOracleId, scryfallIllustrationId, scryfallCardBackId,
            mcmId, mcmMetaId, mtgArenaId, mtgoId, mtgoFoilId, multiverseId,
            tcgplayerProductId, tcgplayerEtchedProductId, tcgplayerAlternativeFoilProductId,
            cardKingdomId, cardKingdomFoilId, cardKingdomEtchedId, cardsphereId,
            cardsphereFoilId, deckboxId, mtgjsonFoilVersionId, mtgjsonNonFoilVersionId,
            mtgjsonV4Id
        FROM tokenIdentifiers
        """;

    public const string SelectTokenByUuid = SelectTokenFields + " WHERE uuid = @uuid";
    public const string SelectTokensBySetCode = SelectTokenFields + " WHERE setCode = @setCode";
    public const string SelectTokenIdentifierByUuid = SelectTokenIdentifierFields + " WHERE uuid = @uuid";


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

    // Insert history only for cards in the user's collection (col = attached collection DB).
    public const string PriceHistorySyncCollectionOnly =
        """
        INSERT OR IGNORE INTO card_price_history (uuid, source, provider, price_type, finish, date, currency, price)
        SELECT p.uuid, p.source, p.provider, p.priceType, p.finish, p.date, p.currency, p.price
        FROM today.prices p
        WHERE p.price IS NOT NULL AND p.price > 0
          AND p.uuid IN (SELECT card_uuid FROM col.my_collection)
        """;

    // Remove existing history for cards not in the user's collection (col = attached collection DB).
    public const string PriceHistoryDeleteNonCollection =
        "DELETE FROM card_price_history WHERE uuid NOT IN (SELECT card_uuid FROM col.my_collection)";

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

    /// <summary>Cards + tokens for CSV import lookup (name/set/number/scryfallId).</summary>
    public const string SelectImportLookupRowsCards =
        """
        SELECT
            c.uuid AS UUID,
            c.name AS Name,
            c.faceName AS FaceName,
            c.setCode AS SetCode,
            s.name AS SetName,
            c.number AS Number,
            ci.scryfallId AS ScryfallId
        FROM cards c
        LEFT JOIN cardIdentifiers ci ON c.uuid = ci.uuid
        LEFT JOIN sets s ON c.setCode = s.code
        WHERE c.side = 'a' OR c.side IS NULL
        """;

    public const string SelectImportLookupRowsTokens =
        """
        SELECT
            c.uuid AS UUID,
            c.name AS Name,
            c.faceName AS FaceName,
            c.setCode AS SetCode,
            s.name AS SetName,
            c.number AS Number,
            ti.scryfallId AS ScryfallId
        FROM tokens c
        LEFT JOIN tokenIdentifiers ti ON c.uuid = ti.uuid
        LEFT JOIN sets s ON c.setCode = s.code
        """;

    public const string SelectImportLookupRows = SelectImportLookupRowsCards + " UNION ALL " + SelectImportLookupRowsTokens;

    // ============================================================================
    // COLLECTION QUERIES
    // ============================================================================

    public const string CollectionGetQuantity =
        "SELECT quantity FROM my_collection WHERE card_uuid = @uuid";

    public const string CollectionUpdateQuantity =
        "UPDATE my_collection SET quantity = @qty, is_foil = @isFoil, is_etched = @isEtched WHERE card_uuid = @uuid";

    public const string CollectionInsertCard =
        "INSERT INTO my_collection (card_uuid, quantity, is_foil, is_etched, sort_order) VALUES (@uuid, @qty, @isFoil, @isEtched, (SELECT COALESCE(MAX(sort_order), 0) + 1 FROM my_collection))";

    public const string CollectionUpsertAddCard =
        """
        INSERT INTO my_collection (card_uuid, quantity, is_foil, is_etched, sort_order)
        VALUES (@uuid, @qty, @isFoil, @isEtched, (SELECT COALESCE(MAX(sort_order), 0) + 1 FROM my_collection))
        ON CONFLICT(card_uuid) DO UPDATE SET
            quantity = my_collection.quantity + excluded.quantity,
            is_foil = MAX(my_collection.is_foil, excluded.is_foil),
            is_etched = MAX(my_collection.is_etched, excluded.is_etched)
        """;

    public const string CollectionDeleteCard =
        "DELETE FROM my_collection WHERE card_uuid = @uuid";

    public const string CollectionDeleteAll =
        "DELETE FROM my_collection";

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

    /// <summary>
    /// Aggregated collection statistics.
    /// Uses the MTG master DB (with attached collection DB as 'col') to
    /// compute counts and averages in a single, efficient query.
    /// Mirrors the semantics of CollectionRepository.CalculateStats.
    /// </summary>
    public const string CollectionStatsAggregates =
        """
        WITH coll AS (
            SELECT
                mc.quantity,
                mc.is_foil,
                mc.is_etched,
                COALESCE(cc.type, tt.type, '') AS type,
                /* Rarity from cards only; tokens have NULL rarity */
                cc.rarity AS rarity,
                cc.manaValue AS manaValue
            FROM col.my_collection mc
            LEFT JOIN cards cc ON cc.uuid = mc.card_uuid
            LEFT JOIN tokens tt ON tt.uuid = mc.card_uuid
        )
        SELECT
            COALESCE(SUM(quantity), 0) AS TotalCards,
            COUNT(*) AS UniqueCards,
            COALESCE(SUM(CASE WHEN type LIKE '%Creature%' THEN quantity ELSE 0 END), 0) AS CreatureCount,
            COALESCE(SUM(CASE WHEN type LIKE '%Land%' THEN quantity ELSE 0 END), 0) AS LandCount,
            COALESCE(SUM(CASE WHEN type NOT LIKE '%Creature%' AND type NOT LIKE '%Land%' THEN quantity ELSE 0 END), 0) AS SpellCount,
            /* Rarity buckets mirror EnumExtensions.ParseCardRarity */
            COALESCE(SUM(CASE
                WHEN rarity IS NULL THEN quantity
                WHEN LOWER(substr(rarity, 1, 1)) = 'c' THEN quantity
                ELSE 0
            END), 0) AS CommonCount,
            COALESCE(SUM(CASE WHEN LOWER(substr(rarity, 1, 1)) = 'u' THEN quantity ELSE 0 END), 0) AS UncommonCount,
            COALESCE(SUM(CASE WHEN LOWER(substr(rarity, 1, 1)) = 'r' THEN quantity ELSE 0 END), 0) AS RareCount,
            COALESCE(SUM(CASE
                WHEN LOWER(rarity) = 'mythic rare' THEN quantity
                WHEN LOWER(substr(rarity, 1, 1)) = 'm' THEN quantity
                ELSE 0
            END), 0) AS MythicCount,
            COALESCE(SUM(CASE
                WHEN (is_foil IS NOT NULL AND is_foil != 0)
                  OR (is_etched IS NOT NULL AND is_etched != 0)
                    THEN quantity
                ELSE 0
            END), 0) AS FoilCount,
            COALESCE(SUM(CASE
                WHEN type NOT LIKE '%Land%' THEN COALESCE(manaValue, 0) * quantity
                ELSE 0
            END), 0.0) AS TotalCMC,
            COALESCE(SUM(CASE
                WHEN type NOT LIKE '%Land%' THEN quantity
                ELSE 0
            END), 0) AS NonLandCount
        FROM coll
        """;

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

    public const string DeckGetCardCount =
        "SELECT COALESCE(SUM(Quantity), 0) FROM DeckCards WHERE DeckId = @DeckId";

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

    public const string BaseTokens =
        """
        SELECT c.artist, c.artistIds, c.asciiName, c.attractionLights, c.availability, c.boosterTypes,
        c.borderColor, NULL AS cardParts, c.colorIdentity, c.colorIndicator, c.colors, NULL AS defense,
        NULL AS duelDeck, NULL AS edhrecRank, c.edhrecSaltiness, NULL AS faceConvertedManaCost,
        NULL AS faceFlavorName, NULL AS faceManaValue, c.faceName, NULL AS facePrintedName, c.finishes,
        c.flavorName, c.flavorText, c.frameEffects, c.frameVersion, NULL AS hand, NULL AS hasAlternativeDeckLimit,
        NULL AS hasContentWarning, NULL AS isAlternative, c.isFullArt, c.isFunny, NULL AS isGameChanger,
        NULL AS isOnlineOnly, c.isOversized, c.isPromo, NULL AS isRebalanced, c.isReprint, NULL AS isReserved,
        NULL AS isStorySpotlight, c.isTextless, NULL AS isTimeshifted, c.keywords, c.language, c.layout,
        NULL AS leadershipSkills, NULL AS life, NULL AS loyalty, c.manaCost, NULL AS manaValue, c.name, c.number,
        NULL AS originalPrintings, NULL AS originalReleaseDate, c.originalText, c.otherFaceIds, c.power,
        NULL AS printedName, NULL AS printedText, c.printedType, NULL AS printings, c.producedMana, c.promoTypes,
        NULL AS rarity, NULL AS rebalancedPrintings, c.relatedCards, c.securityStamp, c.setCode, c.side,
        c.signature, c.sourceProducts, NULL AS subsets, c.subtypes, c.supertypes, c.text, c.toughness, c.type,
        c.types, c.uuid, NULL AS variations, c.watermark,
        ci.scryfallId, s.name as setName,
        NULL as alchemy, NULL as brawl, NULL as commander, NULL as duel, NULL as future, NULL as gladiator,
        NULL as historic, NULL as legacy, NULL as modern, NULL as oathbreaker, NULL as oldschool,
        NULL as pauper, NULL as paupercommander, NULL as penny, NULL as pioneer, NULL as predh,
        NULL as premodern, NULL as standard, NULL as standardbrawl, NULL as timeless, NULL as vintage,
        NULL as cardKingdom, NULL as cardKingdomFoil, NULL as cardKingdomEtched,
        NULL as cardmarket, NULL as tcgplayer, NULL as tcgplayerEtched
        FROM tokens c
        LEFT JOIN tokenIdentifiers ci ON c.uuid = ci.uuid
        LEFT JOIN sets s ON c.setCode = s.code
        """;

    public const string BaseCardsAndTokens =
        $"""
        SELECT * FROM (
            {BaseCards}
            UNION ALL
            {BaseTokens}
        ) c
        """;

    public const string BaseCollection =
        """
        SELECT c.*, ci.scryfallId, s.name as setName,
        cl.alchemy, cl.brawl, cl.commander, cl.duel, cl.future, cl.gladiator,
        cl.historic, cl.legacy, cl.modern, cl.oathbreaker, cl.oldschool,
        cl.pauper, cl.paupercommander, cl.penny, cl.pioneer, cl.predh,
        cl.premodern, cl.standard, cl.standardbrawl, cl.timeless, cl.vintage,
        cp.cardKingdom, cp.cardKingdomFoil, cp.cardKingdomEtched,
        cp.cardmarket, cp.tcgplayer, cp.tcgplayerEtched,
        mc.quantity
        FROM cards c
        INNER JOIN col.my_collection mc ON c.uuid = mc.card_uuid
        LEFT JOIN cardIdentifiers ci ON c.uuid = ci.uuid
        LEFT JOIN sets s ON c.setCode = s.code
        LEFT JOIN cardLegalities cl ON c.uuid = cl.uuid
        LEFT JOIN cardPurchaseUrls cp ON c.uuid = cp.uuid
        """;

    public const string BaseSets = "SELECT * FROM sets";

    /// <summary>Code and name for set filter dropdown; ordered by name.</summary>
    public const string SelectSetsForFilter = "SELECT code AS Code, name AS Name FROM sets ORDER BY name";

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
    public const string CondInCollection = "EXISTS (SELECT 1 FROM col.my_collection WHERE card_uuid = c.uuid)";
    public const string CondSidePrimary = "(c.side = 'a' OR c.side IS NULL)";
    public const string CondNoVariations = "(c.variations IS NULL OR c.variations = '' OR c.variations = '[]')";
}
