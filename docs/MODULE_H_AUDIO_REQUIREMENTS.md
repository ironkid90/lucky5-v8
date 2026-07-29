# Module H: Audio Design Requirements

## Overview
This document specifies the complete audio requirements for AI9 visual parity. All sounds must replicate the mechanical, arcade-style audio feedback of the original AI9 cabinet.

## Audio File Requirements

### 1. Button Press Sounds (5 files)

#### 1.1 press.mp3
- **Usage**: Default button press (BET, CANCEL HOLD, BIG, SMALL)
- **Characteristics**: Sharp mechanical click, ~50-80ms duration
- **Volume**: Medium (0.24-0.30)
- **Priority**: Normal
- **Reference**: Mechanical button switch actuation sound

#### 1.2 hold_press.mp3
- **Usage**: HOLD button activation/deactivation
- **Characteristics**: Distinct click with slight metallic resonance
- **Volume**: Medium (0.26)
- **Priority**: Normal
- **Reference**: Toggle switch with lamp activation

#### 1.3 deal_press.mp3
- **Usage**: DEAL/DRAW button activation
- **Characteristics**: Heavy mechanical thump, authoritative
- **Volume**: Medium-high (0.32)
- **Priority**: High
- **Reference**: Large red arcade button press

#### 1.4 menu_press.mp3
- **Usage**: MENU button and navigation
- **Characteristics**: Subtle click, less pronounced than game buttons
- **Volume**: Low-medium (0.20)
- **Priority**: Low
- **Reference**: Small control panel button

#### 1.5 invalid_press.mp3
- **Usage**: Invalid/blocked action feedback
- **Characteristics**: Dull thud or buzz, ~100ms
- **Volume**: Low (0.18)
- **Priority**: High (must override other sounds to provide feedback)
- **Reference**: Mechanical lockout/buzzer sound

### 2. Card Sounds (3 files)

#### 2.1 card_deal.mp3
- **Usage**: Each card appearance during deal
- **Characteristics**: Quick snap/thump, ~30-50ms
- **Volume**: Low-medium (0.22)
- **Priority**: Normal
- **Reference**: Card sliding into position on felt

#### 2.2 card_draw.mp3
- **Usage**: Card replacement during draw phase
- **Characteristics**: Similar to deal but slightly sharper
- **Volume**: Low-medium (0.24)
- **Priority**: Normal
- **Reference**: Card flipping/replacing sound

#### 2.3 card_shuffle.mp3
- **Usage**: Double-up shuffle/reel animation
- **Characteristics**: Rapid mechanical ratchet, loopable
- **Volume**: Medium (0.28)
- **Priority**: Normal
- **Reference**: Slot machine reel spin

### 3. Win/Score Sounds (4 files)

#### 3.1 win_small.mp3
- **Usage**: Small wins (2 PAIR, 3 OF A KIND)
- **Characteristics**: Pleasant chime, ~200-300ms
- **Volume**: Medium (0.30)
- **Priority**: High
- **Reference**: Mechanical bell ring

#### 3.2 win_medium.mp3
- **Usage**: Medium wins (STRAIGHT, FLUSH, FULL HOUSE)
- **Characteristics**: Richer chime sequence, ~400-500ms
- **Volume**: Medium-high (0.36)
- **Priority**: High
- **Reference**: Multiple bell tones

#### 3.3 win_large.mp3
- **Usage**: Large wins (4 OF A KIND, STRAIGHT FLUSH)
- **Characteristics**: Ascending tone sequence, ~600-800ms
- **Volume**: High (0.42)
- **Priority**: Very High
- **Reference**: Jackpot bell cascade

#### 3.4 win_royal.mp3
- **Usage**: ROYAL FLUSH only
- **Characteristics**: Triumphant fanfare, ~1000-1500ms
- **Volume**: Maximum (0.50)
- **Priority**: Maximum
- **Reference**: Arcade victory fanfare

