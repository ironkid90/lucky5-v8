# Lucky5 v8 Docs

Single delivery path: web-native cabinet served by the ASP.NET host at `http://localhost:5051`.

## Current Truth (always up to date)

**[../mem.md](../mem.md)** — VSYNC timing, card design standards, button system, file versions, known pitfalls. Start here for any engineering task.

## Primary References

- [DEVELOPMENT_HISTORY_AND_CURRENT_STATE.md](DEVELOPMENT_HISTORY_AND_CURRENT_STATE.md) — full development history, strategic pivots, notable achievements, lessons
- [LUCKY5_AUTHORITATIVE_GAMEPLAY_REFERENCE.md](LUCKY5_AUTHORITATIVE_GAMEPLAY_REFERENCE.md) — gameplay and presentation rules the cabinet must honor
- [MACHINE_BEHAVIOR_REFERENCE.md](MACHINE_BEHAVIOR_REFERENCE.md) — machine state machine, double-up flow, idle/attract behavior
- [GAME_FEEL_REFERENCE.md](GAME_FEEL_REFERENCE.md) — visual feel, pacing, button colors, cabinet proportions
- [WEB_NATIVE_STRATEGY.md](WEB_NATIVE_STRATEGY.md) — why v8 exists, what carried over from v7, what was intentionally dropped
- [CABINET_VARIANT_ARCHITECTURE.md](CABINET_VARIANT_ARCHITECTURE.md) — variant engine architecture, config-driven game-feel knobs
- [assets/lucky5-cabinet-assets.md](assets/lucky5-cabinet-assets.md) — card/button asset notes and carry-forward constraints

## AI9 Parity (historical worklogs)

These documents chronicle the AI9 parity implementation journey. They are **historical** — current timing values (VSYNC-locked at 60Hz, staggerFrames=12) live in [../mem.md](../mem.md).

- [AI9_PARITY_GROUND_TRUTH_AND_WORKLOG.md](AI9_PARITY_GROUND_TRUTH_AND_WORKLOG.md) — frame-by-frame analysis worklog
- [AI9_PARITY_IMPLEMENTATION_SUMMARY.md](AI9_PARITY_IMPLEMENTATION_SUMMARY.md) — implementation summary (pre-VSYNC)
- [AI9_PARITY_AGENT_IMPLEMENTATION_GUIDE.md](AI9_PARITY_AGENT_IMPLEMENTATION_GUIDE.md) — agent implementation guide (pre-VSYNC)
- [AI9_OVERHAUL_SPECIFICATION.md](AI9_OVERHAUL_SPECIFICATION.md) — overhaul specification

## Interpretation Notes

- When older reference material says "Godot", read as historical context from v7 migration.
- Gameplay authority: `server/src/Lucky5.Domain/Game/CleanRoom/`, not the web client.
- Active cabinet runtime: `server/src/Lucky5.Api/wwwroot/`.
- "Golden Poker" / "Bonanza" references are AI9 cabinet variants — treat as reference points, not targets.
