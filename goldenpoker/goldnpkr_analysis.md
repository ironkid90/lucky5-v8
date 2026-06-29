# Analysis of goldnpkr.cpp MAME Driver

This document provides a detailed breakdown of the `goldnpkr.cpp` MAME driver file. The file is a comprehensive driver for a vast collection of arcade poker machines and their variants, primarily from the 1980s and 1990s. It emulates various hardware configurations based on the M6502 CPU and peripheral chips like the MC6845 CRTC and 6821 PIA.

## 1. Core Structure: Driver State Classes

The driver is organized around C++ classes that hold the state of the emulated machine.

### `goldnpkr_state`
This is the primary driver class. It inherits from MAME's `driver_device` and encapsulates the core hardware components shared by most games in this family.

- **Key Members:**
  - `m_maincpu`: The main CPU (typically a `M6502` or a variant like `R65C02`).
  - `m_pia[2]`: An array of two `pia6821_device`s for handling I/O.
  - `m_screen`, `m_gfxdecode`, `m_palette`: Standard MAME devices for video rendering.
  - `m_crtc`: An `mc6845_device` for video timing generation.
  - `m_videoram`, `m_colorram`: Shared pointers to the video and color RAM.
  - `m_discrete`, `m_ay8910`, `m_snsnd`: Optional sound devices (discrete circuitry, AY-3-8910, or SN76496).
  - `m_lamps`: An `output_finder` to control cabinet lamps.
  - `m_hopper`: An optional ticket/coin dispenser.

### `lespendu_state`
A derived class for the "Le Super Pendu" game. It adds a `m_databank` member to handle banked ROM for the game's word data.

### `blitz_state`
A derived class for "Mega Double Poker" and its variants. This hardware is more complex and includes:
- `m_mcu`: A `m68705p5_device` microcontroller used for protection and I/O.
- `m_cpubank`: A memory bank for ROMs that are banked and decrypted by the MCU.
- `m_overlay`: A memory overlay to handle the decryption.

## 2. Hardware Modules and Configurations

The driver defines numerous `machine_config` functions to describe the hardware for different game boards. Each function configures the specific set of emulated devices.

- **`goldnpkr_base`:** A foundational configuration that sets up the most common hardware: an M6502 CPU, two PIAs, a screen, a MC6845 CRTC, and basic video/palette decoding.
- **`goldnpkr` / `pottnpkr`:** These build on the base, adding discrete sound circuitry.
- **`witchcrd` / `wcfalcon` / `super21p`:** These configurations often use an `AY8910` PSG for sound instead of discrete circuits.
- **`wing_w90` / `kmhpan`:** These use an `SN76489A` PSG for sound.
- **`megadpkr`:** A unique configuration featuring the `m68705p5` MCU for protection, requiring a more complex setup with CPU-MCU communication handlers.
- **Encrypted Variants (`wildcrde`, `dash_a37`, etc.):** Some configurations include placeholder or partial implementations for games with encrypted CPUs or logic that is not yet fully understood.

## 3. Memory Maps

The memory layout is defined through `address_map` functions. Different hardware variants have slightly different maps.

- **A typical `goldnpkr_map` includes:**
  - `0x0000 - 0x07FF`: NVRAM (Non-Volatile RAM, battery-backed).
  - `0x0800 - 0x0801`: MC6845 CRTC registers.
  - `0x0844 - 0x084B`: Two 6821 PIAs for I/O.
  - `0x1000 - 0x13FF`: Video RAM.
  - `0x1800 - 0x1BFF`: Color RAM.
  - `0x4000 - 0x7FFF` / `0x8000 - 0xFFFF`: Program ROM space (often mirrored).

- **Variants:**
  - `pottnpkr_map`: Has a smaller address space, with ROMs from `0x2000-0x3FFF`.
  - `witchcrd_map`: Includes an extra RAM area and reads a second bank of DIP switches at `0x2000`.
  - `megadpkr_map`: Features a banked and decrypted ROM area controlled by the MCU.

## 4. Video System

The video system is tile-based and relatively straightforward.

- **Tile Generation:** The `TILE_GET_INFO_MEMBER` functions (e.g., `get_bg_tile_info`) are callbacks that MAME uses to get information for each tile on the screen.
  - They read a character code from `m_videoram`.
  - They read an attribute byte from `m_colorram`. The bits of this byte determine the tile's color, which graphics bank to use, and other flags.
- **Palette:** The color information is not direct. Instead, it's an index into a color palette.
  - The `*_palette` functions (e.g., `goldnpkr_palette`) initialize the palette by reading from a color PROM.
  - The PROM data typically defines Red, Green, Blue, and Intensity components for each color index.
  - Some variants have unique features like the "Blue Killer" circuit in `witchcrd` to force a black background.

## 5. Sound System

The driver supports several sound systems:

- **Discrete Sound (`goldnpkr_discrete`, `pottnpkr_discrete`):** This is an emulation of an analog sound circuit built from components like a 555 timer and a resistor ladder (DAC). The PIAs write values to this circuit to produce simple tones.
- **PSG Sound:** More advanced boards use a Programmable Sound Generator chip, either an `AY-3-8910` or an `SN76496`. The CPU writes to the PSG's registers to produce more complex music and sound effects.

## 6. Input/Output and Game Controls

- **Multiplexed Inputs:** The player controls (Hold, Bet, Deal, etc.) are multiplexed. The PIA selects one of four groups of inputs, and the CPU then reads the state of the buttons in that group. The `goldnpkr_mux_port_r` function handles this logic.
- **PIA (Peripheral Interface Adapter):** The two `6821 PIA` devices are central to I/O.
  - **PIA0:** Reads the multiplexed inputs and controls cabinet lamps.
  - **PIA1:** Controls the input multiplexer selector, writes to the sound hardware, and reads DIP switches.
- **DIP Switches:** Multiple banks of DIP switches (`SW1`, `SW2`, etc.) are used to configure game settings like payout rates, game rules, and coinage.

## 7. ROM Definitions and Game Variants

The file contains a massive list of `ROM_START` blocks, one for each supported game or bootleg. Each block defines the ROM files that make up the game and how they are loaded into memory.

- **`ROM_LOAD`:** Specifies a ROM file to be loaded at a particular address.
- **`ROM_COPY` / `ROM_FILL`:** Used to manipulate memory regions, often to reorganize graphics data from multiple ROMs into a format MAME can use.
- **Game Definitions (`GAME` macros):** The list at the end of the file uses MAME's `GAME` macro to register each game, providing metadata like the year, manufacturer, full name, and which machine/input configurations to use.

## 8. Protections and Initializations

Many games in this driver have simple protection schemes or require specific initialization.

- **`init_*` functions:** These are called once when a game is first loaded.
- **Functionality:**
  - **Memory Patching:** Many `init` functions patch the game's ROM in memory to bypass protection checks. These checks often involve checksums, reading from specific memory locations, or waiting for hardware that isn't present in all bootlegs.
  - **Hardware Workarounds:** Some functions patch the code to account for slight hardware differences between variants (e.g., inverted logic, different I/O addresses).
  - **Decryption:** For games like `lfhouse`, the `init` function performs software decryption of the main program ROM before the CPU starts executing it.
  - **NVRAM Setup:** Some games require a default NVRAM file to be loaded to boot correctly.

This structure allows the driver to support hundreds of variations on a few core hardware platforms by reusing and slightly modifying the machine configurations, memory maps, and init functions.
