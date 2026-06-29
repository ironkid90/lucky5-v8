# Deeper Analysis: Golden Poker Gameplay Emulation

This document explores how the `goldnpkr.cpp` driver emulates the hardware that the original Golden Poker game code uses to implement its gameplay, animations, and timings.

The core concept to remember is that the C++ code does not contain the game's rules. It creates a virtual circuit board, and the original game's assembly code (from the ROMs) runs on it.

## 1. Card Dealing Animations, Timings, and Stagger

The smooth "animation" of cards dealing, the delay between events, and the staggering of effects are all controlled by the game's original code, which uses the emulated hardware for timing.

### Hardware Clocks and Interrupts

- **CPU Clock:** The speed of the entire game is dictated by the master CPU clock speed. In the `machine_config`, you'll see a line like this:
  ```cpp
  m_maincpu->set_addrmap(AS_PROGRAM, &goldnpkr_state::goldnpkr_map);
  m_maincpu->set_cpu_clock(XTAL(4'000'000) / 4); // 1 MHz
  ```
  This tells the emulator to run the M6502 CPU at 1 MHz. The game's code, with its delay loops and instruction cycles, is synchronized to this clock.

- **VSYNC Interrupts:** The most common method for timing animations in old games is to synchronize with the screen's refresh rate. The `mc6845_device` (CRTC) is configured to generate an interrupt at the end of each video frame (the "Vertical Sync" or VSYNC).
  ```cpp
  MC6845_RECONFIG(config, m_crtc, 0);
  m_crtc->set_show_border_area(false);
  m_crtc->set_char_width(8);
  m_crtc->out_vsync_callback().set_inputline(m_maincpu, INPUT_LINE_NMI);
  ```
  This configuration tells the emulated CRTC to trigger the CPU's **NMI (Non-Maskable Interrupt)** line on every VSYNC. The game's ROM contains an NMI interrupt handler—a special subroutine that runs automatically 60 times per second.

- **How it works:**
  1.  The game code decides to deal a card.
  2.  It writes the tile for the card back to the video RAM.
  3.  It then enters a waiting loop.
  4.  The VSYNC interrupt occurs. The interrupt handler runs and might decrement a counter.
  5.  The game's main code sees the counter has changed and proceeds to deal the next card.

This interrupt-based counting is how the game achieves consistent timing for card dealing, flashing text ("JACKPOT!"), and other timed events. The "stagger" is simply the game code waiting for a few VSYNC interrupts between actions.

## 2. Double-Up Gameplay (High/Low)

The double-up feature is a pure logic loop in the game's assembly code. The C++ driver's only role is to provide the inputs and display the result.

### Reading Player Input

- **Input Ports:** First, the driver defines the buttons. In `INPUT_PORTS_START(goldnpkr)`, we see the definitions for the double-up controls. These are often named "High" (Big) and "Low" (Small).
  ```cpp
  PORT_START("IN2")
  PORT_BIT( 0x01, IP_ACTIVE_LOW, IPT_POKER_HOLD1 ) PORT_NAME("Hold 1 / Small")
  PORT_BIT( 0x02, IP_ACTIVE_LOW, IPT_POKER_HOLD2 ) PORT_NAME("Hold 2 / Big")
  // ... and so on
  ```
  Here, "Small" (Low) and "Big" (High) are mapped to the same physical inputs as Hold 1 and Hold 2.

- **PIA and Multiplexing:** The game code reads these buttons through the `6821 PIA`. As mentioned in the previous analysis, the inputs are multiplexed.
  1.  The game code writes to PIA1 to select an input group (e.g., group '2' for the "IN2" inputs).
  2.  It then reads from PIA0.
  3.  The C++ function `goldnpkr_mux_port_r` intercepts this read, checks which group is selected, and returns the state of the appropriate `ioport`.

The game ROM now has the button state ("High" or "Low"). It then runs its internal random number generator to determine the next card, compares it to the current card, and checks if the player's choice was correct.

## 3. Score Drain and Credit Management

The player's score and credits are stored in a special area of memory that persists even when the machine is turned off.

- **NVRAM (Non-Volatile RAM):** The driver configures a portion of the memory map as NVRAM.
  ```cpp
  // In the machine_config:
  NVRAM(config, "nvram", nvram_device::DEFAULT_ALL);

  // In the memory map:
  map(0x0000, 0x07ff).ram().share("nvram");
  ```
  This tells MAME that the memory range from `0x0000` to `0x07FF` is battery-backed. MAME saves the contents of this memory area to a file when you close the game and loads it back when you start it again.

- **Score Drain Logic:** The "score drain" or "credit drain" during attract mode is a simple loop in the game's assembly code.
  1.  The game code checks if the machine is idle (i.e., no game is being played).
  2.  It uses its VSYNC-based timers to wait for a certain amount of time.
  3.  After the time has elapsed, it reads the current credit value from an address in NVRAM (e.g., `0x0010`).
  4.  It decrements the value and writes it back to the same address.
  5.  It updates the score display on the screen by writing the new numbers to video RAM.

The C++ driver does not have any logic for this; it just faithfully emulates the CPU that is executing these instructions and the RAM where the credits are stored.

### Summary

To truly understand the gameplay logic, you would need to use a **6502 disassembler** on the `goldnpkr` ROM files. This would convert the machine code back into human-readable assembly instructions. You could then trace the program flow to see exactly how it handles card shuffling, payout calculations, and the double-up logic. The C++ driver acts as the perfect, predictable hardware foundation that makes this complex game logic possible.
