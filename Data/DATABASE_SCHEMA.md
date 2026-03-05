# MTG / Collection Database Schema

Reference schema for the MTG master SQLite database (and related structures). Use when writing queries, mappers, or repository code.

---

## MTG Master Database Tables

### cards
`artist` TEXT, `artistIds` TEXT, `asciiName` TEXT, `attractionLights` TEXT, `availability` TEXT, `boosterTypes` TEXT, `borderColor` TEXT, `cardParts` TEXT, `colorIdentity` TEXT, `colorIndicator` TEXT, `colors` TEXT, `defense` TEXT, `duelDeck` TEXT, `edhrecRank` INTEGER, `edhrecSaltiness` REAL, `faceConvertedManaCost` REAL, `faceFlavorName` TEXT, `faceManaValue` REAL, `faceName` TEXT, `facePrintedName` TEXT, `finishes` TEXT, `flavorName` TEXT, `flavorText` TEXT, `frameEffects` TEXT, `frameVersion` TEXT, `hand` TEXT, `hasAlternativeDeckLimit` BOOLEAN, `hasContentWarning` BOOLEAN, `isAlternative` BOOLEAN, `isFullArt` BOOLEAN, `isFunny` BOOLEAN, `isGameChanger` BOOLEAN, `isOnlineOnly` BOOLEAN, `isOversized` BOOLEAN, `isPromo` BOOLEAN, `isRebalanced` BOOLEAN, `isReprint` BOOLEAN, `isReserved` BOOLEAN, `isStorySpotlight` BOOLEAN, `isTextless` BOOLEAN, `isTimeshifted` BOOLEAN, `keywords` TEXT, `language` TEXT, `layout` TEXT, `leadershipSkills` TEXT, `life` TEXT, `loyalty` TEXT, `manaCost` TEXT, `manaValue` REAL, `name` TEXT, `number` TEXT, `originalPrintings` TEXT, `originalReleaseDate` TEXT, `originalText` TEXT, `otherFaceIds` TEXT, `power` TEXT, `printedName` TEXT, `printedText` TEXT, `printedType` TEXT, `printings` TEXT, `producedMana` TEXT, `promoTypes` TEXT, `rarity` TEXT, `rebalancedPrintings` TEXT, `relatedCards` TEXT, `securityStamp` TEXT, `setCode` TEXT, `side` TEXT, `signature` TEXT, `sourceProducts` TEXT, `subsets` TEXT, `subtypes` TEXT, `supertypes` TEXT, `text` TEXT, `toughness` TEXT, `type` TEXT, `types` TEXT, `uuid` TEXT, `variations` TEXT, `watermark` TEXT

### cardIdentifiers
`uuid` TEXT, `scryfallId` TEXT, `scryfallOracleId` TEXT, `scryfallIllustrationId` TEXT, `scryfallCardBackId` TEXT, `mcmId` TEXT, `mcmMetaId` TEXT, `mtgArenaId` TEXT, `mtgoId` TEXT, `mtgoFoilId` TEXT, `multiverseId` TEXT, `tcgplayerProductId` TEXT, `tcgplayerEtchedProductId` TEXT, `tcgplayerAlternativeFoilProductId` TEXT, `cardKingdomId` TEXT, `cardKingdomFoilId` TEXT, `cardKingdomEtchedId` TEXT, `cardsphereId` TEXT, `cardsphereFoilId` TEXT, `deckboxId` TEXT, `mtgjsonFoilVersionId` TEXT, `mtgjsonNonFoilVersionId` TEXT, `mtgjsonV4Id` TEXT

### cardLegalities
`uuid` TEXT, `alchemy` TEXT, `brawl` TEXT, `commander` TEXT, `duel` TEXT, `future` TEXT, `gladiator` TEXT, `historic` TEXT, `legacy` TEXT, `modern` TEXT, `oathbreaker` TEXT, `oldschool` TEXT, `pauper` TEXT, `paupercommander` TEXT, `penny` TEXT, `pioneer` TEXT, `predh` TEXT, `premodern` TEXT, `standard` TEXT, `standardbrawl` TEXT, `timeless` TEXT, `vintage` TEXT

### cardRulings
`uuid` TEXT, `date` DATE, `text` TEXT

### cardPurchaseUrls
`uuid` TEXT, `cardKingdom` TEXT, `cardKingdomFoil` TEXT, `cardKingdomEtched` TEXT, `cardmarket` TEXT, `tcgplayer` TEXT, `tcgplayerEtched` TEXT, `tcgplayerAlternativeFoil` TEXT

