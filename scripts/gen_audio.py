#!/usr/bin/env python3
"""Generate synthetic WAV audio files for Lucky5 v8 cabinet sounds."""
import struct
import math
import os
import random

AUDIO_DIR = r"C:\Users\Gabi.WIN-CD45QMUUPFF\Documents\GitHub\lucky5-v8\lucky5-v8\server\src\Lucky5.Api\wwwroot\assets\audio"
os.makedirs(AUDIO_DIR, exist_ok=True)

SAMPLE_RATE = 44100

def write_wav(path, samples):
    num_samples = len(samples)
    data_size = num_samples * 2
    with open(path, 'wb') as f:
        f.write(b'RIFF')
        f.write(struct.pack('<I', 36 + data_size))
        f.write(b'WAVE')
        f.write(b'fmt ')
        f.write(struct.pack('<I', 16))
        f.write(struct.pack('<H', 1))   # PCM
        f.write(struct.pack('<H', 1))   # mono
        f.write(struct.pack('<I', SAMPLE_RATE))
        f.write(struct.pack('<I', SAMPLE_RATE * 2))
        f.write(struct.pack('<H', 2))
        f.write(struct.pack('<H', 16))
        f.write(b'data')
        f.write(struct.pack('<I', data_size))
        for s in samples:
            f.write(struct.pack('<h', max(-32768, min(32767, int(s * 32767)))))

def tone(freq, duration, shape='square', duty=0.5):
    n = int(SAMPLE_RATE * duration)
    samples = []
    for i in range(n):
        t = i / SAMPLE_RATE
        if shape == 'square':
            samples.append(1.0 if (freq * t) % 1 < duty else -1.0)
        elif shape == 'saw':
            samples.append(2.0 * ((freq * t) % 1) - 1.0)
        elif shape == 'sine':
            samples.append(math.sin(2 * math.pi * freq * t))
        elif shape == 'noise':
            samples.append(random.uniform(-1, 1))
        else:
            samples.append(0.0)
    return samples

def env(samples, attack_ms=2, release_ms=10):
    n = len(samples)
    a = int(SAMPLE_RATE * attack_ms / 1000)
    r = int(SAMPLE_RATE * release_ms / 1000)
    out = list(samples)
    for i in range(n):
        e = 1.0
        if a > 0 and i < a:
            e = i / a
        if r > 0 and i > n - r:
            e = min(e, (n - i) / r)
        out[i] *= e
    return out

print("Generating cabinet audio...")

# 1. deal.wav — mechanical card thud (~50ms)
write_wav(os.path.join(AUDIO_DIR, 'deal.wav'),
    env([s * 0.4 for s in tone(180, 0.06, 'square', 0.3) + tone(120, 0.04, 'square', 0.3)], 1, 5))

# 2. hold.wav — click
write_wav(os.path.join(AUDIO_DIR, 'hold.wav'),
    env(tone(800, 0.03, 'square') + [0]*int(SAMPLE_RATE*0.01) + tone(600, 0.02, 'square'), 1, 3))

# 3. draw.wav — card flip
write_wav(os.path.join(AUDIO_DIR, 'draw.wav'),
    env([s * 0.5 for s in tone(250, 0.05, 'saw') + tone(150, 0.03, 'saw')], 1, 8))

# 4. win-small.wav — gentle 2-tone
write_wav(os.path.join(AUDIO_DIR, 'win-small.wav'),
    env(tone(523, 0.12, 'sine') + tone(659, 0.12, 'sine'), 5, 30))

# 5. win-medium.wav — 3-tone ascending
write_wav(os.path.join(AUDIO_DIR, 'win-medium.wav'),
    env(tone(523, 0.10, 'sine') + tone(659, 0.10, 'sine') + tone(784, 0.15, 'sine'), 5, 40))

# 6. win-big.wav — fanfare
write_wav(os.path.join(AUDIO_DIR, 'win-big.wav'),
    env(tone(523, 0.08, 'square') + tone(659, 0.08, 'square') + tone(784, 0.08, 'square') + tone(1047, 0.20, 'square'), 5, 60))

# 7. win-jackpot.wav — extended fanfare + ticks
jp = tone(523, 0.10, 'square') + tone(659, 0.10, 'square') + tone(784, 0.10, 'square') + tone(1047, 0.30, 'square')
jp += [0]*int(SAMPLE_RATE*0.15)
for _ in range(8):
    jp += tone(2000, 0.02, 'sine') + [0]*int(SAMPLE_RATE*0.08)
write_wav(os.path.join(AUDIO_DIR, 'win-jackpot.wav'), env(jp, 5, 80))

# 8. du-shuffle.wav — rapid clicking
shuf = []
for _ in range(20):
    shuf += tone(400, 0.015, 'noise') + [0]*int(SAMPLE_RATE*0.015)
write_wav(os.path.join(AUDIO_DIR, 'du-shuffle.wav'), [s * 0.3 for s in shuf])

# 9. du-reveal.wav — card snap
write_wav(os.path.join(AUDIO_DIR, 'du-reveal.wav'),
    env(tone(600, 0.08, 'square', 0.2), 1, 15))

# 10. du-win.wav — ascending
write_wav(os.path.join(AUDIO_DIR, 'du-win.wav'),
    env(tone(440, 0.10, 'sine') + tone(880, 0.15, 'sine'), 3, 30))

# 11. du-lose.wav — descending buzz
write_wav(os.path.join(AUDIO_DIR, 'du-lose.wav'),
    env(tone(440, 0.12, 'saw') + tone(330, 0.15, 'saw'), 3, 40))

# 12. lucky5.wav — ethereal chime
l5 = tone(1319, 0.15, 'sine') + [0]*int(SAMPLE_RATE*0.05) + tone(1568, 0.25, 'sine')
write_wav(os.path.join(AUDIO_DIR, 'lucky5.wav'), env(l5, 10, 60))

# 13. machine-close.wav — alarm bell
mc = tone(880, 0.15, 'square') + [0]*int(SAMPLE_RATE*0.06) + tone(880, 0.15, 'square') + [0]*int(SAMPLE_RATE*0.06) + tone(880, 0.15, 'square')
for _ in range(6):
    mc += tone(1200, 0.03, 'sine') + [0]*int(SAMPLE_RATE*0.10)
write_wav(os.path.join(AUDIO_DIR, 'machine-close.wav'), env(mc, 3, 50))

# 14. bet-ramp.wav — rapid credit ticks
ramp = []
for _ in range(30):
    ramp += tone(1000, 0.012, 'square') + [0]*int(SAMPLE_RATE*0.028)
write_wav(os.path.join(AUDIO_DIR, 'bet-ramp.wav'), [s * 0.25 for s in ramp])

# 15. credit-tick.wav — single tick
write_wav(os.path.join(AUDIO_DIR, 'credit-tick.wav'),
    env(tone(1500, 0.015, 'square', 0.2), 0.5, 3))

# 16. press.wav — generic button (legacy compat)
write_wav(os.path.join(AUDIO_DIR, 'press.wav'),
    env(tone(600, 0.04, 'square', 0.3), 1, 5))

files = sorted(os.listdir(AUDIO_DIR))
print(f"Generated {len(files)} WAV files:")
for f in files:
    size = os.path.getsize(os.path.join(AUDIO_DIR, f))
    print(f"  {f} ({size:,} bytes)")