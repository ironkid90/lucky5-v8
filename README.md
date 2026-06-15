# Lucky5 v8

Web-native Lucky 5 cabinet built around the existing .NET backend and CleanRoom game logic.

This repo starts from the strongest reusable parts of `Lucky5-v7` and deliberately drops the Godot-first web lane. The product direction here is:

- `server/src/Lucky5.Domain/Game/CleanRoom/` stays authoritative for RNG, payouts, jackpots, credits, recovery, and double-up rules
- `server/src/Lucky5.Api/wwwroot/` is the primary cabinet client
- the cabinet remains portrait, dense, tactile, and AI9-inspired
- Godot migration artifacts are not part of this repo

## Quick Start

```powershell
# API + browser
.\dev.ps1

# API only
.\dev.ps1 -Headless
```

The launcher sets `PORT=5051` by default, so the cabinet opens at `http://localhost:5051`.

## Credentials

| Username | Password | Role |
| --- | --- | --- |
| `admin` | `admin123` | Admin |
| `tester` | `password` | Player |

## Core Architecture

- `server/src/Lucky5.Api/` - ASP.NET Core host, controllers, auth, SignalR, static cabinet
- `server/src/Lucky5.Application/` - DTOs, contracts, request models
- `server/src/Lucky5.Domain/` - domain entities and deterministic CleanRoom game logic
- `server/src/Lucky5.Infrastructure/` - services, repositories, persistence
- `server/src/Lucky5.Realtime/` - realtime hub + services
- `server/tests/Lucky5.Tests/` - regression suite
- `docs/` - gameplay, cabinet feel, architecture decisions, and carried-forward reference material

## Validation

```powershell
dotnet build server/Lucky5.sln
dotnet run --project server/tests/Lucky5.Tests/Lucky5.Tests.csproj
```

## Intentional Differences From v7

- no Godot client
- no Next.js or Netlify split frontend
- no iframe-based Godot web shell
- docs are rewritten around a single web-native cabinet path

## Documentation

- [docs/README.md](docs/README.md)
- [docs/WEB_NATIVE_STRATEGY.md](docs/WEB_NATIVE_STRATEGY.md)
- [docs/LUCKY5_AUTHORITATIVE_GAMEPLAY_REFERENCE.md](docs/LUCKY5_AUTHORITATIVE_GAMEPLAY_REFERENCE.md)
- [docs/GAME_FEEL_REFERENCE.md](docs/GAME_FEEL_REFERENCE.md)
