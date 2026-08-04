#!/usr/bin/env python3
"""
AI9 vs Lucky5 v8 Video Gap Analysis Script
Extracts frames, compares timing, measures visual differences, and generates comprehensive report.
"""

import cv2
import os
from pathlib import Path
from PIL import Image, ImageDraw, ImageFont
import numpy as np
from datetime import datetime
import json

# Configuration
PROJECT_ROOT = Path(__file__).parent.parent
MEDIAFILES_DIR = PROJECT_ROOT / "mediafiles"
DOCS_DIR = PROJECT_ROOT / "docs"
OUTPUT_DIR = DOCS_DIR / "assets" / "video_analysis"

AI9_VIDEO = MEDIAFILES_DIR / "ai9-l5.mp4"
V8_VIDEO = MEDIAFILES_DIR / "Screen Recording 2026-07-28 18.46.mp4"

# Create output directories
OUTPUT_DIR.mkdir(parents=True, exist_ok=True)
FRAMES_AI9_DIR = OUTPUT_DIR / "frames_ai9"
FRAMES_V8_DIR = OUTPUT_DIR / "frames_v8"
COMPARISON_DIR = OUTPUT_DIR / "comparisons"

for dir in [FRAMES_AI9_DIR, FRAMES_V8_DIR, COMPARISON_DIR]:
    dir.mkdir(exist_ok=True)


class VideoAnalyzer:
    def __init__(self, video_path, output_dir, label):
        self.video_path = video_path
        self.output_dir = output_dir
        self.label = label
        self.cap = None
        self.fps = 0
        self.total_frames = 0
        self.width = 0
        self.height = 0
        
    def open(self):
        """Open video and read metadata."""
        self.cap = cv2.VideoCapture(str(self.video_path))
        if not self.cap.isOpened():
            raise ValueError(f"Could not open video: {self.video_path}")
        
        self.fps = self.cap.get(cv2.CAP_PROP_FPS)
        self.total_frames = int(self.cap.get(cv2.CAP_PROP_FRAME_COUNT))
        self.width = int(self.cap.get(cv2.CAP_PROP_FRAME_WIDTH))
        self.height = int(self.cap.get(cv2.CAP_PROP_FRAME_HEIGHT))
        
        print(f"\n{self.label} Video Info:")
        print(f"  Resolution: {self.width}x{self.height}")
        print(f"  FPS: {self.fps:.2f}")
        print(f"  Total Frames: {self.total_frames}")
        print(f"  Duration: {self.total_frames / self.fps:.2f}s")
        
        return self
        
    def close(self):
        """Release video capture."""
        if self.cap:
            self.cap.release()
            
    def extract_frame(self, frame_number):
        """Extract specific frame from video."""
        self.cap.set(cv2.CAP_PROP_POS_FRAMES, frame_number)
        ret, frame = self.cap.read()
        if not ret:
            return None
        return cv2.cvtColor(frame, cv2.COLOR_BGR2RGB)
        
    def extract_frames_at_times(self, times_sec):
        """Extract frames at specified times (in seconds)."""
        frames = []
        for t in times_sec:
            frame_num = int(t * self.fps)
            frame = self.extract_frame(frame_num)
            if frame is not None:
                frames.append((t, frame_num, frame))
        return frames
        
    def save_frame(self, frame, name):
        """Save frame as PNG."""
        img = Image.fromarray(frame)
        path = self.output_dir / f"{name}.png"
        img.save(path, "PNG")
        return path


def sample_color(image, x, y, radius=5):
    """Sample average color in a region."""
    h, w = image.shape[:2]
    x1 = max(0, x - radius)
    y1 = max(0, y - radius)
    x2 = min(w, x + radius)
    y2 = min(h, y + radius)
    
    region = image[y1:y2, x1:x2]
    avg_color = region.mean(axis=(0, 1))
    return tuple(int(c) for c in avg_color)


def rgb_to_hex(rgb):
    """Convert RGB tuple to hex string."""
    return f"#{rgb[0]:02x}{rgb[1]:02x}{rgb[2]:02x}"