### 4. Double-Up Sounds (3 files)

#### 4.1 du_enter.mp3
- **Usage**: Entering double-up mode
- **Characteristics**: Rising tone transition, ~200ms
- **Volume**: Medium (0.28)
- **Priority**: High
- **Reference**: Mode change indicator

#### 4.2 du_win.mp3
- **Usage**: Successful double-up guess
- **Characteristics**: Success chime, ~250ms
- **Volume**: Medium-high (0.34)
- **Priority**: High
- **Reference**: Correct answer confirmation

#### 4.3 du_lose.mp3
- **Usage**: Failed double-up guess
- **Characteristics**: Descending tone, ~300ms with decay
- **Volume**: Medium (0.30)
- **Priority**: High
- **Reference**: Wrong answer/loss indicator

### 5. System Sounds (3 files)

#### 5.1 credit_tick.mp3
- **Usage**: Credit counter increment during drain animation
- **Characteristics**: Mechanical counter tick, ~20-30ms, loopable
- **Volume**: Low (0.16)
- **Priority**: Low
- **Reference**: Electromechanical odometer tick

#### 5.2 machine_ready.mp3
- **Usage**: Machine initialization/session start
- **Characteristics**: Power-up tone, ~400ms
- **Volume**: Medium (0.28)
- **Priority**: Normal
- **Reference**: Arcade cabinet power-on

#### 5.3 machine_close.mp3
- **Usage**: Cash-out/session end
- **Characteristics**: Power-down sequence, ~500ms with reverb tail
- **Volume**: High (0.40)
- **Priority**: Very High
- **Reference**: Safe locking/machine securing

### 6. Special Events (2 files)

#### 6.1 lucky5_trigger.mp3
- **Usage**: 5♠ appears (Lucky 5 special card)
- **Characteristics**: Distinct fanfare with sparkle effect, ~800ms
- **Volume**: High (0.45)
- **Priority**: Maximum
- **Reference**: Special bonus trigger

#### 6.2 jackpot_hit.mp3
- **Usage**: Jackpot counter hit (FH rank match, 4K, SF with jackpot)
- **Characteristics**: Bell cascade with coin sounds, ~1200ms
- **Volume**: Maximum (0.50)
- **Priority**: Maximum
- **Reference**: Physical jackpot payout

## Implementation Notes

### Audio Format Specifications
- **Format**: MP3, 44.1kHz sample rate
- **Bitrate**: 128-192 kbps (balance quality vs. load time)
- **Channels**: Mono (arcade cabinets typically used mono speakers)
- **Normalization**: Peak normalize to -1.0dB to prevent clipping

### Volume Levels
- All volume values (0.0-1.0) are relative to the audio context gain
- User should have master volume control in settings
- Sounds should NOT distort at maximum volume setting

### Priority System
- **Maximum**: Overrides all other sounds, cannot be interrupted
- **Very High**: Overrides High/Normal/Low
- **High**: Overrides Normal/Low
- **Normal**: Can be interrupted by High or above
- **Low**: Background/ambient, easily interrupted

### Timing Synchronization
- Card sounds MUST sync with visual card appearance (VSYNC-locked at 60Hz)
- Stagger timing: 4 frames (~66ms) between card sounds during deal/draw
- Credit tick sound loops at 50ms intervals during drain animation
- DU shuffle sound loops continuously during reel animation

### Browser Compatibility
- Use Web Audio API for precise timing and mixing
- Preload all sounds during asset loading phase
- Implement fallback for browsers without Web Audio API support
- Handle iOS audio unlock on first user interaction

