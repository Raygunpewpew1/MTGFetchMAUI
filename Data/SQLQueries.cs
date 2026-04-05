namespace AetherVault.Data;

/// <summary>
/// Centralized SQL query and schema definitions. All SQL used by repositories lives here so we avoid
/// scattered string literals and keep parameterized queries in one place. Never build SQL by concatenating user input.
/// </summary>
public static class SqlQueries
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

    public const string CreatePricesIndex =
        "CREATE INDEX IF NOT EXISTS idx_prices_uuid ON card_prices(uuid)";

    /// <summary>Composite index matching common pattern WHERE uuid = @uuid AND source = 'paper'.</summary>
    public const string CreatePricesUuidSourceIndex =
        "CREATE INDEX IF NOT EXISTS idx_prices_uuid_source ON card_prices(uuid, source)";

    public const string DropPricesIndex = "DROP INDEX IF EXISTS idx_prices_uuid";

    // Detects old wide-column schema (pre-refactor). Used for one-time migration.
    public const string PricesSchemaCheck =
        "SELECT COUNT(*) FROM pragma_table_info('card_prices') WHERE name = 'tcg_retail_normal'";

    // ATTACH-based sync from a downloaded AllPricesToday.sqlite (attached as 'today').
    // 'priceType' is the camelCase column name used by MTGJSON; we map it to snake_case locally.
    // Filter: paper only, vendors used by PriceDisplayHelper, retail only, finishes shown in UI.
    public const string PricesSyncFromAttached =
        """
        INSERT OR REPLACE INTO card_prices (uuid, source, provider, price_type, finish, currency, price)
        SELECT uuid, source, provider, priceType, finish, currency, price
        FROM today.prices
        WHERE price IS NOT NULL AND price > 0
          AND lower(source) = 'paper'
          AND lower(provider) IN ('tcgplayer', 'cardmarket', 'cardkingdom', 'manapool')
          AND lower(priceType) = 'retail'
          AND lower(finish) IN ('normal', 'foil', 'etched')
        """;

    /// <summary>Diagnostic count against attached <c>today</c>; must match <see cref="PricesSyncFromAttached"/> filter.</summary>
    public const string PricesCountFilteredInAttached =
        """
        SELECT COUNT(*) FROM today.prices
        WHERE price IS NOT NULL AND price > 0
          AND lower(source) = 'paper'
          AND lower(provider) IN ('tcgplayer', 'cardmarket', 'cardkingdom', 'manapool')
          AND lower(priceType) = 'retail'
          AND lower(finish) IN ('normal', 'foil', 'etched')
        """;

    // Aliases: Dapper maps columns to PascalCase properties; snake_case price_type does not match PriceType.
    public const string PricesGetByUuid =
        "SELECT provider, price_type AS PriceType, finish, currency, price FROM card_prices WHERE uuid = @uuid AND source = 'paper'";

    public const string PricesCount =
        "SELECT COUNT(*) FROM card_prices";

    public const string PricesDeleteAll =
        "DELETE FROM card_prices";

    public const string PricesGetBulkByUuids =
        "SELECT uuid, provider, price_type AS PriceType, finish, currency, price FROM card_prices WHERE uuid IN ({0}) AND source = 'paper'";

    /// <summary>
    /// All paper price rows for distinct cards in the user's collection (requires collection DB attached as <c>col</c>).
    /// Single round-trip for collection-wide price sort / cache warm.
    /// </summary>
    public const string PricesGetAllForCollection =
        """
        SELECT p.uuid, p.provider, p.price_type AS PriceType, p.finish, p.currency, p.price
        FROM card_prices p
        WHERE p.source = 'paper'
          AND p.uuid IN (SELECT DISTINCT card_uuid FROM col.my_collection)
        """;

    /// <summary>
    /// Computes total collection value by joining attached collection DB (alias: col) with card_prices.
    /// Provider priority is passed via @v1..@v4 and finish fallback mirrors PriceDisplayHelper.GetNumericPrice().
    /// </summary>
    public const string PricesGetCollectionTotalValue =
        """
        WITH price_rows AS (
            SELECT
                mc.card_uuid,
                mc.quantity,
                mc.is_foil,
                mc.is_etched,
                MAX(CASE WHEN p.provider = @v1 AND p.finish = 'normal' THEN p.price ELSE 0 END) AS v1_normal,
                MAX(CASE WHEN p.provider = @v1 AND p.finish = 'foil' THEN p.price ELSE 0 END) AS v1_foil,
                MAX(CASE WHEN p.provider = @v1 AND p.finish = 'etched' THEN p.price ELSE 0 END) AS v1_etched,
                MAX(CASE WHEN p.provider = @v2 AND p.finish = 'normal' THEN p.price ELSE 0 END) AS v2_normal,
                MAX(CASE WHEN p.provider = @v2 AND p.finish = 'foil' THEN p.price ELSE 0 END) AS v2_foil,
                MAX(CASE WHEN p.provider = @v2 AND p.finish = 'etched' THEN p.price ELSE 0 END) AS v2_etched,
                MAX(CASE WHEN p.provider = @v3 AND p.finish = 'normal' THEN p.price ELSE 0 END) AS v3_normal,
                MAX(CASE WHEN p.provider = @v3 AND p.finish = 'foil' THEN p.price ELSE 0 END) AS v3_foil,
                MAX(CASE WHEN p.provider = @v3 AND p.finish = 'etched' THEN p.price ELSE 0 END) AS v3_etched,
                MAX(CASE WHEN p.provider = @v4 AND p.finish = 'normal' THEN p.price ELSE 0 END) AS v4_normal,
                MAX(CASE WHEN p.provider = @v4 AND p.finish = 'foil' THEN p.price ELSE 0 END) AS v4_foil,
                MAX(CASE WHEN p.provider = @v4 AND p.finish = 'etched' THEN p.price ELSE 0 END) AS v4_etched
            FROM col.my_collection mc
            LEFT JOIN card_prices p
                ON p.uuid = mc.card_uuid
               AND p.source = 'paper'
               AND p.price_type = 'retail'
            GROUP BY mc.card_uuid, mc.quantity, mc.is_foil, mc.is_etched
        )
        SELECT COALESCE(SUM(quantity * COALESCE(
            CASE
                WHEN (v1_normal > 0 OR v1_foil > 0 OR v1_etched > 0) THEN
                    CASE
                        WHEN is_etched != 0 AND v1_etched > 0 THEN v1_etched
                        WHEN is_foil != 0 AND v1_foil > 0 THEN v1_foil
                        WHEN v1_normal > 0 THEN v1_normal
                        WHEN v1_foil > 0 THEN v1_foil
                        WHEN v1_etched > 0 THEN v1_etched
                        ELSE 0
                    END
                ELSE NULL
            END,
            CASE
                WHEN (v2_normal > 0 OR v2_foil > 0 OR v2_etched > 0) THEN
                    CASE
                        WHEN is_etched != 0 AND v2_etched > 0 THEN v2_etched
                        WHEN is_foil != 0 AND v2_foil > 0 THEN v2_foil
                        WHEN v2_normal > 0 THEN v2_normal
                        WHEN v2_foil > 0 THEN v2_foil
                        WHEN v2_etched > 0 THEN v2_etched
                        ELSE 0
                    END
                ELSE NULL
            END,
            CASE
                WHEN (v3_normal > 0 OR v3_foil > 0 OR v3_etched > 0) THEN
                    CASE
                        WHEN is_etched != 0 AND v3_etched > 0 THEN v3_etched
                        WHEN is_foil != 0 AND v3_foil > 0 THEN v3_foil
                        WHEN v3_normal > 0 THEN v3_normal
                        WHEN v3_foil > 0 THEN v3_foil
                        WHEN v3_etched > 0 THEN v3_etched
                        ELSE 0
                    END
                ELSE NULL
            END,
            CASE
                WHEN (v4_normal > 0 OR v4_foil > 0 OR v4_etched > 0) THEN
                    CASE
                        WHEN is_etched != 0 AND v4_etched > 0 THEN v4_etched
                        WHEN is_foil != 0 AND v4_foil > 0 THEN v4_foil
                        WHEN v4_normal > 0 THEN v4_normal
                        WHEN v4_foil > 0 THEN v4_foil
                        WHEN v4_etched > 0 THEN v4_etched
                        ELSE 0
                    END
                ELSE NULL
            END,
            0
        )), 0.0)
        FROM price_rows
        """;

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

    /// <summary>Find one card/token by name and set (for MTGJSON deck import fallback).</summary>
    public const string WhereNameAndSet = " WHERE c.name = @name AND c.setCode = @set LIMIT 1";
    /// <summary>Find one card/token by Scryfall ID (for MTGJSON deck import fallback).</summary>
    public const string WhereScryfallId = " WHERE c.scryfallId = @sid LIMIT 1";

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

    // Aliases: Dapper matches column names to properties; snake_case columns do not map to PascalCase POCOs.
    public const string CollectionGetAll =
        "SELECT card_uuid AS CardUuid, quantity, date_added AS DateAdded, sort_order AS SortOrder, is_foil AS IsFoil, is_etched AS IsEtched FROM my_collection ORDER BY sort_order ASC, date_added DESC";

    /// <summary>Minimal projection for pricing calculations — no ORDER BY, no unused columns.</summary>
    public const string CollectionGetForPricing =
        "SELECT card_uuid AS CardUuid, quantity, is_foil AS IsFoil, is_etched AS IsEtched FROM my_collection";

    /// <summary>
    /// Single round-trip load for the Collection tab grid: only columns needed for grid CardState, sort, filter,
    /// and export — not full BaseCards text/JSON blobs. Joins col.my_collection to cards or tokens (one row per owned card).
    /// </summary>
    public const string CollectionGridLoad =
        """
        SELECT
            mc.card_uuid AS CardUuid,
            mc.quantity AS Quantity,
            mc.date_added AS DateAdded,
            mc.sort_order AS SortOrder,
            mc.is_foil AS IsFoil,
            mc.is_etched AS IsEtched,
            COALESCE(c.uuid, t.uuid) AS Uuid,
            COALESCE(c.name, t.name) AS Name,
            COALESCE(c.type, t.type) AS CardType,
            COALESCE(c.manaCost, t.manaCost) AS ManaCost,
            c.manaValue AS ManaValue,
            c.faceManaValue AS FaceManaValue,
            COALESCE(c.colorIdentity, t.colorIdentity) AS ColorIdentity,
            c.rarity AS Rarity,
            COALESCE(c.setCode, t.setCode) AS SetCode,
            COALESCE(c.number, t.number) AS Number,
            COALESCE(c.isOnlineOnly, 0) AS IsOnlineOnly,
            COALESCE(ci.scryfallId, tci.scryfallId) AS ScryfallId
        FROM col.my_collection mc
        LEFT JOIN cards c ON c.uuid = mc.card_uuid
        LEFT JOIN tokens t ON t.uuid = mc.card_uuid AND c.uuid IS NULL
        LEFT JOIN cardIdentifiers ci ON ci.uuid = c.uuid
        LEFT JOIN tokenIdentifiers tci ON tci.uuid = t.uuid
        WHERE COALESCE(c.uuid, t.uuid) IS NOT NULL
        ORDER BY mc.sort_order ASC, mc.date_added DESC
        """;

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
            /* Rarity buckets mirror EnumExtensions.ParseCardRarity; MTGJSON stores lowercase values */
            COALESCE(SUM(CASE
                WHEN rarity IS NULL THEN quantity
                WHEN rarity = 'common' THEN quantity
                ELSE 0
            END), 0) AS CommonCount,
            COALESCE(SUM(CASE WHEN rarity = 'uncommon' THEN quantity ELSE 0 END), 0) AS UncommonCount,
            COALESCE(SUM(CASE WHEN rarity = 'rare' THEN quantity ELSE 0 END), 0) AS RareCount,
            COALESCE(SUM(CASE WHEN rarity = 'mythic' THEN quantity ELSE 0 END), 0) AS MythicCount,
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

    /// <summary>Returns DeckId and Total (sum of Quantity) for each deck. Use with Dapper IN @DeckIds.</summary>
    public const string DeckGetCardCountsBatch =
        "SELECT DeckId, CAST(COALESCE(SUM(Quantity), 0) AS INTEGER) AS Total FROM DeckCards WHERE DeckId IN @DeckIds GROUP BY DeckId";

    // ============================================================================
    // FTS5 (av_cards_fts) — built by CI; optional at runtime
    // ============================================================================

    /// <summary>FTS table name; must match .github/workflows/main.yml.</summary>
    public const string FtsTableName = "av_cards_fts";

    /// <summary>Returns one row if FTS table exists; used for runtime capability check.</summary>
    public const string FtsExistsCheck = "SELECT 1 FROM sqlite_master WHERE type='table' AND name='av_cards_fts' LIMIT 1";


    /// <summary>Fragment: c.uuid IN (SELECT uuid FROM av_cards_fts WHERE av_cards_fts MATCH @paramName). Append param name + ")".</summary>
    public const string CondFtsMatchPrefix = "c.uuid IN (SELECT uuid FROM av_cards_fts WHERE av_cards_fts MATCH @";

    /// <summary>Order by FTS relevance (bm25) then name. Use when FTS filter is active.</summary>
    public const string OrderByFtsRelevanceThenName =
        "ORDER BY (SELECT bm25(av_cards_fts) FROM av_cards_fts WHERE av_cards_fts.uuid = c.uuid LIMIT 1) ASC, c.name";

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
        SELECT
        c.artist, c.artistIds, c.asciiName, c.attractionLights, c.availability, c.boosterTypes,
        c.borderColor, c.cardParts, c.colorIdentity, c.colorIndicator, c.colors, c.defense,
        c.duelDeck, c.edhrecRank, c.edhrecSaltiness, c.faceConvertedManaCost,
        c.faceFlavorName, c.faceManaValue, c.faceName, c.facePrintedName, c.finishes,
        c.flavorName, c.flavorText, c.frameEffects, c.frameVersion, c.hand, c.hasAlternativeDeckLimit,
        c.hasContentWarning, c.isAlternative, c.isFullArt, c.isFunny, c.isGameChanger,
        c.isOnlineOnly, c.isOversized, c.isPromo, c.isRebalanced, c.isReprint, c.isReserved,
        c.isStorySpotlight, c.isTextless, c.isTimeshifted, c.keywords, c.language, c.layout,
        c.leadershipSkills, c.life, c.loyalty, c.manaCost, c.manaValue, c.name, c.number,
        c.originalPrintings, c.originalReleaseDate, c.originalText, c.otherFaceIds, c.power,
        c.printedName, c.printedText, c.printedType, c.printings, c.producedMana, c.promoTypes,
        c.rarity, c.rebalancedPrintings, c.relatedCards, c.securityStamp, c.setCode, c.side,
        c.signature, c.sourceProducts, c.subsets, c.subtypes, c.supertypes, c.text, c.toughness, c.type,
        c.types, c.uuid, c.variations, c.watermark,
        ci.scryfallId, s.name as setName,
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
    /// <summary>Matches colorless cards (MTGJSON stores empty colors, not "C").</summary>
    public const string CondColorless = "(c.colors IS NULL OR c.colors = '' OR TRIM(c.colors) = '' OR c.colors = '[]')";
    /// <summary>Matches colorless color identity (empty JSON array in MTGJSON).</summary>
    public const string CondColorIdentityColorless =
        "(c.colorIdentity IS NULL OR c.colorIdentity = '' OR TRIM(c.colorIdentity) = '' OR c.colorIdentity = '[]')";
    public const string CondManaValue = "c.manaValue = @";
    public const string CondManaValueBetween = "c.manaValue BETWEEN @";
    public const string CondPower = "c.power = @";
    public const string CondToughness = "c.toughness = @";
    public const string CondArtist = "c.artist LIKE @";
    public const string CondInCollection = "EXISTS (SELECT 1 FROM col.my_collection WHERE card_uuid = c.uuid)";
    public const string CondSidePrimary = "(c.side = 'a' OR c.side IS NULL)";
    public const string CondNoVariations = "(c.variations IS NULL OR c.variations = '' OR c.variations = '[]')";
    /// <summary>Cards that can be a commander: Legendary Creature or text contains "can be your commander".</summary>
    public const string CondCommanderOnly =
        """
        (
            (
                c.leadershipSkills IS NOT NULL
                AND TRIM(c.leadershipSkills) != ''
                AND (
                    json_extract(c.leadershipSkills, '$.commander') = 1
                )
            )
            OR
            ((c.type LIKE '%Legendary%' AND c.type LIKE '%Creature%') OR (c.text LIKE '%can be your commander%'))
        )
        """;
}