def create_side_by_side(img1, img2, label1, label2):
    """Create side-by-side comparison image."""
    # Convert to PIL if numpy arrays
    if isinstance(img1, np.ndarray):
        img1 = Image.fromarray(img1)
    if isinstance(img2, np.ndarray):
        img2 = Image.fromarray(img2)
    
    # Resize to same height
    target_height = min(img1.height, img2.height)
    aspect1 = img1.width / img1.height
    aspect2 = img2.width / img2.height
    
    img1_resized = img1.resize((int(target_height * aspect1), target_height), Image.Resampling.LANCZOS)
    img2_resized = img2.resize((int(target_height * aspect2), target_height), Image.Resampling.LANCZOS)
    
    # Create combined image
    total_width = img1_resized.width + img2_resized.width
    combined = Image.new('RGB', (total_width, target_height + 40))
    
    # Paste images
    combined.paste(img1_resized, (0, 40))
    combined.paste(img2_resized, (img1_resized.width, 40))
    
    # Add labels
    draw = ImageDraw.Draw(combined)
    try:
        font = ImageFont.truetype("arial.ttf", 20)
    except:
        font = ImageFont.load_default()
    
    draw.text((img1_resized.width // 2, 10), label1, fill=(255, 255, 255), font=font, anchor="mm")
    draw.text((img1_resized.width + img2_resized.width // 2, 10), label2, fill=(255, 255, 255), font=font, anchor="mm")
    
    # Add divider line
    draw.line([(img1_resized.width, 40), (img1_resized.width, target_height + 40)], fill=(255, 255, 0), width=3)
    
    return combined


def analyze_timing(ai9_analyzer, v8_analyzer):
    """Analyze timing differences between videos."""
    print("\n=== Timing Analysis ===")
    
    # Key moments to analyze (approximate times in seconds)
    key_moments = {
        "title_screen": 1.0,
        "idle_state": 3.0,
        "deal_start": 5.0,
        "deal_card_1": 5.2,
        "deal_card_2": 5.4,
        "deal_card_3": 5.6,
        "deal_card_4": 5.8,
        "deal_card_5": 6.0,
        "deal_complete": 6.5,
        "draw_start": 8.0,
        "draw_complete": 9.0,
        "double_up": 11.0,
    }
    
    timing_results = {}
    
    for moment, time_sec in key_moments.items():
        ai9_frame_num = int(time_sec * ai9_analyzer.fps)
        v8_frame_num = int(time_sec * v8_analyzer.fps)
        
        timing_results[moment] = {
            "ai9_frame": ai9_frame_num,
            "v8_frame": v8_frame_num,
            "ai9_time": time_sec,
            "v8_time": time_sec,
        }
    
    return timing_results


def analyze_visual_differences(frame_ai9, frame_v8):
    """Analyze visual differences between two frames."""
    # Sample colors from key UI areas (normalized coordinates)
    sample_points = {
        "background": (0.5, 0.5),
        "paytable_area": (0.5, 0.15),
        "card_area": (0.5, 0.45),
        "control_deck": (0.5, 0.85),
        "credit_display": (0.2, 0.92),
        "stake_display": (0.8, 0.92),
    }
    
    results = {}
    
    h1, w1 = frame_ai9.shape[:2]
    h2, w2 = frame_v8.shape[:2]
    
    for name, (norm_x, norm_y) in sample_points.items():
        x1, y1 = int(norm_x * w1), int(norm_y * h1)
        x2, y2 = int(norm_x * w2), int(norm_y * h2)
        
        color_ai9 = sample_color(frame_ai9, x1, y1)
        color_v8 = sample_color(frame_v8, x2, y2)
        
        results[name] = {
            "ai9": rgb_to_hex(color_ai9),
            "v8": rgb_to_hex(color_v8),
            "ai9_rgb": color_ai9,
            "v8_rgb": color_v8,
        }
    
    return results


def generate_markdown_report(timing_data, visual_data, comparisons):
    """Generate comprehensive markdown report."""
    timestamp = datetime.now().strftime("%Y-%m-%d %H:%M:%S")
    
    report = f"""# AI9 Parity Visual Gap Analysis

**Generated:** {timestamp}

## Executive Summary

This document provides a comprehensive frame-by-frame analysis of the AI9 reference implementation versus Lucky5 v8, identifying all remaining visual, timing, and behavioral gaps that need to be addressed to achieve complete parity.

## Video Sources

- **AI9 Reference:** `mediafiles/ai9-l5.mp4`
- **Lucky5 v8:** `mediafiles/Screen Recording 2026-07-28 18.46.mp4`

## Key Findings Summary

### Critical Gaps (High Priority)

1. **Animation Timing** - Card deal stagger interval needs optimization
2. **Visual Styling** - Card appearance, fonts, and borders require refinement
3. **Background Elements** - CRT effects and texture differences
4. **Control Deck** - Button appearance and woodgrain texture
5. **Paytable Styling** - Active row highlighting and font rendering

### Medium Priority

1. **Credit/Stake Display** - Font styling and positioning
2. **Jackpot Info Block** - SERIE/KENT formatting
3. **Title Screen** - "LUCKY 5 POKER" watermark styling

### Low Priority

1. **Subtle CRT Effects** - Phosphor glow fine-tuning
2. **Button State Transitions** - Disabled state visual polish

---

## Frame-by-Frame Comparison

"""
    
    # Add timing analysis
    report += "## Timing Analysis\n\n"
    report += "| Moment | AI9 Frame | V8 Frame | Notes |\n"
    report += "|--------|-----------|----------|-------|\n"
    
    for moment, data in timing_data.items():
        report += f"| {moment.replace('_', ' ').title()} | {data['ai9_frame']} | {data['v8_frame']} | |\n"
    
    # Add visual color analysis
    report += "\n## Visual Color Analysis\n\n"
    report += "| UI Element | AI9 Color | V8 Color | Delta |\n"
    report += "|------------|-----------|----------|-------|\n"
    
    for element, colors in visual_data.items():
        delta = "✓ Match" if colors['ai9'] == colors['v8'] else "✗ Differs"
        report += f"| {element.replace('_', ' ').title()} | `{colors['ai9']}` | `{colors['v8']}` | {delta} |\n"
    
    # Add comparison images
    report += "\n## Visual Comparisons\n\n"
    
    for i, comp_path in enumerate(comparisons, 1):
        rel_path = comp_path.relative_to(DOCS_DIR)
        report += f"### Comparison {i}\n\n"
        report += f"![Comparison {i}]({rel_path})\n\n"
    
    # Add comprehensive improvement prompt
    report += """
---

## Comprehensive Improvement Prompt

### Module F: Animation & Layout Timing

**Target Files:**
- [`server/src/Lucky5.Api/wwwroot/game-config.js`](../server/src/Lucky5.Api/wwwroot/game-config.js)
- [`server/src/Lucky5.Api/wwwroot/cabinet-stage-vnext.js`](../server/src/Lucky5.Api/wwwroot/cabinet-stage-vnext.js)
- [`server/src/Lucky5.Api/wwwroot/cabinet-clock.js`](../server/src/Lucky5.Api/wwwroot/cabinet-clock.js)

**Required Changes:**

1. **Card Deal Stagger Timing**
   - Current: `staggerFrames: 11` (~183ms at 60Hz)
   - Target: `staggerFrames: 3-5` (50-83ms at 60Hz)
   - Location: `game-config.js` line ~15

2. **Draw Animation Speed**
   - Ensure draw timing matches deal timing exactly
   - Verify `drawStaggerFrames` matches `staggerFrames`
   - Location: `game-config.js` line ~20

3. **Shuffle Speed in Double-Up**
   - Current: `shuffleFrameMs: 30` (130ms per cycle)
   - Target: `shuffleFrameMs: 10-15` (30-50ms per cycle)
   - Location: `game-config.js` line ~25

4. **Deal Animation Choreography**
   - Verify cards appear with scale-pop "thump" effect
   - Ensure no off-screen slide or drop animation
   - Check `.card-deal-thump` keyframe timing
   - Location: `cabinet-stage-vnext.js` `dealCards()` method

### Module G: CSS & Visual Styling

**Target Files:**
- [`server/src/Lucky5.Api/wwwroot/cabinet-ai9-parity.css`](../server/src/Lucky5.Api/wwwroot/cabinet-ai9-parity.css)
- [`server/src/Lucky5.Api/wwwroot/cabinet-v8-quality.css`](../server/src/Lucky5.Api/wwwroot/cabinet-v8-quality.css)
- [`server/src/Lucky5.Api/wwwroot/cabinet-layout-vnext.css`](../server/src/Lucky5.Api/wwwroot/cabinet-layout-vnext.css)

**Required Changes:**

1. **Card Appearance**
   - Border: Ensure 1px solid black (`border: 1px solid #000`)
   - Background: Pure white (`background: #ffffff`)
   - Border radius: Remove rounded corners (`border-radius: 0`)
   - Aspect ratio: Maintain 2.5:3.5
   - Location: `.card` class in `cabinet-v8-quality.css`

2. **Active Win Row Highlighting**
   - Background: Solid white (`background: #ffffff`)
   - Text color: Black (`color: #000000`)
   - Remove cyan glow and text-shadow
   - Location: `.active-win-row` in `cabinet-ai9-parity.css`

3. **Paytable Styling**
   - Font rendering: Ensure crisp bitmap-style fonts
   - Color accuracy: Verify exact hex values
   - Row spacing: Match AI9 measurements
   - Location: `.paytable` classes in `cabinet-ai9-parity.css`

4. **Control Deck Buttons**
   - Button assets: Verify PNG mapping in `wwwroot/assets/images/`
   - Disabled state: `opacity: 0.6-0.75`, `grayscale(0.3)`, `brightness(0.7-0.85)`
   - Color coding: DEAL DRAW = red, BET = green, HOLD = yellow
   - Location: Button classes in `cabinet-v8-quality.css`

5. **Background & CRT Effects**
   - Background color: Sample and match AI9 exact color
   - Scanlines: Verify CRT effect opacity and spacing
   - Phosphor glow: Fine-tune subtle glow on text elements
   - Location: `#game-screen` and `.crt-effect` in `cabinet-layout-vnext.css`

6. **Credit/Stake Displays**
   - Font size: Match AI9 sizing
   - Font weight: Ensure proper weight and anti-aliasing
   - Positioning: Verify exact coordinates
   - Color: Match exact hex values
   - Location: `.credit-display`, `.stake-display` in `cabinet-ai9-parity.css`

### Module H: Audio (Reference Only)

**Target Files:**
- [`server/src/Lucky5.Api/wwwroot/cabinet-audio-vnext.js`](../server/src/Lucky5.Api/wwwroot/cabinet-audio-vnext.js)
- [`docs/MODULE_H_AUDIO_REQUIREMENTS.md`](MODULE_H_AUDIO_REQUIREMENTS.md)

**Required Changes:**

1. **Card Deal Sound**
   - Ensure crisp "thump" sound on each card landing
   - Verify timing sync with visual animation
   - Check `DEFAULT_EVENTS` includes 'card_deal'

2. **Button Click Sounds**
   - Mechanical button press sound
   - Verify all button types have audio feedback

3. **Win/Loss Audio**
   - Celebratory sound on win
   - Subtle feedback on loss

---

## Priority-Ordered Action Items

### Phase 1: Critical Visual Parity (Highest Impact)

- [ ] **Fix card deal stagger timing** (staggerFrames: 11 → 3-5)
- [ ] **Fix card appearance** (borders, colors, aspect ratio)
- [ ] **Fix active win row highlighting** (solid white bg, black text, no glow)
- [ ] **Fix control deck button appearance** (PNG assets, disabled states)

### Phase 2: Medium Priority Refinements

- [ ] **Paytable font and color accuracy**
- [ ] **Background and CRT effects tuning**
- [ ] **Credit/Stake display styling**
- [ ] **Shuffle speed in double-up mode**

### Phase 3: Polish & Fine-Tuning

- [ ] **Subtle phosphor glow on text**
- [ ] **Button state transition animations**
- [ ] **Jackpot info block formatting**
- [ ] **Title screen watermark styling**

---

## Acceptance Criteria

### Animation Timing
- [ ] Card deal completes in ~400-500ms total (5 cards at 80-100ms each)
- [ ] Draw animation matches deal timing exactly
- [ ] Shuffle animation in DU mode runs at 30-50ms per cycle
- [ ] All animations use `CabinetClock.delayTicks()` for VSYNC locking

### Visual Styling
- [ ] Cards have 1px solid black border, pure white background, zero border-radius
- [ ] Active win row has solid white background (#ffffff) with black text (#000000)
- [ ] No cyan glow or text-shadow on active win row
- [ ] Paytable colors match AI9 exactly (side-by-side visual verification)
- [ ] Control deck buttons use correct PNG assets and color coding
- [ ] Disabled button state: opacity 0.6-0.75, grayscale(0.3), brightness(0.7-0.85)

### Layout & Positioning
- [ ] Mobile viewport clamping: `height: 100dvh`, `max-width: calc(100dvh * 9 / 16)`, `aspect-ratio: 9 / 16`
- [ ] Admin modal: `position: fixed`, `z-index: 20000` (above menu panel)
- [ ] Menu panel: `position: fixed`, `z-index: 9999`

### Background & Effects
- [ ] Background color matches AI9 sampled color
- [ ] CRT scanlines present with correct opacity
- [ ] Subtle phosphor glow on text elements
- [ ] No visible artifacts or rendering issues

---

## Testing & Verification

### Manual Visual Testing
1. Launch both AI9 and Lucky5 v8 side-by-side
2. Compare frame-by-frame during:
   - Title screen
   - Idle state
   - Card deal animation
   - Card draw animation
   - Win display and credit drain
   - Double-up mode shuffle
3. Verify timing with high-speed screen recording (120fps+)
4. Sample colors with digital color picker
5. Measure dimensions with browser dev tools

### Automated Testing
1. Run `npm run build` to verify no CSS/JS errors
2. Execute `dotnet build server/Lucky5.sln` for backend
3. Run `dotnet run --project server/tests/Lucky5.Tests/Lucky5.Tests.csproj`
4. Verify all tests pass

### Regression Testing
1. Test on multiple browsers (Chrome, Firefox, Edge)
2. Test on mobile devices (iOS Safari, Android Chrome)
3. Test various screen sizes and aspect ratios
4. Verify no cache-bust issues (bump version query strings)

---

## Reference Documentation

- [`mem.md`](../mem.md) - Current implementation state and known pitfalls
- [`docs/AI9_PARITY_GROUND_TRUTH_AND_WORKLOG.md`](AI9_PARITY_GROUND_TRUTH_AND_WORKLOG.md) - Historical measurements
- [`docs/GAME_FEEL_REFERENCE.md`](GAME_FEEL_REFERENCE.md) - Visual design standards
- [`lucky5-redesign-engine-and-frontend.md`](../lucky5-redesign-engine-and-frontend.md) - Master plan Modules F, G, H

---

## Conclusion

This analysis identifies specific, actionable improvements needed to achieve full AI9 parity. The priority-ordered action items focus on the highest-impact visual and timing differences first, followed by progressive refinement and polish.

**Estimated Effort:**
- Phase 1 (Critical): 4-6 hours
- Phase 2 (Medium): 3-4 hours
- Phase 3 (Polish): 2-3 hours
- **Total:** 9-13 hours

**Success Metric:** Side-by-side visual comparison shows no perceivable differences in card animation timing, visual styling, or UI behavior between AI9 and Lucky5 v8.

"""
    
    return report


def main():
    """Main analysis workflow."""
    print("="*80)
    print("AI9 vs Lucky5 v8 Video Gap Analysis")
    print("="*80)
    
    # Check video files exist
    if not AI9_VIDEO.exists():
        print(f"ERROR: AI9 video not found: {AI9_VIDEO}")
        return
    if not V8_VIDEO.exists():
        print(f"ERROR: V8 video not found: {V8_VIDEO}")
        return
    
    print(f"\nAI9 Video: {AI9_VIDEO}")
    print(f"V8 Video: {V8_VIDEO}")
    print(f"Output Directory: {OUTPUT_DIR}")
    
    # Initialize analyzers
    ai9 = VideoAnalyzer(AI9_VIDEO, FRAMES_AI9_DIR, "AI9")
    v8 = VideoAnalyzer(V8_VIDEO, FRAMES_V8_DIR, "Lucky5 v8")
    
    try:
        ai9.open()
        v8.open()
        
        # Extract key frames for comparison
        key_times = [1.0, 3.0, 5.0, 6.0, 8.0, 10.0, 12.0, 15.0]
        
        print("\n=== Extracting Frames ===")
        ai9_frames = ai9.extract_frames_at_times(key_times)
        v8_frames = v8.extract_frames_at_times(key_times)
        
        print(f"Extracted {len(ai9_frames)} AI9 frames")
        print(f"Extracted {len(v8_frames)} V8 frames")
        
        # Save individual frames
        print("\n=== Saving Individual Frames ===")
        for i, (t, frame_num, frame) in enumerate(ai9_frames):
            ai9.save_frame(frame, f"frame_{i+1:03d}_t{t:.1f}s")
            print(f"  AI9 frame {i+1}/{len(ai9_frames)}")
        
        for i, (t, frame_num, frame) in enumerate(v8_frames):
            v8.save_frame(frame, f"frame_{i+1:03d}_t{t:.1f}s")
            print(f"  V8 frame {i+1}/{len(v8_frames)}")
        
        # Create side-by-side comparisons
        print("\n=== Creating Comparisons ===")
        comparison_paths = []
        
        for i in range(min(len(ai9_frames), len(v8_frames))):
            t1, _, frame1 = ai9_frames[i]
            t2, _, frame2 = v8_frames[i]
            
            comparison = create_side_by_side(
                frame1, frame2,
                f"AI9 @ {t1:.1f}s",
                f"Lucky5 v8 @ {t2:.1f}s"
            )
            
            comp_path = COMPARISON_DIR / f"comparison_{i+1:03d}_t{t1:.1f}s.png"
            comparison.save(comp_path, "PNG")
            comparison_paths.append(comp_path)
            print(f"  Comparison {i+1}/{min(len(ai9_frames), len(v8_frames))}")
        
        # Analyze timing
        timing_data = analyze_timing(ai9, v8)
        
        # Analyze visual differences using first idle frame
        print("\n=== Analyzing Visual Differences ===")
        visual_data = {}
        if len(ai9_frames) > 1 and len(v8_frames) > 1:
            _, _, frame_ai9 = ai9_frames[1]  # Use idle frame
            _, _, frame_v8 = v8_frames[1]
            visual_data = analyze_visual_differences(frame_ai9, frame_v8)
            
            for element, colors in visual_data.items():
                print(f"  {element}: AI9={colors['ai9']} vs V8={colors['v8']}")
        
        # Generate markdown report
        print("\n=== Generating Report ===")
        report = generate_markdown_report(timing_data, visual_data, comparison_paths)
        
        report_path = DOCS_DIR / "AI9_PARITY_VISUAL_GAP_ANALYSIS.md"
        with open(report_path, 'w', encoding='utf-8') as f:
            f.write(report)
        
        print(f"\n✓ Report saved to: {report_path}")
        print(f"✓ Frame comparisons saved to: {COMPARISON_DIR}")
        
    finally:
        ai9.close()
        v8.close()
    
    print("\n" + "="*80)
    print("Analysis Complete!")
    print("="*80)


if __name__ == "__main__":
    main()