### tokens
`artist` TEXT, `artistIds` TEXT, `asciiName` TEXT, `attractionLights` TEXT, `availability` TEXT, `boosterTypes` TEXT, `borderColor` TEXT, `colorIdentity` TEXT, `colorIndicator` TEXT, `colors` TEXT, `edhrecSaltiness` REAL, `faceName` TEXT, `finishes` TEXT, `flavorName` TEXT, `flavorText` TEXT, `frameEffects` TEXT, `frameVersion` TEXT, `isFullArt` BOOLEAN, `isFunny` BOOLEAN, `isOversized` BOOLEAN, `isPromo` BOOLEAN, `isReprint` BOOLEAN, `isTextless` BOOLEAN, `keywords` TEXT, `language` TEXT, `layout` TEXT, `manaCost` TEXT, `name` TEXT, `number` TEXT, `orientation` TEXT, `originalText` TEXT, `otherFaceIds` TEXT, `power` TEXT, `printedType` TEXT, `producedMana` TEXT, `promoTypes` TEXT, `relatedCards` TEXT, `securityStamp` TEXT, `setCode` TEXT, `side` TEXT, `signature` TEXT, `sourceProducts` TEXT, `subtypes` TEXT, `supertypes` TEXT, `text` TEXT, `toughness` TEXT, `type` TEXT, `types` TEXT, `uuid` TEXT, `watermark` TEXT

### tokenIdentifiers
`uuid` TEXT, `scryfallId` TEXT, `scryfallOracleId` TEXT, `scryfallIllustrationId` TEXT, `scryfallCardBackId` TEXT, `mcmId` TEXT, `mcmMetaId` TEXT, `mtgArenaId` TEXT, `mtgoId` TEXT, `mtgoFoilId` TEXT, `multiverseId` TEXT, `tcgplayerProductId` TEXT, `tcgplayerEtchedProductId` TEXT, `tcgplayerAlternativeFoilProductId` TEXT, `cardKingdomId` TEXT, `cardKingdomFoilId` TEXT, `cardKingdomEtchedId` TEXT, `cardsphereId` TEXT, `cardsphereFoilId` TEXT, `deckboxId` TEXT, `mtgjsonFoilVersionId` TEXT, `mtgjsonNonFoilVersionId` TEXT, `mtgjsonV4Id` TEXT

### sets
`code` TEXT, `name` TEXT, `mtgoCode` TEXT, `block` TEXT, `tokenSetCode` TEXT, `releaseDate` TEXT, `type` TEXT, `isOnlineOnly` BOOLEAN, `isFoilOnly` BOOLEAN, `tcgplayerGroupId` INTEGER, `isNonFoilOnly` BOOLEAN, `parentCode` TEXT, `totalSetSize` INTEGER, `baseSetSize` INTEGER, `keyruneCode` TEXT, `mcmId` INTEGER, `mcmName` TEXT, `mcmIdExtras` INTEGER, `isForeignOnly` BOOLEAN, `isPartialPreview` BOOLEAN

### setTranslations
`code` TEXT, `language` TEXT, `translation` TEXT

### setBoosterSheets
`setCode` TEXT, `boosterName` TEXT, `sheetName` TEXT, `sheetIsFoil` BOOLEAN, `sheetHasBalanceColors` BOOLEAN, `sheetTotalWeight` INTEGER

### setBoosterSheetCards
`setCode` TEXT, `boosterName` TEXT, `sheetName` TEXT, `cardUuid` TEXT, `cardWeight` INTEGER

### setBoosterContents
`setCode` TEXT, `boosterName` TEXT, `boosterIndex` INTEGER, `sheetName` TEXT, `sheetPicks` INTEGER

### setBoosterContentWeights
`setCode` TEXT, `boosterName` TEXT, `boosterIndex` INTEGER, `boosterWeight` INTEGER

### meta
`date` TEXT, `version` TEXT

---

## Indexes (MTG master)

- `idx_cards_uuid` ON `cards` (`uuid`)
- `idx_cards_name` ON `cards` (`name`)
- `idx_cards_setCode` ON `cards` (`setCode`)
- `idx_cardIdentifiers_uuid` ON `cardIdentifiers` (`uuid`)
- `idx_cardLegalities_uuid` ON `cardLegalities` (`uuid`)
- `idx_cardRulings_uuid` ON `cardRulings` (`uuid`)
- `idx_cardPurchaseUrls_uuid` ON `cardPurchaseUrls` (`uuid`)
- `idx_tokens_uuid` ON `tokens` (`uuid`)
- `idx_tokens_name` ON `tokens` (`name`)
- `idx_tokens_setCode` ON `tokens` (`setCode`)
- `idx_tokenIdentifiers_uuid` ON `tokenIdentifiers` (`uuid`)
- `idx_sets_code` ON `sets` (`code`)
- `idx_sets_name` ON `sets` (`name`)
- `idx_setTranslations_code` ON `setTranslations` (`code`)
- `idx_setBoosterSheets_setCode` ON `setBoosterSheets` (`setCode`)
- `idx_setBoosterSheetCards_setCode` ON `setBoosterSheetCards` (`setCode`)
- `idx_setBoosterSheetCards_cardUuid` ON `setBoosterSheetCards` (`cardUuid`)
- `idx_setBoosterContents_setCode` ON `setBoosterContents` (`setCode`)
- `idx_setBoosterContentWeights_setCode` ON `setBoosterContentWeights` (`setCode`)

---

## Collection database (separate DB)

See `SQLQueries.cs` for `my_collection`, `Decks`, `DeckCards`, and price tables. Schema for those is defined in the same file via `CreateCollectionTable`, `CreateDecksTable`, `CreateDeckCardsTable`, etc.
