# AetherVault

A Magic: The Gathering collection manager for Android. Search any card in the game, track what you own, build decks, and get stats on your collection — all stored locally on your device.

Still in active development. If you play Magic and have an Android phone, I'm always looking for people to try it out and tell me what's broken or missing.

---

<table>
  <tr>
    <td align="center"><img src="docs/media/screenshots/search-page.jpg" width="220" alt="Search Page"/><br/><b>Search</b></td>
    <td align="center"><img src="docs/media/screenshots/collection-page.jpg" width="220" alt="Collection Page"/><br/><b>Collection</b></td>
    <td align="center"><img src="docs/media/screenshots/card-detail.jpg" width="220" alt="Card Detail"/><br/><b>Card Detail</b></td>
  </tr>
  <tr>
    <td align="center"><img src="docs/media/screenshots/decks-page.jpg" width="220" alt="Decks Page"/><br/><b>Decks</b></td>
    <td align="center"><img src="docs/media/screenshots/sample-decks.jpg" width="220" alt="Sample Decks"/><br/><b>Browse Sample Decks</b></td>
    <td align="center"><img src="docs/media/screenshots/stats-page.jpg" width="220" alt="Stats Page"/><br/><b>Stats</b></td>
  </tr>
</table>

---

## What it does

- Search the full MTG card database with filters for color, type, rarity, format legality, and more
- Browse card details, rulings, and legalities
- Track your collection and see what you own at a glance
- Build and manage decks
- Collection stats — mana curve, color breakdown, total value, etc.
- Card images loaded from Scryfall and cached locally so they don't reload every time
- Swipe between cards in search results without going back to the list

Everything runs locally. No account needed, no data sent anywhere.

---

## Interested in testing?

The app isn't on the Play Store yet. If you want to try it, open an issue or reach out and I can send you an APK.

Feedback on anything — crashes, missing features, things that feel off — is genuinely helpful at this stage.

---

## Building it yourself

You'll need:
- .NET 10 SDK
- .NET MAUI workload with Android support
- Android SDK (API 21+)
- Java JDK

```bash
dotnet workload install maui-android
dotnet restore AetherVault.sln
dotnet build AetherVault.csproj -f net10.0-android -m
```

To run directly on a device or emulator:

```bash
dotnet build AetherVault.csproj -t:Run -f net10.0-android -m
```

Use `-m` to build in parallel — makes a noticeable difference. For a standalone APK, set `EmbedAssembliesIntoApk` to `True` in the Debug property group in `AetherVault.csproj`.

---

## Tests

```bash
dotnet test AetherVault.Tests/AetherVault.Tests.csproj
```

---

## How it works under the hood

On first launch the app downloads the MTG database from GitHub Releases (~50MB zip). After that everything is local.

Two SQLite databases:
- A read-only copy of MTGJSON data for all card info
- A separate read-write database for your collection and decks

The card grid is rendered with SkiaSharp rather than standard MAUI controls — it handles large card lists without slowing down.

The database is rebuilt weekly via GitHub Actions whenever MTGJSON publishes updates.

---

## Repo layout

```
Data/        repositories, SQL queries, database manager
Services/    image cache, card manager, deck builder
ViewModels/  MVVM view models
Pages/       MAUI XAML pages
Controls/    custom controls and card grid renderer
Core/        shared models, enums, utilities
```
