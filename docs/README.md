# Lucky5 v8 Docs

The repo now has a single delivery path: a web-native cabinet served by the ASP.NET host.

## Primary References

- [DEVELOPMENT_HISTORY_AND_CURRENT_STATE.md](DEVELOPMENT_HISTORY_AND_CURRENT_STATE.md) - fully curated global development history, major achievements, strategic pivots, notable mistakes, and technical module state
- [WEB_NATIVE_STRATEGY.md](WEB_NATIVE_STRATEGY.md) - why v8 exists, what carried over from v7, and what was intentionally dropped
- [LUCKY5_AUTHORITATIVE_GAMEPLAY_REFERENCE.md](LUCKY5_AUTHORITATIVE_GAMEPLAY_REFERENCE.md) - gameplay and presentation rules that the cabinet must honor
- [GAME_FEEL_REFERENCE.md](GAME_FEEL_REFERENCE.md) - visual feel, pacing, button colors, and cabinet proportions
- [assets/lucky5-cabinet-assets.md](assets/lucky5-cabinet-assets.md) - card/button asset notes and carry-forward constraints

## Interpretation Notes

- When older reference material says "Godot", read that as historical context from the v7 migration attempt unless the note is explicitly about engine-specific behavior.
- The authority boundary did not change: gameplay truth lives in `server/src/Lucky5.Domain/Game/CleanRoom/`, not in the web client.
- The active cabinet runtime is `server/src/Lucky5.Api/wwwroot/`.