### File Organization
```
/assets/sounds/
├── buttons/
│   ├── press.mp3
│   ├── hold_press.mp3
│   ├── deal_press.mp3
│   ├── menu_press.mp3
│   └── invalid_press.mp3
├── cards/
│   ├── card_deal.mp3
│   ├── card_draw.mp3
│   └── card_shuffle.mp3
├── wins/
│   ├── win_small.mp3
│   ├── win_medium.mp3
│   ├── win_large.mp3
│   └── win_royal.mp3
├── doubleup/
│   ├── du_enter.mp3
│   ├── du_win.mp3
│   └── du_lose.mp3
├── system/
│   ├── credit_tick.mp3
│   ├── machine_ready.mp3
│   └── machine_close.mp3
└── special/
    ├── lucky5_trigger.mp3
    └── jackpot_hit.mp3
```

## Audio Event Mapping

| Game Event | Audio File | Timing | Notes |
|------------|-----------|--------|-------|
| HOLD button press | hold_press.mp3 | Immediate | Toggle feedback |
| BET button press | press.mp3 | Immediate | Bet increment |
| DEAL button press | deal_press.mp3 | Immediate | Heavy emphasis |
| Card appears (deal) | card_deal.mp3 | Per-card, 66ms stagger | Sync with VSYNC |
| Card appears (draw) | card_draw.mp3 | Per-card, 66ms stagger | Replacement only |
| Small win | win_small.mp3 | On hand evaluation | 2P, 3K |
| Medium win | win_medium.mp3 | On hand evaluation | ST, FL, FH |
| Large win | win_large.mp3 | On hand evaluation | 4K, SF |
| Royal Flush | win_royal.mp3 | On hand evaluation | Maximum celebration |
| DU enter | du_enter.mp3 | Mode transition | Before shuffle starts |
| DU shuffle | card_shuffle.mp3 | Loop during animation | Stop on reveal |
| DU win | du_win.mp3 | On successful guess | Trail advances |
| DU lose | du_lose.mp3 | On failed guess | Before siphon starts |
| Credit drain tick | credit_tick.mp3 | Loop @ 50ms | During TAKE SCORE |
| 5♠ appears | lucky5_trigger.mp3 | Card reveal + 50ms | Special overlay |
| Jackpot hit | jackpot_hit.mp3 | Post-win evaluation | Replaces win sound |
| Invalid action | invalid_press.mp3 | Immediate | Override current sound |
| Session start | machine_ready.mp3 | After cash-in | Welcome tone |
| Session end | machine_close.mp3 | On cash-out | Farewell sequence |

## Testing Checklist

- [ ] All 20 audio files present in correct directory structure
- [ ] Each sound plays at specified volume level
- [ ] Priority system correctly interrupts lower-priority sounds
- [ ] Card sounds sync precisely with visual card appearance (±16ms tolerance)
- [ ] Credit tick loops smoothly during drain animation
- [ ] DU shuffle loops seamlessly during reel animation
- [ ] No audio clipping or distortion at maximum volume
- [ ] Sounds work on iOS after unlock interaction
- [ ] Sounds work in all major browsers (Chrome, Firefox, Safari, Edge)
- [ ] Master volume control affects all sounds proportionally
- [ ] Preloading completes before game allows first action
- [ ] No audio glitches during rapid button presses
- [ ] Special event sounds (Lucky 5, Jackpot) trigger correctly
- [ ] Audio memory usage stays under 10MB total

## Future Enhancements

### Phase 2 (Optional)
- Ambient background hum (CRT power supply drone)
- Button release sounds for more mechanical realism
- Spatial audio (stereo panning for left/right jackpot counters)
- Dynamic volume based on win magnitude
- Combo sound system (rapid wins trigger special combo audio)

### Phase 3 (Optional)
- User-selectable sound themes (classic, modern, minimal)
- Accessibility: visual feedback option for sound-off mode
- Sound effect customization (per-sound volume controls)
- Audio analytics (track which sounds are most impactful)

## References

- AI9 arcade cabinet audio recordings (if available)
- Classic arcade game sound design patterns
- Web Audio API best practices
- iOS audio unlock patterns
- Retro gaming audio analysis
