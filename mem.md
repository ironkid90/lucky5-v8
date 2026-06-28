# Lucky5 v8 Project Memory

- Primary playable client is the vanilla HTML/CSS/JS cabinet in `server/src/Lucky5.Api/wwwroot/`; backend authority remains in the .NET solution under `server/src/Lucky5.Domain/Game/CleanRoom/`.
- Cabinet visual target is AI9/ai9poker-style portrait arcade parity. Keep the cabinet 9:16, tactile, retro, and asset-driven; do not modernize into a generic flat casino UI.
- AI9 parity overrides live in `wwwroot/css/cabinet-ai9-parity.css`, loaded after other cabinet CSS. It owns final geometry, button PNG mapping, control-deck pointer/z-index safety, and viewport aspect locks.
- Button PNGs live in `wwwroot/assets/images/` and map directly by CSS classes: hold, big, small, cancel_hold, deal_draw, bet, take_half, take_score, and menu. DEAL DRAW must remain red; BET must remain green.
- Menu overlay visibility convention should support both `.is-open` and `.visible`; `body.menu-open` is used for the dim backdrop.
- Central animation timings are in `wwwroot/js/game-config.js`; AI9 parity guide targets are `dealStaggerMs: 350` and `drawStaggerMs: 100`.
- If cabinet DOM elements exist but visuals are missing, first check browser cache/version query strings and the final `cabinet-ai9-parity.css` overrides; stale CSS can preserve clipped paytables/blank control buttons even after source fixes.
- `game.js` preloads cabinet button assets in `preloadAllAssets()`; include `menu.png` there so the square MENU key is warmed with the rest of the AI9 control deck.