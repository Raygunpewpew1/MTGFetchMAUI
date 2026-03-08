# Future Tasks

## UI Improvements
- [x] **Evaluate UI Libraries**
    - **Context**: Investigated UraniumUI vs Syncfusion for "Better Visuals" (Material Design inputs, charts, etc.).
    - **Decision**: Syncfusion is rejected due to licensing/paywall.
    - **Goal**: Improve "eye candy" (better inputs, floating labels, styled checkboxes).
    - **Result**: Implemented **UraniumUI** for inputs/styling (MIT License) in Search and Filters pages.
    - **Note**: UraniumUI lacks charts, so a separate library (e.g., Microcharts, LiveCharts2) would be needed for the Stats page.

## Features
- [ ] **Deck Building**
    - **Status**: In progress.
    - **Description**: Implement functionality to build and manage decks.

## Server / Backend (future — revisit later)
- [ ] **Backend options (noted for later)**
    - **ASP.NET Core** — same stack, full control; Minimal API / Web API + SignalR; host on Azure, AWS, Fly.io, etc.
    - **Azure** — Mobile Apps, Azure SQL/Cosmos, Functions (serverless).
    - **Firebase** — Firestore (real-time sync), Auth, Cloud Functions; no C# server to host.
    - **Supabase** — Postgres, auth, real-time; REST/real-time from app.
    - **Custom sync** — small ASP.NET Core API to upload/download collection DB or deltas; keep existing SQLite model, add backup/sync.
    - **Use case to decide**: sync across devices, cloud backup, shared decks, user accounts, etc.
