# Golden Poker (goldnpkr) Codebase Architecture and Modules

This document provides a highly detailed dissection of the `goldnpkr` ROM assembly code. It serves as an architectural index and module outline, enabling agents and developers to extract logic, replicate gameplay mechanics, and understand the timing and state management of this classic poker game.

## Memory Map Overview

Based on the MAME `goldnpkr.cpp` driver and our disassembly (`disassembly_correct.asm`), the 6502 CPU maps memory as follows:

*   **`$0000 - $07FF`**: NVRAM (Battery-backed RAM). The game uses the Zero Page (`$0000-$00FF`) for high-speed variable access (state, RNG, input buffers) and higher addresses for persistent credits and bookkeeping.
*   **`$0800 - $0801`**: CRTC (MC6845) Video Timings/Address Registers.
*   **`$0844 - $0847`**: PIA 0 (I/O). `0844` is Port A (Input reading). `0846` is Port B (Lamps/Output).
*   **`$0848 - $084B`**: PIA 1 (I/O). `0848` is Port A (DIP Switches). `084A` is Port B (Audio triggers and Multiplexer selection).
*   **`$1000 - $13FF`**: Video RAM (VRAM) - The onscreen tiles.
*   **`$1800 - $1BFF`**: Color RAM (CRAM) - The onscreen colors.
*   **`$2000 - $7FFF`**: Game ROM (Code and fixed data). Mapped directly from the `ups39_12a.bin` file.

---

## Core Modules & Subroutines

### 1. Boot & Initialization (Address: `$572E`)
*   **Trigger**: Hardware RESET vector (`$7FFC`).
*   **Operation**: The game starts at `L572E`. It immediately disables interrupts (`sei`), sets the stack pointer to `$FF` (`txs`), and clears the decimal flag (`cld`).
*   **RAM Test**: It loops through memory checking for corruption. If RAM is determined to be dirty (e.g., first boot or battery failure), it jumps to a wipe routine to zero out NVRAM and reset bookkeeping.

### 2. NMI / VSYNC Interrupt Handler (Address: `$5876`)
*   **Trigger**: NMI vector (`$7FFA`). Fired 60 times a second (60Hz) by the CRTC.
*   **Role**: This is the heartbeat of the game. It controls all animations, stagger delays, input polling, and asynchronous game state changes.
*   **Input Polling (`$5892` and `$58A3`)**: The NMI handler writes selection bits to the PIA 1 Multiplexer (`$084A`) and reads the physical buttons from PIA 0 (`$0844`). It XORs the results with `$FF` (to handle active-low switches) and stores the debounced state in Zero Page memory (`$0054 - $0057`).
*   **Countdown Timers (`$599F`)**: The handler iterates over a block of memory (`$0302` to `$030C`). If any byte is non-zero, it decrements it. This array acts as 11 independent 60Hz countdown timers used by the main game logic to pause, flash text, or stagger dealing animations.

### 3. Hardware RNG / Card Generation (Address: `$4091` & `$4076`)
*   **Role**: Generates a valid card rank (1-13).
*   **Implementation (`$4076`)**: Uses a 4-byte Linear Feedback Shift Register (LFSR) stored at `$00A1 - $00A4`. It performs multiple shifts (`lsr`, `rol`) and XORs (`eor`) across these bytes to generate pseudo-random bits, then rolls the new bit back into `$00A1`.
*   **Validation (`$4091`)**: Loops up to 4 times extracting the lower nibble (4 bits) of `$00A1`. It checks if the value is `0` or `>= 14` (`$0E`). If invalid, it immediately loops back to `$4076` to shift the LFSR again until a valid card rank (1-13) is produced.

---

## Gameplay Mechanics

### Card Dealing Animations & Stagger Time
*   **Logic**: The game does not use traditional "sleep" commands. Instead, it relies on the NMI countdown timers.
*   **Flow**:
    1. The game selects a card using the RNG routine.
    2. It writes the corresponding tile ID directly to Video RAM (`$1000-$13FF`).
    3. It writes a delay value (e.g., `$0A` for 10 frames) into one of the NMI timers (e.g., `$0304`).
    4. The main game loop then enters a spin-wait: `lda $0304 \ bne <wait>`.
    5. The NMI handler decrements `$0304` every 1/60th of a second. Once it hits `0`, the main loop breaks out and proceeds to deal the next card.
*   **Stagger**: By adjusting the value written to the timer, the game controls the exact "snap" or stagger speed of the cards flipping over.

### Double-Up (High/Low) Gameplay
*   **Logic**: A core loop activated after a winning hand, giving the player the chance to double their winnings.
*   **Inputs**: The game checks the Zero Page input buffers (`$0054` area) for the "Big" (Hold 2) or "Small" (Hold 1) bits.
*   **Execution**:
    1. The game displays the dealer's card.
    2. It spin-waits for an input bit to go high in the Zero Page buffer.
    3. Once an input is detected, it triggers the RNG (`$4091`) to draw the player's card.
    4. It compares the rank of the drawn card to the dealer's card.
    5. If (Player > Dealer AND Input == Big) OR (Player < Dealer AND Input == Small), the player wins. The win multiplier logic is then applied, and the score display is updated via VRAM.

### Score Drain / Credit Management
*   **Logic**: Handles the persistent deduction of credits or the slow drain of the score counter when transferring to credits or during attract mode.
*   **Execution**:
    1. Bookkeeping values (like current credits) are stored safely in NVRAM (e.g., `$0010 - $0020`).
    2. During a drain or payout phase, the game uses an NMI timer to pace the countdown (e.g., 2 frames per tick).
    3. When the timer hits 0, it decrements the NVRAM credit value, increments the destination value (if transferring), and triggers a subroutine to redraw the specific VRAM tiles that make up the onscreen digits.
    4. It triggers a sound effect via `PIA 1` (`$0848`) for the "tick" noise.

---

## Extensibility for Future Agents

When adapting this codebase or porting logic to modern engines (like V8 / TypeScript):
1. **Event Loops:** Replace the NMI VSYNC timer spin-waits with `await new Promise(r => setTimeout(r, ticks * 16.66))`.
2. **LFSR RNG:** The `$4076` routine can be perfectly replicated in JS using bitwise operators on an array of 4 integers, preserving the exact "feel" and predictability of the original arcade game.
3. **State Management:** Map the NVRAM block to a `localStorage` or JSON structure. The memory addresses (`$0054` for inputs, `$00A1` for RNG) become keys in a state object.