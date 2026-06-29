# Golden Poker ROM Analysis (`disassembly_offset0.asm`)

This document provides a detailed dissection of the disassembled Golden Poker program ROM (`ups39_12a.bin`). The analysis is based on the disassembly starting at file offset `$0000`, with an understanding of the memory map provided by the MAME driver.

## 1. Key Execution Vectors

From the end of the ROM file, we identified the critical 6502 vectors. These addresses are the entry points for the most important routines in the game.

- **RESET Vector (`$FFFC`): `$572E`**
  - This is the starting point of all execution when the machine is powered on or reset.
- **NMI (Non-Maskable Interrupt) Vector (`$FFFA`): `$5876`**
  - This routine is called every frame (typically 60 times per second) and is the heartbeat of the game, controlling timing, animation, and input polling.
- **IRQ (Interrupt Request) Vector (`$FFFE`): `$59AD`**
  - This is a general-purpose interrupt, though it appears less critical than the NMI in this specific game.

## 2. Code Analysis: The RESET Routine (Entry Point at `$572E`)

The code at `L572E` (the label `da65` assigned to address `$572E`) is the first code to run. Its job is to initialize the entire system.

```assembly
; RESET Vector entry point at $572E
L572E:  sei             ; Disable interrupts
        ldx     #$FF
        txs             ; Set up the stack pointer
        cld             ; Clear decimal mode
        jsr     L570C   ; Call a subroutine
        jsr     L4000   ; Call a subroutine (likely hardware/memory init)
        jsr     L570C   ; Call the same subroutine again
        jsr     L5E75   ; Call another subroutine
```
- The first few instructions are standard 6502 setup: disable interrupts (`sei`), initialize the stack pointer (`ldx #$FF, txs`), and ensure binary arithmetic mode (`cld`).
- It then calls a series of subroutines. `jsr L4000` is likely a key initialization routine that sets up PIAs, the CRTC, and clears the main RAM and Video RAM.
- **NVRAM Check:**
  ```assembly
  ; ... inside the reset routine ...
        ldx     #$2F
  L741: lda     $00,x   ; Reads from the first page of RAM (NVRAM)
        cmp     #$0A
        bcs     L774    ; If value is >= $0A, branch to L774 (init memory)
        dex
        bpl     L741
        ; ... more checks ...
        lda     $0300
        cmp     #$AA    ; Check for signature byte in NVRAM
        bne     L774
        lda     $0301
        cmp     #$55    ; Check for second signature byte
        bne     L774
        jmp     L57B8   ; If checks pass, jump to main game loop
  L774:
        ; This is the "cold boot" or "factory reset" path
        jsr     L570C
        lda     #$00
        ldx     #$00
  L77B: sta     $00,x   ; Clear all of RAM/NVRAM ($0000-$00FF)
        inx
        bne     L77B
        ; ... continues to clear all RAM pages ...
        lda     #$AA    ; Write the signature bytes to NVRAM
        sta     $0300
        lda     #$55
        sta     $0301
        ; ... continues to initialize game state ...
  ```
- **Analysis:** This section is a classic "first boot" check. It looks for specific "magic bytes" (`$AA` and `$55`) in the battery-backed RAM (NVRAM). If they exist, it means the machine was already initialized, and it jumps straight to the main game loop (`L57B8`), preserving old scores and settings. If they *don't* exist (or if other RAM checks fail), it assumes this is the first time the machine has been turned on, and it proceeds to wipe all memory to a clean state (`L774`) before writing the magic bytes for the next boot.

## 3. Code Analysis: The NMI Handler (Timing Loop at `$5876`)

The NMI handler is the most important routine for understanding gameplay flow. It runs on every screen refresh.

```assembly
; NMI Vector entry point at $5876
L5876:  pha             ; Push registers to the stack to preserve them
        txa
        pha
        tya
        pha
        jsr     L4091   ; Subroutine
        jsr     L599F   ; Subroutine - possibly update timers/counters
        jsr     L436D   ; Subroutine
        lda     $030D
        sta     $0846   ; Write to PIA1 Port B (likely controls input mux/lamps)
        jsr     L53AC   ; Subroutine
        jsr     L590F   ; Poll inputs
        ldx     #$03
  L892: ; ... complex input polling loop ...
        ; This loop reads the PIA ports, compares with previous values,
        ; and sets flags for button presses in zero-page RAM (e.g., $5C, $5D)
        dex
        bpl     L892
        ; ... more logic ...
        pla             ; Pull registers from stack
        tay
        pla
        tax
        pla
        rti             ; Return from Interrupt
```

- **Analysis:** This is a dense but typical interrupt handler.
  1.  It saves the CPU state (`pha, txa, pha, tya, pha`).
  2.  It calls `jsr L590F` and the loop that follows, which is responsible for **polling player inputs**. It cycles through the input multiplexer by writing to the PIA at `$0846` and reads the results. It then processes this to detect new button presses.
  3.  It calls various other subroutines that likely handle updating animation counters, sound effects, and other periodic tasks.
  4.  The `rti` instruction at the end returns control to the main program code that was running before the interrupt occurred.

This handler is the direct link to gameplay features:
- **Card Dealing Animation:** The main code might start a deal, then wait in a loop. The NMI handler would be responsible for decrementing a timer value stored in RAM. When the main loop sees the timer reach zero, it draws the next card and resets the timer, creating a paced animation.
- **Score Drain:** In attract mode, the NMI handler can be used to count down a timer. When the timer expires, it calls a subroutine to decrement the credit counter in NVRAM.

## 4. Dissection of the Graphics ROMs

The files `u38_5a.bin`, `u40_4a.bin`, and `u43_2a.bin` contain the graphics, not code.

- **`u38_5a.bin` (char ROM):** This ROM contains the tile data for the font and other simple UI elements.
- **`u43_2a.bin` and `u40_4a.bin` (card ROMs):** These contain the graphics for the deck of cards.

### Graphics Format: Planar Bitmaps

The `ROM_START` block gives us clues about the format:
```cpp
ROM_LOAD( "u43_2a.bin", 0x0000, 0x2000, ... )    // cards deck gfx, bitplane1
ROM_LOAD( "u40_4a.bin", 0x2000, 0x2000, ... )    // cards deck gfx, bitplane2
ROM_COPY( "gfx1",   0x4800, 0x4000, 0x0800 )    // cards deck gfx, bitplane3
```
- This is a **3-bit-per-pixel (3bpp)** planar format. This means each pixel's color is determined by 3 bits, allowing for 2^3 = 8 colors per tile.
- The data for each bit is stored in a separate "plane".
- To get the color for a single pixel, you would take:
  - 1 bit from the `u43_2a.bin` ROM (bitplane 1)
  - 1 bit from the `u40_4a.bin` ROM (bitplane 2)
  - 1 bit from the `u38_5a.bin` ROM (bitplane 3, copied from the char ROM region)
- These 3 bits are combined to form a color index from 0-7, which is then looked up in the color PROM (`tbp24s10n.7d`) to get the final RGB value to display on screen.

### Visualization

To "dissect" the graphics, one would write a small program (e.g., in Python with PIL/Pillow) to:
1.  Read the three graphics ROM files into byte arrays.
2.  Iterate through the data, treating it as a sequence of 8x8 pixel tiles.
3.  For each pixel in a tile, fetch the corresponding bit from each of the three "planes".
4.  Combine the 3 bits to form a color index.
5.  Use a predefined palette (or one read from the color PROM) to draw the pixel.
6.  Render all the tiles to an image file.

This process would produce a complete "tileset" image, showing all the character fonts and every card in the deck, which could then be easily used or upgraded in other projects.
