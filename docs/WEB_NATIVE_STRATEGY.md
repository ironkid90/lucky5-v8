# Lucky5 v8 Web-Native Strategy

## Decision

Lucky5 v8 treats the web cabinet as the main product, not as a fallback to an engine export.

## What v8 Keeps

- the .NET backend and API surface
- CleanRoom game logic in `server/src/Lucky5.Domain/Game/CleanRoom/`
- the current ASP.NET-served cabinet in `server/src/Lucky5.Api/wwwroot/`
- AI9-driven presentation references, button semantics, and gameplay timing docs
- operator/admin flows, cabinet-device auth, jackpots, and recovery behavior

## What v8 Drops

- Godot as the primary web client
- the split Next.js hosted shell
- iframe-first engine embedding
- engine-specific docs and tests as active delivery requirements

## Lessons Carried Forward

1. The core product value is backend authority plus cabinet feel, not the renderer.
2. Dense portrait cabinet UI is easier to evolve in HTML/CSS/JS than in a web-exported engine scene.
3. Delivery constraints matter as much as visuals: startup cost, iframe behavior, audio latency, and mobile recovery all shape the product.
4. v8 should port useful behavior from prior experiments without porting the prior frontend architecture one-to-one.

## First Foundation Scope

This initial v8 import does four things:

1. brings the backend, tests, and current ASP.NET cabinet into a clean repo
2. switches local launch behavior to web-first
3. removes Godot-first regression gates
4. establishes fresh docs around the new primary path

## Next Implementation Slices

- modularize the current `wwwroot` cabinet scripts into clearer presentation/state/effects boundaries
- port proven Godot-side timing and polish improvements into the web cabinet
- introduce explicit asset and payload budgets
- add browser-focused visual/audio regression checks
