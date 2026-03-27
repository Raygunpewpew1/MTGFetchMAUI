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

## Platforms and cross-device use

The beta I’m shipping is **Android only**. Fair warning: when someone asks for “PC” or “cross-platform,” they might mean any of three different things — and they’re not the same project.

Putting the **same app on Windows** (or another desktop) is mostly packaging. Useful on its own, but it doesn’t magically keep your phone and your PC in sync.

**Moving your data** is a separate idea. That’s what **CSV** is for: **import and export** with a **Moxfield-style** column layout, so you can back up, tweak things in a spreadsheet, or hand the list to another tool without lock-in.

**Live sync** — your collection and decks always matching on your phone and somewhere else without emailing files — needs sign-in, hosted storage, and real rules for conflicting edits. **That isn’t in the app today.**

A lot of people tell me they want a **web page on PC**, not another installed catalog. A browser companion (or actual cloud sync) is its own milestone beside a desktop build; **Windows alone** wouldn’t give you “open a tab anywhere” or automatic sync.

So the straight answer: **local-first** Android, **offline search** against the bundled MTG database once it’s on your device, **no account**. **CSV** is the bridge to the rest of your workflow until sync is a thing.

---

## Try it

The app is available through Firebase App Distribution — no Play Store needed.

**[Download the beta](https://appdistribution.firebase.dev/i/3f7cde07e1510353)**

Open that link on your Android device, install the Firebase App Tester app when prompted, and you're in. Any feedback — crashes, things that feel broken, features you'd want — is welcome. Open an issue or just reach out directly.

**Help prioritize what to build next** (copy/paste or answer wherever you send feedback):

- Would you prefer **automatic sync across devices** (sign-in and dependence on cloud/services) or staying **local-only** with **periodic CSV export/import**?
- For using your collection on a PC, does a **browser-based** experience matter more to you than a **Windows (or other desktop) app**?

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
