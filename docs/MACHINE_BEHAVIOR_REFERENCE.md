# Lucky5 Machine Behavior Reference

**Status:** Authoritative operational specification for the Lucky5 cabinet.
**Audience:** Any agent working on Lucky, cabinet frontend, game policy, or session management.
**Last updated:** 2026-06-16

---

## Table of Contents

1. [Design Philosophy](#1-design-philosophy)
2. [Wallet vs Machine Credits — Separation of Concerns](#2-wallet-vs-machine-credits)
3. [Cash-In Behavior](#3-cash-in-behavior)
4. [Betting and Paytable Display](#4-betting-and-paytable-display)
5. [Machine Credit Limit and Close Behavior](#5-machine-credit-limit-and-close-behavior)
6. [Jackpot Wins — Mini Machine Close](#6-jackpot-wins)
7. [Double-Up Round — Complete Flow](#7-double-up-round)
8. [Loss Siphon Behavior](#8-loss-siphon-behavior)
9. [Session Pause and Reconnection](#9-session-pause-and-reconnection)
10. [Siphon Drain Timing](#10-siphon-drain-timing)
11. [Animation and Pacing Reference](#11-animation-and-pacing-reference)
12. [RTP Policy and Long-Term Operation](#12-rtp-policy-and-long-term-operation)
13. [Jackpot Accumulation](#13-jackpot-accumulation)
14. [What NOT to Change](#14-what-not-to-change)

---

## 1. Design Philosophy

This machine simulates a **Lebanese Bonanza-style arcade video poker cabinet** from the 1990s. Every design decision serves one goal: **the player should feel like they are sitting in front of a real mechanical machine**, not a web app.

### Core Principles

- **Mechanical, not cinematic.** No particle effects, no screen shake, no coin showers. Every animation is a physical motion — a card flipping, a counter ticking, a button depressing.
- **Deliberate pacing.** Cards reveal one at a time. Wins drain slowly enough to feel like achievement. Nothing happens instantly unless it's a mechanical snap (button press).
- **The machine has memory.** Jackpots accumulate for weeks. RTP converges over hundreds of rounds. The machine remembers streaks, droughts, and payout history.
- **Disconnect tolerance.** A player can walk away for 5 minutes and come back to find their machine exactly as they left it — mid-DU, mid-siphon, any state.

---

## 2. Wallet vs Machine Credits

The machine has **two completely separate currency tracks**.

### Wallet Balance
- This is the player's account balance on the server.
- **Only used for:** Depositing into machine credits (cash-in), and receiving machine credits (cash-out).
- **Never displayed on the game screen during play.** The player only sees this in the lobby/wallet screen.
- Wallet balance is irrelevant to paytable values, bet amounts, and game logic.

### Machine Credits
- This is the "coins in the machine" — the value shown in the **CREDIT** display during gameplay.
- **Only used for:** Placing bets, receiving payouts, entering Double-Up, jackpot wins, everything during active play.
- **Displayed prominently** in green at the top-right of the cabinet during play.
- Machine credits are the **only** value the paytable cares about.

### Key Invariant
```
During gameplay, the player NEVER sees their wallet balance.
The CREDIT display always shows machine credits.
Wallet balance is only visible in the lobby/wallet screen and cash-in/cash-out prompts.
```

---

## 3. Cash-In Behavior

### How it works
1. Player opens the menu (MENU button) and presses **CASH IN**
2. They can deposit **any positive amount** up to their total available wallet balance
3. If the player has less than the standard increment, they can deposit whatever they have — even their last few credits
4. The deposited amount is deducted from wallet and added to machine credits
5. Paytable values update immediately based on the new machine credits

### Important Details
- **No fixed increments enforced.** If a player has 47,000 in their wallet, they can deposit all 47,000. The paytable scales accordingly.
- **No enforced minimum** beyond "positive amount." Even 1 credit can be deposited.
- **Session cap** of 1,000,000 still applies for normal deposits, but if the player's remaining balance is below the cap, they can deposit it all (last-deposit allowance).
- **Closed machine blocks cash-in.** Must cash out first.
- Paytable values after cash-in scale linearly with the new bet amount (which itself may be limited by the new credits).

---

## 4. Betting and Paytable Display

### Bet Amount
- Each machine has a **fixed min/max bet** (e.g., 5,000–10,000). The player cycles through bet levels with the BET button.
- The player **cannot bet more than they have in machine credits divided by 2** (because both deal and draw cost the bet amount).
- Bet amounts are **not** player-configurable to arbitrary values — only the preset levels for that machine tier.

### Paytable Display
- Paytable values = `hand_multiplier × current_bet`
- The paytable shows **theoretical** values — what the hand would pay at the current bet
- Paytable rows light up when a winning hand is formed
- The Full House row has a special **FH jackpot tag** displayed directly below the 2 PAIR row, showing the currently-armed Full House rank and its jackpot value in a white/gold badge

### Key Invariant
```
Paytable always shows multiplier × bet.
Paytable does NOT show "remaining credits" or "capped to 40M."
The paytable is a fixed reference — like the glass on a real machine.
Actual settlement handles the cap, not the display.
```

---

## 5. Machine Credit Limit and Close Behavior

### The Close Threshold
The machine has a **fixed close threshold of 40,000,000 machine credits**. When machine credits reach or above this value, the machine **closes**.

### What "Closed" Means
- **No new deals.** The DEAL/DRAW button is disabled. BET is disabled.
- **No new cash-in.** Must cash out first.
- **The machine credit display stops at 40M+.** The player sees their actual balance (which may exceed 40M from DU wins).
- **The paytable does NOT change.** It still shows theoretical values based on bet. The machine doesn't artificially lower paytable numbers.

### How the Machine Closes
The machine can close from several events:
1. **Base game win** pushes credits to/above 40M
2. **Double-Up win** pushes credits to/above 40M
3. **Take-Half** credits half to machine, pushing to/above 40M

### What Happens After Close
- **Auto-siphon starts immediately.** The excess winnings automatically drain from the display into the credit counter. No player input needed.
- **TAKE HALF is instant.** If the player presses TAKE HALF, half is credited immediately to machine credits (and may push further above 40M), the remaining half stays in DU.
- **After siphon completes, auto cash-out.** The machine credits are transferred to the player's wallet automatically. Then the machine resets to idle.
- **Exception:** If the player is still in DU when the machine closes, they can continue DU play. After each DU guess, if the result is MachineClosed, the auto-siphon runs.

### Theoretical Maximum from One Session
A player starting near the limit can theoretically win up to **~3× the close threshold** in a single session:
1. Start at ~39M credits, win base game → ~39M in win amount
2. Enter DU, double → ~78M in DU score
3. Take Half → ~39M credited instantly, ~39M remains in DU
4. DU double again → ~78M total

The auto-siphon handles draining all of this gradually.

---

## 6. Jackpot Wins

### Jackpot Types
| Hand | Jackpot Pool | Starting Value | Cap | Contribution/Round |
|------|-------------|---------------|-----|-------------------|
| Full House (ranked) | JackpotFullHouse | 90,000 | 650,000 | 68 |
| Four of a Kind A | JackpotFourOfAKindA | 140,000 | 1,000,000 | 85 (only when starred) |
| Four of a Kind B | JackpotFourOfAKindB | 140,000 | 1,000,000 | 0 (only when starred) |
| Straight Flush | JackpotStraightFlush | 850,000 | 7,500,000 | 152 |
| Kent (5-of-a-kind) | JackpotKent | 500,000 | 5,000,000 | 200 |

### Jackpot Win Behavior — "Mini Machine Close"
When a jackpot is won, the machine behaves like a mini close:

1. **Everything freezes.** No buttons work. The player sees the jackpot banner.
2. **Jackpot counter drains** slowly into the credit counter. The jackpot counter (e.g., FH meter) counts down while the CREDIT counter counts up by the same amount. This is a single coupled animation.
3. **Duration scales with amount.** A 40M jackpot takes ~60 seconds to drain. This gives the player time to feel the weight of their win — watching millions tick into their credit balance, exactly like the mechanical counters on old machines.
4. **After drain completes**, the 4 OF A KIND WINS BONUS banner pulses.
5. **Then the DU page appears** (if the player has a positive win). The game transitions to the Double-Up screen.
6. **If the jackpot pushed credits past 40M**, the machine closes and the full auto-siphon sequence begins (Section 5).

### Jackpot Accumulation
- Jackpots accumulate from **every round** via contributions, regardless of outcome.
- The 4OAK slot alternates between A and B. Only the starred slot contributes each round.
- Jackpots grow until won. A Full House with the wrong rank does NOT reset the FH jackpot.
- When a jackpot is won, it resets to its start value.
- FH rank rotates (2 → 3 → ... → A → 2) after each FH jackpot payout.

---

## 7. Double-Up Round — Complete Flow

### Entry
After a base game win, the player sees:
- Paytable row highlighted with the win amount
- Message: "WIN: [amount] - DOUBLE UP"
- BIG and SMALL buttons become active

Pressing BIG or SMALL (or auto-enter after delay) starts the DU round.

### DU Board Layout
- 5 card slots displayed horizontally
- Slot 1: The **dealer card** (revealed from the previous game's win)
- Slots 2-5: Initially show card backs on earlier pages, the active slot shuffles

### Per-Guess Cycle
1. **Shuffle phase:** The next available slot cycles through random card faces every ~80ms (visual only — the server already determined the result)
2. **Player presses BIG or SMALL**
3. **Card snaps to the server-determined result** (not the current shuffle frame)
4. **Reveal:** The challenger card is shown face-up with a brief glow
5. **Resolve:**
   - **Win:** Card stays in slot, amount doubles, show WIN message, brief hold, then advance to next slot
   - **Tie (equal rank):** Typically treated as a push or loss depending on ruleset
   - **Lose:** Show losing card briefly, then siphon all DU display amount back to zero
   - **SafeFail (5♡ active):** Show losing card, flash white, siphon the accumulated amount to credits safely

### DU Loss — Showing the Losing Card
When the player loses a DU guess:
1. The **losing challenger card is displayed** for ~1.2 seconds so the player sees what beat them
2. Then the DU winnings (current display amount) **siphon back to zero** via drain animation
3. The drain uses the same timing formula as other siphons (amount / 1M × 1.5s)
4. After siphon, exit DU and return to idle

### DU Win — Advancing
When the player wins a DU guess:
1. The winning card is shown with a brief glow (~650ms visible)
2. Then the trail advances — the card is added to the trail with the guess label (BIG/SMALL)
3. The next slot starts shuffling
4. If all 5 slots fill, the round resolves and a new DU page starts with the last card as the new dealer

### Page Model
- The DU trail can span multiple pages (4 trail cards + 1 active slot = 5 visible per page)
- The last card of page N carries over as the first card of page N+1
- This gives visual continuity across pages

### DU with 5♠ Never Lose
- When the "Lucky 5" state is active, the rules panel glows at full brightness
- If the challenger card is 5♡ and the guess was wrong, the result is SafeFail instead of Lose
- The accumulated amount is credited to machine credits and the round ends safely
- A brief white flash marks the 5♡ event

---

## 8. Loss Siphon Behavior

### DU Loss
When the player loses a DU round with accumulated winnings:
1. Show the losing card (~1.2s) — **player sees what beat them**
2. Animate the DU display amount draining back to zero
3. Use `animateDrainToCredits` with proper timing
4. Credits count DOWN, display counts DOWN simultaneously

### Base Game Loss
On a base game loss (no win):
- Brief message, cards dim, return to idle
- No siphon needed (nothing accumulated)

---

## 9. Session Pause and Reconnection

### Disconnect Behavior
When a player disconnects from a machine (network issue, browser close, etc.):
1. The machine **does NOT immediately reset**
2. A **5-minute grace period timer** starts
3. The machine appears **occupied/busy** to other players in the lobby (isOccupied = true, state = "Reconnecting")
4. The player's game state is fully preserved: DU progress, win amounts, jackpot state, everything
5. The SignalR seat-occupancy lock is held

### Reconnection
When the player reconnects (within 5 minutes):
1. They rejoin the machine via `JoinMachine` or `ReconnectSync`
2. The SignalR lock is restored
3. The pending disconnect timer is cancelled
4. The machine sends the latest cabinet snapshot + event replay
5. The game state is fully restored — they continue exactly where they left off
6. **This works for any state:** mid-DU, mid-siphon, mid-deal, any phase

### Grace Period Expired
When the 5-minute timer expires:
1. The machine lock is released
2. The machine appears available to other players
3. The server-side session data is still preserved (machine credits, rounds, etc.)
4. If the player returns after the grace period, they start a fresh session on that machine
5. Their previous machine credits are still in the session and can be cashed out

---

## 10. Siphon Drain Timing

### Duration Formula
```javascript
duration = Math.min(countUpMaxMs, Math.max(countUpMinMs, amount / 1_000_000 * 1500))
```

### Timing Table
| Amount | Duration | Context |
|--------|----------|---------|
| 500K | 750ms | Small win |
| 1M | 1.5s | Regular win |
| 10M | 15s | Medium jackpot |
| 20M | 30s | Large jackpot |
| 40M | 60s | Maximum jackpot |

### Behavior During Siphon
- The **credit counter counts UP** linearly (easement curve)
- The **win display counts DOWN** by the same amount simultaneously
- The **jackpot counter counts DOWN** (for jackpot wins)
- The **message** shows "SIPHONING WINNINGS..." or "COLLECTING..."
- **All buttons are disabled** during siphon
- After siphon: credits are finalized, settle with server, cash out

### Ease Curve
The drain uses ease-out cubic: `1 - (1 - progress)³`. This means:
- Fast start (the first third of the amount drains relatively quickly)
- Slow finish (the last third drains more slowly, building anticipation)
- This mimics mechanical counters that slow down as they approach the target

---

## 11. Animation and Pacing Reference

All timing values are in `game-config.js` under `GAME_CONFIG.timing`. The cabinet uses a locked 9:16 portrait coordinate system. All durations in milliseconds.

### Card Dealing
| Phase | Duration | Notes |
|-------|----------|-------|
| Deal base delay | 120ms | Pause before first card (cabinet "thunk") |
| Deal stagger | 180ms | Between each card landing — visible one-at-a-time drop |
| Deal animation | 300ms | Slide + flip per card (visible motion) |
| Total deal (5 cards) | ~940ms | 120 + 4×180 + 300 |
| Auto-hold delay | ~1100ms | After deal before showing holds |

### Card Draw
| Phase | Duration | Notes |
|-------|----------|-------|
| Draw out | 100ms | Fade-out / flip-out of replaced cards |
| Draw in | 150ms | Fade-in / flip-in of new cards |
| Draw stagger | 100ms | Between replaced card slots |
| Draw result delay | 120ms | After cards settle before showing result |

### Double-Up
| Phase | Duration | Notes |
|-------|----------|-------|
| Shuffle frame | 130ms | Card face cycle interval (visible reel spin) |
| Reveal delay | 250ms | After server response before showing challenger |
| Win hold | 900ms | Card visible before advancing trail (player sees the win) |
| Lose reveal | 1500ms | Losing card visible before siphon starts |
| Stagger per card | 120ms | On fresh DU pages |

### Win/Loss
| Phase | Duration | Notes |
|-------|----------|-------|
| Credit tick | 120ms | Digit flash toggle interval (mechanical reel tick) |
| DU → idle delay | 1000ms | After loss siphon, before idle |
| Post-loss title | 1200ms | Before idle title shows after loss |
| Win → DU prompt | 800ms | Before auto-launching DU after win |
| Take-half continue | 800ms | Before re-offering DU after take-half |

### Drain/Siphon
| Config | Value | Notes |
|--------|-------|-------|
| countUpMinMs | 1000 | Minimum drain duration |
| countUpMaxMs | 65000 | Maximum drain duration |
| Formula | amount / 1M × 1.5s | Linear scaling |

---

## 12. RTP Policy and Long-Term Operation

### Target RTP: 80%
The machine targets **80% return to player** over its lifetime. This is not per-session — it's the long-term convergence target.

### RTP Components
| Component | Target Share | Pool |
|-----------|-------------|------|
| Base game wins | ~67% of credits in | Direct payouts |
| Jackpot wins | ~3.25% of credits in | 4 jackpot pools |
| Double-Up wins | ~12% of credits in | DU session amounts |
| House edge | ~17.75% | Operator profit |

### Convergence Horizon: 320 rounds
The policy controller ramps up correction over the first 320 rounds. After this, it has full authority to adjust payout scales. This means:
- The first ~30 rounds are "warmup" — generous opening
- Rounds 30-320 gradually increase control
- After round 320, full active control

### Pity System
The machine tracks consecutive losses and increases payout likelihood:
- **4+ consecutive losses:** Small pity boost begins (scale +0.02)
- **8+ consecutive losses:** Full pity boost (scale +0.06)
- **12+ consecutive losses:** Crisis mode (additional +0.05)
- **15+ rounds since medium win:** Additional pity (+0.02)

### Jackpot Leak
If jackpot RTP exceeds the soft cap (3%), the policy applies negative adjustment to base game payouts. This prevents jackpots from growing indefinitely and ensures the machine returns to target RTP.

### Soft Caps
| Level | Value | Behavior |
|-------|-------|----------|
| SoftCapWarning | 28M | Gradual pressure increase begins |
| SoftCapHard | 35M | Full pressure, distribution shifts hot |
| CloseThreshold | 40M | Machine closes |

### Long-Term Persistence
- **MachineLedgerState** persists in memory for the server's lifetime
- Can be persisted to disk via `FilePersistentStateStore` config
- Jackpot values, RTP counters, streak history, round count — all survive restarts when file persistence is configured
- No automatic reset — the machine keeps running for weeks/months
- Jackpots accumulate naturally as long as the machine is played

---

## 13. Jackpot Accumulation

Every round contributes to jackpots **regardless of outcome**:
- FullHouse: +68 credits to FH jackpot
- 4OAK: +85 credits to the starred slot (A or B, alternating)
- StraightFlush: +152 credits to SF jackpot  
- Kent: +200 credits to Kent jackpot

A typical machine being played regularly will see jackpots grow steadily. A Full House jackpot starting at 90K will reach its 650K cap after ~8,000 rounds (assuming no FH jackpot wins). The Straight Flush jackpot grows more slowly due to higher contribution but also higher cap.

---

## 14. What NOT to Change

### Critical Invariants
1. **Never reset MachineLedgerState** without explicit operator action. The ledger IS the machine's memory.
2. **Never change CloseThreshold** without recalibrating the entire policy. All soft caps are relative to it.
3. **Never make the drain instantaneous.** The slow drain IS the player's reward moment. Making it instant removes the achievement feeling.
4. **Never release the machine lock on disconnect instantly.** The 5-minute grace period is essential for network resilience.
5. **Never show wallet balance during gameplay.** Wallet and machine credits must remain separate concepts.
6. **Never allow arbitrary bet amounts.** Bet must be one of the preset machine tiers.
7. **Never skip card reveals.** Every card must be visible for the minimum durations specified above.
8. **Never change the siphon formula's proportionality.** The amount/duration relationship creates the correct feedback. If you change the constant, you break the feel.
9. **Never remove the ConvergenceHorizon ramp.** Abrupt policy correction feels rigged. The gradual ramp feels natural.
10. **Never hardcode paytable values.** They must always be `multiplier × current_bet`, read from the backend's Rules dictionary.
