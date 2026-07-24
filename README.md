# PkManager — Cross-Generation Save Manager for Creature-Collection Games

A full-stack web application for uploading, visualizing, editing, and managing save files from **22 creature-collection games** spanning **Generations 3–7** (GBA / NDS / 3DS). Built on [PKHeX.Core](https://phonologic-diethylstilboestrol121.github.io) for legality-aware save editing, with in-browser GBA/NDS emulation via mGBA & melonDS WASM, plus local 3DS emulator integration via Azahar.

> **Current scope**: Gen3–7 (GBA / NDS / 3DS). Gen8–9 (Switch) metadata is retained for save identification only; editor and emulator features are not yet developed for those generations.

---

## Features

- **Save File Management** — Upload, download, export, and delete save files. Automatic 5-slot backup with one-click restore.
- **Creature Editor** — 7-tab editor (Main / Stats / Moves / Met / Legality / OT-Misc / Cosmetic) covering 60%+ of PKHeX fields, with generation-specific fields (LGPE AVs, LA GVs, Dynamax Level, Tera Type, Alpha, etc.).
- **Legality System** — Tri-state legality analysis (Legal / Fishy / Illegal) with per-check indicators and 7 AutoFix strategies (Ball, Met Location, Moves, Relearn Moves, Ability, Nature, Shiny). QR code generation for 3DS hardware injection.
- **Bag Editor** — Multi-pouch item management (15 pouch types, capability-driven display).
- **Trainer Editor** — OT info, badges (visual toggle), currencies (Money / Coins / BP / League Points), Game Sync ID, trainer card.
- **Dex Editor** — Seen / Caught management, batch operations (seenAll / caughtAll / clearAll), Gen4 extended fields (gender, forms, Spinda spots, language flags).
- **Advanced Search** — 23-dimension filtering across save boxes & bank, with save/load filter presets.
- **Encounter Database** — Search legal encounters from PKHeX static encounter tables; apply constraints or generate legal creatures directly into boxes.
- **One-Click Evolution** — Evolution path discovery with branch selection and trade-evolution support (auto-sets traded state).
- **Showdown Import/Export** — Import from Showdown / PokePaste text or URL; export to Showdown format.
- **Generation-Specific Tools** — Gen3 RTC clock editor, Gen6 O-Powers, Gen5 Dream World viewer, Gen7 Zygarde Cell tracker.
- **Cross-Save Storage (Bank)** — Cross-save storage with batch move, batch delete, batch export (.zip), legality scan, and full editing.
- **GBA Emulator (mGBA WASM)** — In-browser Gen3 emulation with save sync, save states, cheats (CodeBreaker), speed control, gamepad support, touch controls, and AI control interface.
- **NDS Emulator (melonDS WASM)** — In-browser Gen4–5 emulation with dual-screen rendering, touch overlay, save sync.
- **Local NDS Emulator (DeSmuME)** — Protocol launcher + fallback script for native DeSmuME with save injection, auto-sync on exit, and local save restoration.
- **Local 3DS Emulator (Azahar)** — Protocol launcher + fallback script for native Azahar (Citra successor) covering 8 Gen6–7 games.
- **Diagnostics System** — Global error boundary, circular-buffer diagnostic store (200 entries), localStorage persistence, sendBeacon auto-upload to server, backend exception logging, one-click health check script.
- **i18n** — 10 display languages (zh-Hans, zh-Hant, en, ja, fr, it, de, es, es-419, ko) for both server messages and client UI.
- **Dark/Light Theme** — Toggle with system-follow option, localStorage persistence.
- **Dual Sprite Style** — Game-accurate pixel sprites (local, ~4 MB) and Home-style HD sprites (CDN lazy-load).

---

## Screenshots

### Dashboard

![Dashboard](image/dashboard.png)

### Save Files

![Save Files](image/saves.png)

### Creature Editor & Bank

![Creature Editor & Bank](image/bank.png)

### Creature Editor

![Creature Editor](image/edit.png)

### In-Browser Emulator

![In-Browser Emulator](image/game.png)

### Settings

![Settings](image/settings.png)

---

## Tech Stack

| Layer | Technology |
|---|---|
| **Backend** | ASP.NET Core 10 (.NET 10.0.300 LTS), C# |
| **Save Engine** | PKHeX.Core v26.05.05 (SDK source-build → local NuGet) |
| **Frontend** | React 19, TypeScript ~6.0, Vite 8, Ant Design 6 |
| **State Management** | Zustand |
| **Drag & Drop** | @dnd-kit |
| **Database** | PostgreSQL 14.23 (local, non-Docker) |
| **Data Access** | Dapper + Npgsql (raw SQL, snake_case → PascalCase) |
| **Auth** | JWT Bearer (HMAC-SHA256) + BCrypt password hashing |
| **GBA Emulator** | mGBA WASM (@thenick775/mgba-wasm) |
| **NDS Emulator** | melonDS WASM (Emscripten 5.0.7, SIMD + PThreads) |
| **Local NDS Emulator** | DeSmuME (GPLv2, via protocol launcher) |
| **Local 3DS Emulator** | Azahar (GPLv2, Citra successor) |
| **i18n** | react-i18next (client) + JsonMessageLocalizer (server) |

---

## Supported Games

| Gen | Platform | Games |
|---|---|---|
| **Gen3** | GBA | Ruby, Sapphire, Emerald, FireRed, LeafGreen |
| **Gen4** | NDS | Diamond, Pearl, Platinum, HeartGold, SoulSilver |
| **Gen5** | NDS | Black, White, Black 2, White 2 |
| **Gen6** | 3DS | X, Y, Omega Ruby, Alpha Sapphire |
| **Gen7** | 3DS | Sun, Moon, Ultra Sun, Ultra Moon |

---

## Quick Start

### Prerequisites

- **.NET SDK** 10.0.300+ ([download](https://phonologic-diethylstilboestrol121.github.io))
- **Node.js** 20+ and **npm** 10+
- **PostgreSQL** 14 (client tools: `psql`, `pg_ctl`, `initdb`)
- **Git**

### 1. Clone

```bash
git clone <repo-url> pkmanager
cd pkmanager
```

### 2. Build PKHeX.Core

The project builds PKHeX.Core from source as a local NuGet package:

```bash
./scripts/update-pkhex-core-package.sh
```

### 3. Create Configuration

```bash
cp config.dst config
```

Edit `config` and set real values for `DB_PASSWORD`, `JWT_SECRET`, and any other overrides.  
All keys have documented defaults — see `config.dst` for full reference.

### 4. Initialize Database

```bash
# The startup script handles initdb automatically on first run,
# or initialize manually:
initdb -D data/pgdata --username=pkadmin --pwfile=<(echo "pkadmin123")
```

### 5. Start Development Server

```bash
./scripts/start-dev.sh
```

This launches PostgreSQL, the .NET backend (`:5000` / `:5001`), and the Vite frontend (`:5173`).

Open **https://localhost:5173** in your browser (self-signed certificate — accept the warning).

### 6. Run Health Check

```bash
./scripts/check-health.sh          # Full check (API + diagnostics + smoke)
./scripts/check-health.sh --quick  # API + diagnostics only
```

---

## Development Commands

```bash
# Full stack
./scripts/start-dev.sh              # Start all services
./scripts/start-dev.sh --stop       # Stop all services

# Frontend only (cd client/)
npm run dev                         # Vite dev server (:5173, HTTPS)
npm run build                       # TypeScript check + production build
npm run lint                        # ESLint

# Backend only (cd server/PkManager.Server/)
dotnet run --urls "http://0.0.0.0:5000"
dotnet build

# Database (local PostgreSQL)
psql -h data/pgdata/run -U pkadmin  # Connect to running DB

# Static data seeding (species/moves/abilities/natures/items)
./scripts/seed-static-data.sh --lang zh-Hans
./scripts/seed-static-data.sh --lang all

# PKHeX.Core upgrade
cd sdk/PKHeX && git pull
./scripts/update-pkhex-core-package.sh
```

---

## Architecture

```
Browser (React 19 + Vite 8 + Ant Design 6 + TypeScript)
  ├── Zustand stores (auth / resource / diagnostic / settings)
  ├── @dnd-kit (drag creatures between save boxes ↔ bank)
  ├── mGBA WASM — in-browser GBA emulator (Gen3)
  ├── melonDS WASM — in-browser NDS emulator (Gen4–5)
  ├── pkmanager:// protocol launcher — local DeSmuME (NDS) / Azahar (3DS)
  └── Axios → /api → ASP.NET Core 10 REST API
                         │
        ┌────────────────┼────────────────┐
        │                │                │
   Controllers      Services         Dapper + Npgsql → PostgreSQL 14
   (thin, auth+      (business logic)  (raw SQL, snake_case→PascalCase)
    validation)
                         │
                    PKHeX.Core v26.05.05
                    SaveUtil, PKM, LegalityAnalysis
```

### Backend Layers

- **Controllers** — Thin layer: extract user from `UserContext`, call service, return `ApiResponse<T>`. 9 controllers: Auth, SaveFile, Pokemon, Bank, Emulator, Resource, Settings, Diagnostics, Health.
- **Services** — Business logic: AuthService (JWT + BCrypt), ParseService (PKM ↔ DTO), SaveFileService (save CRUD + bag + trainer), PokemonEditService, BankService, SettingsService, LegalizationService.
- **Middleware** — ExceptionLoggingMiddleware (unhandled exceptions → `data/logs/backend-errors.jsonl`), LanguageMiddleware (Accept-Language / DB preference / query param resolution), Cross-Origin Isolation headers for SharedArrayBuffer.
- **Data** — `DbConnectionFactory` wrapping Npgsql. 8 core tables: `users`, `bank_pokemon`, `save_files`, `save_backups`, `rom_files`, `emulator_save_states`, `user_settings`, `res_*` (5 static-data tables).

### PKHeX Integration

Save files are stored as raw binary (`save_files.raw_save_data` BYTEA + file system under `data/saves/{userId}/{saveFileId}/save.sav`). Creatures are addressed by `(boxIndex, slotIndex)` within the raw save — no independent database IDs for box creatures. All edits write directly into the save binary via PKHeX.Core APIs.

### Emulator Architecture

- **In-browser (GBA/NDS)**: ROM loaded into WASM runtime → save auto-synced every 30 s + on beforeunload via sendBeacon.
- **Local (NDS/3DS)**: Browser attempts `pkmanager://launch/{token}` protocol → falls back to downloaded shell/PowerShell script → script backs up local save → injects pkmanager save (SHA-256 verified) → launches emulator → waits for exit → POSTs save binary back → restores local backup.

---

## Configuration

All configuration lives in `config` (gitignored, derived from `config.dst` template):

| Section | Key | Description |
|---|---|---|
| **Database** | `DB_HOST`, `DB_PORT`, `DB_NAME`, `DB_USER`, `DB_PASSWORD` | PostgreSQL connection |
| **JWT** | `JWT_SECRET`, `JWT_ISSUER`, `JWT_AUDIENCE`, `JWT_EXPIRE_HOURS`, `JWT_REFRESH_DAYS` | Authentication tokens |
| **Server** | `ASPNETCORE_URLS`, `Kestrel:CertPath`, `Kestrel:CertPassword` | Kestrel endpoints |
| **Frontend** | `VITE_API_BASE_URL`, `VITE_HTTPS_KEY`, `VITE_HTTPS_CERT` | Vite dev server |
| **Paths** | `ROM_IMPORT_DIRECTORY`, `DEFAULT_LANG` | Optional overrides |

See `config.dst` for all 16 configuration items with full documentation (bilingual EN/ZH comments).

---

## Project Structure

```
pkmanager/
├── certs/                    # TLS certificates (self-signed)
├── client/                   # React 19 frontend
│   ├── public/
│   │   ├── assets/sprites/pokemon/  # 1025 creature sprites
│   │   ├── emulator/         # mGBA + melonDS WASM runtimes
│   │   └── scripts/          # Launcher scripts (downloadable)
│   └── src/
│       ├── api/              # Axios API layer
│       ├── components/       # Shared + editor/ + bank/
│       ├── constants/        # Game metadata (single source of truth)
│       ├── i18n/             # Client-side localization
│       ├── lib/              # Emulator wrappers + utilities
│       ├── pages/            # Route pages
│       └── stores/           # Zustand state management
├── data/
│   ├── pgdata/               # PostgreSQL 14 data directory
│   ├── saves/                # Save files (runtime)
│   └── logs/                 # Logs (runtime)
├── docs/                     # Technical documentation
│   ├── TODOLIST.md           # Feature tracking
│   └── archive/              # Completed design docs
├── roms/                     # Game ROM files
├── scripts/                  # Dev & deployment scripts
├── sdk/                      # External tool source (PKHeX / Azahar / DeSmuME)
├── server/
│   └── PkManager.Server/     # ASP.NET Core 10 backend
│       ├── Controllers/
│       ├── Services/
│       ├── Models/           # Entity / Request / Response
│       ├── Middleware/
│       ├── Helpers/
│       ├── Data/
│       └── Resources/        # Server-side localization (10 languages)
├── test-data/                # Test save files
├── config                    # Unified configuration (dev, gitignored)
└── config.dst                # Configuration template (committed)
```

---

## Path Conventions

- No hardcoded absolute paths (`/home/…`, `$HOME/…`) in runtime code.
- Backend derives all paths from `IWebHostEnvironment.ContentRootPath` (`server/PkManager.Server/`).
- Frontend and scripts derive paths from their own file location, not `cwd`.
- `SaveFileService` is the single authority for save file I/O — controllers never touch save files directly.
- Shell scripts use `PROJECT_DIR="$(cd "$(dirname "$0")/.." && pwd)"` for self-location.

---

## License

This project is licensed under **GPL-3.0-or-later**. See [LICENSE](LICENSE) for the full text.

---

## Issues & Feedback

If you encounter any errors or unexpected behavior while using the application, please [open an issue]() with:

- Steps to reproduce
- Browser and OS version
- Relevant error messages (check the Diagnostics panel with `Ctrl+Shift+D`)

---

## Acknowledgments

- [PKHeX](https://phonologic-diethylstilboestrol121.github.io) — the core save editing library (GPLv3)
- [mGBA](https://phonologic-diethylstilboestrol121.github.io) — GBA emulator (MPL 2.0)
- [melonDS](https://phonologic-diethylstilboestrol121.github.io) — NDS emulator (GPLv3)
- [ds-anywhere](https://phonologic-diethylstilboestrol121.github.io) — melonDS WASM port
- [Azahar](https://phonologic-diethylstilboestrol121.github.io) — 3DS emulator (GPLv2, Citra successor)
- [DeSmuME](https://phonologic-diethylstilboestrol121.github.io) — NDS emulator (GPLv2)
