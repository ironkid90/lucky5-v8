#!/usr/bin/env python3
"""
Lucky5 v8 — high-fidelity asset regenerator ("crisper retro" pass).

Regenerates the card faces, the wood control-panel board, and the wooden
button shells at higher resolution and with real texture depth, while
preserving the AI9/retro look (same layout, same palette, same geometry).

Outputs into assets_hq/ (never overwrites originals). The HQ layer is
opt-in via css/cabinet-v8-hq.css.

Requires: Pillow (pip install pillow).
"""
import math
import os
import random
from PIL import Image, ImageDraw, ImageFilter, ImageOps

ROOT = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.join(ROOT, "assets_hq")
CARDS_OUT = os.path.join(OUT, "cards")
os.makedirs(CARDS_OUT, exist_ok=True)

RANKS = ["A", "2", "3", "4", "5", "6", "7", "8", "9", "10", "J", "Q", "K"]
SUITS = ["S", "H", "D", "C"]  # spades, hearts, diamonds, clubs

# Card geometry — keep the same 313:528 aspect, render at 2x.
CARD_W, CARD_H = 626, 1056
CARD_RADIUS = 46          # rounded-corner radius on the white face
CARD_BORDER = 10          # white margin from the transparent edge to the art

# ---------------------------------------------------------------- fonts ----

def _load_font(size, bold=True):
    """Best-effort scalable font; falls back to PIL's default bitmap font."""
    candidates = [
        "C:/Windows/Fonts/arialbd.ttf" if bold else "C:/Windows/Fonts/arial.ttf",
        "C:/Windows/Fonts/arial.ttf",
        "C:/Windows/Fonts/consola.ttf",
        "C:/Windows/Fonts/courbd.ttf",
    ]
    for path in candidates:
        if os.path.exists(path):
            try:
                from PIL import ImageFont
                return ImageFont.truetype(path, size)
            except Exception:
                pass
    from PIL import ImageFont
    return ImageFont.load_default()

# ------------------------------------------------------------- suit art ----

def _suit_mask(size, suit):
    """Render a suit glyph as a smooth (anti-aliased) alpha mask."""
    ss = 4  # supersample
    S = size * ss
    img = Image.new("L", (S, S), 0)
    d = ImageDraw.Draw(img)
    c = S // 2
    u = S / 100.0  # unit: 1% of the tile

    if suit == "H":  # heart
        r = 24 * u
        d.ellipse([c - 2 * r, 12 * u, c, 12 * u + 2 * r], fill=255)
        d.ellipse([c, 12 * u, c + 2 * r, 12 * u + 2 * r], fill=255)
        d.polygon([(c - 47 * u, 30 * u), (c + 47 * u, 30 * u), (c, 94 * u)], fill=255)
    elif suit == "D":  # diamond
        d.polygon([(c, 6 * u), (c + 32 * u, 50 * u), (c, 94 * u), (c - 32 * u, 50 * u)], fill=255)
    elif suit == "S":  # spade
        r = 24 * u
        d.ellipse([c - 2 * r, 32 * u, c, 32 * u + 2 * r], fill=255)
        d.ellipse([c, 32 * u, c + 2 * r, 32 * u + 2 * r], fill=255)
        d.polygon([(c, 6 * u), (c + 47 * u, 70 * u), (c - 47 * u, 70 * u)], fill=255)
        d.polygon([(c - 7 * u, 62 * u), (c + 7 * u, 62 * u), (c + 13 * u, 96 * u), (c - 13 * u, 96 * u)], fill=255)
    else:  # "C" club — three tangent circles + tapered stem
        r = 19 * u
        d.ellipse([c - r, 10 * u, c + r, 10 * u + 2 * r], fill=255)                      # top lobe
        d.ellipse([c - 2 * r + 2 * u, 34 * u, c - 2 * u, 34 * u + 2 * r], fill=255)      # left lobe
        d.ellipse([c + 2 * u, 34 * u, c + 2 * r - 2 * u, 34 * u + 2 * r], fill=255)      # right lobe
        # stem: narrow at lobes, flares to a small base — no spikes
        d.polygon([(c - 5 * u, 56 * u), (c + 5 * u, 56 * u),
                   (c + 11 * u, 92 * u), (c - 11 * u, 92 * u)], fill=255)
        d.ellipse([c - 13 * u, 86 * u, c + 13 * u, 96 * u], fill=255)                    # foot

    return img.resize((size, size), Image.LANCZOS)

def _paste_suit(card, x, y, size, suit, color):
    mask = _suit_mask(size, suit)
    tint = Image.new("RGBA", (size, size), color + (0,))
    tint.putalpha(mask)
    card.alpha_composite(tint, (int(x - size / 2), int(y - size / 2)))

def _paste_suit_on(target, x, y, size, s, color):
    """Paste a suit glyph onto an arbitrary RGBA tile (used for index pips & court bands)."""
    m = _suit_mask(size, s)
    t = Image.new("RGBA", (size, size), color + (0,))
    t.putalpha(m)
    target.alpha_composite(t, (int(x - size / 2), int(y - size / 2)))

def _pip_positions(rank):
    """Classic pip layouts on a 3x7 grid -> normalized (x, y) coords."""
    L, M, R = 0.25, 0.50, 0.75
    rows = [0.13, 0.24, 0.35, 0.50, 0.65, 0.76, 0.87]
    layouts = {
        "2":  [(M, rows[0]), (M, rows[6])],
        "3":  [(M, rows[0]), (M, rows[3]), (M, rows[6])],
        "4":  [(L, rows[0]), (R, rows[0]), (L, rows[6]), (R, rows[6])],
        "5":  [(L, rows[0]), (R, rows[0]), (M, rows[3]), (L, rows[6]), (R, rows[6])],
        "6":  [(L, rows[0]), (R, rows[0]), (L, rows[3]), (R, rows[3]), (L, rows[6]), (R, rows[6])],
        "7":  [(L, rows[0]), (R, rows[0]), (M, rows[1] + 0.02), (L, rows[3]), (R, rows[3]), (L, rows[6]), (R, rows[6])],
        "8":  [(L, rows[0]), (R, rows[0]), (M, rows[1] + 0.02), (L, rows[3]), (R, rows[3]),
               (M, rows[5] - 0.02), (L, rows[6]), (R, rows[6])],
        "9":  [(L, rows[0]), (R, rows[0]), (L, rows[2]), (R, rows[2]), (M, rows[3]),
               (L, rows[4]), (R, rows[4]), (L, rows[6]), (R, rows[6])],
        "10": [(L, rows[0]), (R, rows[0]), (M, rows[1] + 0.01), (L, rows[2]), (R, rows[2]),
               (L, rows[4]), (R, rows[4]), (M, rows[5] - 0.01), (L, rows[6]), (R, rows[6])],
    }
    return layouts.get(rank, [(M, rows[3])])

def _round_rect_mask(w, h, radius):
    ss = 4
    m = Image.new("L", (w * ss, h * ss), 0)
    ImageDraw.Draw(m).rounded_rectangle([0, 0, w * ss - 1, h * ss - 1], radius * ss, fill=255)
    return m.resize((w, h), Image.LANCZOS)

# ------------------------------------------------------------ card faces ----

def render_card(rank, suit):
    ink = (178, 30, 30) if suit in ("H", "D") else (18, 18, 24)
    card = Image.new("RGBA", (CARD_W, CARD_H), (0, 0, 0, 0))

    face = Image.new("RGBA", (CARD_W, CARD_H), (0, 0, 0, 0))
    white = Image.new("RGBA", (CARD_W - 2 * CARD_BORDER, CARD_H - 2 * CARD_BORDER), (252, 252, 250, 255))
    mask = _round_rect_mask(*white.size, CARD_RADIUS)
    face.paste(white, (CARD_BORDER, CARD_BORDER), mask)
    card.alpha_composite(face)

    margin_x = CARD_W * 0.115
    margin_y = CARD_H * 0.125
    index_font = _load_font(int(CARD_H * 0.075))

    def draw_index(cx, cy, flipped=False):
        tile = Image.new("RGBA", (int(CARD_W * 0.22), int(CARD_H * 0.22)), (0, 0, 0, 0))
        td = ImageDraw.Draw(tile)
        tb = td.textbbox((0, 0), rank, font=index_font)
        tw, th = tb[2] - tb[0], tb[3] - tb[1]
        td.text(((tile.width - tw) / 2 - tb[0], tile.height * 0.06 - tb[1]), rank, font=index_font, fill=ink)
        _paste_suit_on(tile, tile.width / 2, tile.height * 0.68, int(CARD_W * 0.115), suit, ink)
        if flipped:
            tile = tile.rotate(180)
        card.alpha_composite(tile, (int(cx - tile.width / 2), int(cy - tile.height / 2)))

    draw_index(CARD_BORDER + margin_x, CARD_BORDER + margin_y)
    draw_index(CARD_W - CARD_BORDER - margin_x, CARD_H - CARD_BORDER - margin_y, flipped=True)

    art_w = CARD_W - 2 * (CARD_BORDER + margin_x) - CARD_W * 0.05
    art_h = CARD_H - 2 * (CARD_BORDER + margin_y) - CARD_H * 0.04
    cx0 = CARD_W / 2
    cy0 = CARD_H / 2

    if rank in ("J", "Q", "K"):
        # Two-headed court panel: a framed band holding a stylized "figure"
        # (crown + rank initial) in the top half, mirrored in the bottom half.
        # A single small suit pip sits centered on the divider, never
        # overlapping the letters.
        band_h = art_h * 0.60
        band_w = art_w * 0.66
        band = Image.new("RGBA", (int(band_w), int(band_h)), (0, 0, 0, 0))

        half = Image.new("RGBA", (band.width, band.height // 2), (0, 0, 0, 0))
        hd = ImageDraw.Draw(half)
        accent = ink
        # crown
        cw = half.width * 0.46
        cx, cy = half.width / 2, half.height * 0.16
        hd.polygon([(cx - cw / 2, cy + 14), (cx - cw / 4, cy - 6), (cx, cy + 8),
                    (cx + cw / 4, cy - 6), (cx + cw / 2, cy + 14)], fill=accent)
        hd.rectangle([cx - cw / 2, cy + 12, cx + cw / 2, cy + 20], fill=accent)
        # rank initial under the crown
        rank_font = _load_font(int(half.height * 0.44))
        tb = hd.textbbox((0, 0), rank, font=rank_font)
        hd.text(((half.width - (tb[2] - tb[0])) / 2 - tb[0],
                 half.height * 0.86 - (tb[3] - tb[1]) - tb[1]), rank, font=rank_font, fill=accent)

        band.alpha_composite(half, (0, 0))
        band.alpha_composite(half.rotate(180), (0, band.height - half.height))

        # center divider + single suit pip
        bd = ImageDraw.Draw(band)
        bd.line([(0, band.height / 2), (band.width, band.height / 2)], fill=accent, width=5)
        _paste_suit_on(band, band.width / 2, band.height / 2, int(band.width * 0.26), suit, accent)

        # frame around the whole band
        bd.rounded_rectangle([2, 2, band.width - 3, band.height - 3], 16, outline=accent, width=5)

        card.alpha_composite(band, (int(cx0 - band.width / 2), int(cy0 - band.height / 2)))
    elif rank == "A":
        _paste_suit(card, cx0, cy0, int(art_w * 0.86), suit, ink)
    else:
        pip = int(art_w * 0.30)
        for (nx, ny) in _pip_positions(rank):
            px = cx0 + (nx - 0.5) * art_w
            py = cy0 + (ny - 0.5) * art_h
            _paste_suit(card, px, py, pip, suit, ink)

    return card

def render_card_back():
    card = Image.new("RGBA", (CARD_W, CARD_H), (0, 0, 0, 0))
    white = Image.new("RGBA", (CARD_W - 2 * CARD_BORDER, CARD_H - 2 * CARD_BORDER), (252, 252, 250, 255))
    mask = _round_rect_mask(*white.size, CARD_RADIUS)
    card.paste(white, (CARD_BORDER, CARD_BORDER), mask)

    d = ImageDraw.Draw(card)
    pad = CARD_BORDER + 18
    d.rectangle([pad, pad, CARD_W - pad - 1, CARD_H - pad - 1], outline=(178, 30, 30, 255), width=6)

    step = 34
    for row in range((CARD_H - 2 * pad) // step + 2):
        y = pad + row * step
        offset = (step // 2) if row % 2 else 0
        for col in range((CARD_W - 2 * pad) // step + 2):
            x = pad + offset + col * step
            r = 7
            d.polygon([(x, y - r), (x + r, y), (x, y + r), (x - r, y)], fill=(178, 30, 30, 255))
    # clip pattern to inner frame
    frame_mask = Image.new("L", card.size, 0)
    ImageDraw.Draw(frame_mask).rectangle([pad + 3, pad + 3, CARD_W - pad - 4, CARD_H - pad - 4], fill=255)
    inner = Image.new("RGBA", card.size, (0, 0, 0, 0))
    inner.paste(card, (0, 0), frame_mask)
    base = Image.new("RGBA", card.size, (0, 0, 0, 0))
    base.paste(white, (CARD_BORDER, CARD_BORDER), mask)
    d2 = ImageDraw.Draw(base)
    d2.rectangle([pad, pad, CARD_W - pad - 1, CARD_H - pad - 1], outline=(178, 30, 30, 255), width=6)
    base.alpha_composite(inner)
    return base

# ------------------------------------------------------------------ wood ----

def _wood_texture(w, h, base=(84, 40, 14), ring=(150, 92, 38), dark=(38, 16, 4), seed=7):
    """Procedural dark-stained wood: non-uniform growth rings with warped,
    non-periodic grain, chromatic streaking, and occasional knots."""
    rnd = random.Random(seed)
    img = Image.new("RGB", (w, h), base)
    px = img.load()

    # a few invisible "knot" centres that warp the ring field locally
    knots = [(rnd.uniform(0.1, 0.9) * w, rnd.uniform(0.1, 0.9) * h, rnd.uniform(40, 110))
             for _ in range(max(2, (w * h) // (512 * 512)))]

    def ring_field(x, y):
        # long-wavelength warp + medium + fine, all incommensurate so no moire
        t = (y
             + 9.0 * math.sin(x / 83.0 + y / 211.0)
             + 4.0 * math.sin(x / 29.0 + 1.7)
             + 2.0 * math.sin(y / 13.0 + x / 61.0))
        # non-uniform ring spacing: low-frequency modulation of the spacing itself
        spacing = 10.0 + 6.0 * math.sin(y / 173.0 + 0.9)
        f = (t / spacing) % 1.0
        band = 0.5 - 0.5 * math.cos(f * 2 * math.pi)
        return band ** 1.5

    for y in range(h):
        for x in range(w):
            b = ring_field(x, y)
            # knots: pull rings around the centre and darken
            for (kx, ky, kr) in knots:
                dx, dy = x - kx, y - ky
                dist = math.hypot(dx, dy)
                if dist < kr * 2.2:
                    swirl = ring_field(x + dx * 0.35, y + dy * 0.35)
                    b = max(b, swirl * (1 - dist / (kr * 2.2)))
                    if dist < kr * 0.5:
                        b = min(1.0, b + 0.55 * (1 - dist / (kr * 0.5)))
            # chromatic streak: slight red/amber drift
            streak = 0.5 + 0.5 * math.sin(x / 149.0 + y / 397.0)
            n = rnd.uniform(-0.045, 0.045)
            k = max(0.0, min(1.0, b * 0.82 + n))
            r = int(base[0] * (1 - k) + ring[0] * k * (0.92 + 0.14 * streak))
            g = int(base[1] * (1 - k) + ring[1] * k)
            bl = int(base[2] * (1 - k) + ring[2] * k * (1.0 - 0.12 * streak))
            px[x, y] = (min(255, r), min(255, g), max(0, bl))
    img = img.filter(ImageFilter.GaussianBlur(0.7))

    # fine broken grain lines (pores) — short, tapered, low contrast
    grain = Image.new("L", (w, h), 0)
    gd = ImageDraw.Draw(grain)
    for _ in range(int(h * 1.1)):
        y0 = rnd.randint(0, h - 1)
        x0 = rnd.randint(0, w - 1)
        ln = rnd.randint(w // 10, w // 2)
        drift = rnd.uniform(-0.02, 0.02)
        pts = [(x0 + i, y0 + int(drift * i + 1.5 * math.sin(i / 23.0))) for i in range(0, ln, 7)]
        if len(pts) > 1:
            gd.line(pts, fill=rnd.randint(14, 40), width=1)
    grain = grain.filter(ImageFilter.GaussianBlur(0.6))
    img = Image.composite(Image.new("RGB", (w, h), dark), img, grain)

    return img

def _vignette(img, strength=0.42):
    w, h = img.size
    mask = Image.new("L", (w, h), 0)
    md = ImageDraw.Draw(mask)
    md.ellipse([-w * 0.25, -h * 0.25, w * 1.25, h * 1.25], fill=255)
    mask = mask.filter(ImageFilter.GaussianBlur(min(w, h) // 6))
    # invert so center is bright (mask=255) and edges fall toward black
    keep = mask.point(lambda v: int(255 - (255 - v) * strength))
    black = Image.new("RGB", (w, h), (0, 0, 0))
    return Image.composite(img, black, keep)

def render_board():
    W = H = 1024
    wood = _wood_texture(W, H, seed=11)
    wood = _vignette(wood, 0.5)

    # subtle lacquer sheen from the top
    sheen = Image.new("L", (W, H), 0)
    sd = ImageDraw.Draw(sheen)
    for i in range(H // 3):
        sd.line([(0, i), (W, i)], fill=int(26 * (1 - i / (H / 3))))
    sheen = sheen.filter(ImageFilter.GaussianBlur(24))
    gloss = Image.new("RGB", (W, H), (255, 230, 190))
    wood = Image.composite(gloss, wood, sheen.point(lambda v: int(v * 0.12)))

    # inner shadow at the very top where the screen glass meets the panel
    seam = Image.new("L", (W, H), 0)
    ImageDraw.Draw(seam).rectangle([0, 0, W, 90], fill=120)
    seam = seam.filter(ImageFilter.GaussianBlur(28))
    wood = Image.composite(Image.new("RGB", (W, H), (12, 4, 0)), wood, seam)

    wood.save(os.path.join(OUT, "board.png"), optimize=True, compress_level=9)

# ---------------------------------------------------------------- buttons ----

def _rounded_gradient(size, radius, top, mid, bottom, vertical=True):
    w, h = size
    grad = Image.new("RGB", (1, h))
    for y in range(h):
        t = y / (h - 1)
        if t < 0.5:
            k = t / 0.5
            c = tuple(int(top[i] * (1 - k) + mid[i] * k) for i in range(3))
        else:
            k = (t - 0.5) / 0.5
            c = tuple(int(mid[i] * (1 - k) + bottom[i] * k) for i in range(3))
        grad.putpixel((0, y), c)
    grad = grad.resize((w, h))
    mask = _round_rect_mask(w, h, radius)
    out = Image.new("RGBA", (w, h), (0, 0, 0, 0))
    out.paste(grad, (0, 0), mask)
    return out

def _center_text(base, text, size, color, y_frac=0.5, stroke=None):
    d = ImageDraw.Draw(base)
    font = _load_font(size)
    tb = d.textbbox((0, 0), text, font=font, stroke_width=0)
    tw, th = tb[2] - tb[0], tb[3] - tb[1]
    x = (base.width - tw) / 2 - tb[0]
    y = base.height * y_frac - th / 2 - tb[1]
    if stroke:
        d.text((x, y), text, font=font, fill=color, stroke_width=max(2, size // 14), stroke_fill=stroke)
    else:
        d.text((x, y), text, font=font, fill=color)

def render_button_shell(filename, label, face_top, face_mid, face_bottom, text_color,
                        pressed=False, text_stroke=None):
    W, H = 1024, 1536
    img = Image.new("RGBA", (W, H), (0, 0, 0, 0))

    # wooden outer bezel
    wood = _wood_texture(W, H, base=(66, 30, 9), ring=(126, 74, 26), dark=(26, 10, 2),
                         seed=hash(label) & 0xFFFF)
    wood = _vignette(wood, 0.55)
    bezel_mask = _round_rect_mask(W, H, 150)
    bezel = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    bezel.paste(wood, (0, 0), bezel_mask)
    img.alpha_composite(bezel)

    # bezel edge highlight/shadow
    edge = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    ed = ImageDraw.Draw(edge)
    ed.rounded_rectangle([6, 6, W - 7, H - 7], 148, outline=(0, 0, 0, 200), width=10)
    ed.rounded_rectangle([16, 16, W - 17, H - 17], 140, outline=(255, 210, 150, 60), width=4)
    img.alpha_composite(edge)

    # recessed well (dark groove the lens sits in)
    inset = 96
    well = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    wd = ImageDraw.Draw(well)
    wd.rounded_rectangle([inset, inset, W - inset, H - inset], 92, fill=(12, 4, 0, 235))
    well = well.filter(ImageFilter.GaussianBlur(6))
    img.alpha_composite(well)

    # illuminated lens
    li = inset + 26
    lens = _rounded_gradient((W - 2 * li, H - 2 * li), 74,
                             face_top, face_mid, face_bottom)
    if pressed:
        lens = Image.eval(lens, lambda v: int(v * 1.0))
    # glass highlight
    hl = Image.new("RGBA", lens.size, (0, 0, 0, 0))
    hd = ImageDraw.Draw(hl)
    hd.ellipse([lens.width * 0.10, lens.height * 0.05, lens.width * 0.90, lens.height * 0.42],
               fill=(255, 255, 255, 46 if pressed else 70))
    hl = hl.filter(ImageFilter.GaussianBlur(18))
    lens.alpha_composite(hl)
    # inner rim shadow at lens bottom
    rim = Image.new("RGBA", lens.size, (0, 0, 0, 0))
    rd = ImageDraw.Draw(rim)
    rd.rounded_rectangle([0, 0, lens.width - 1, lens.height - 1], 74,
                         outline=(0, 0, 0, 110), width=8)
    lens.alpha_composite(rim)
    img.alpha_composite(lens, (li, li))

    # label
    _center_text(img, label, 150, text_color, y_frac=0.5,
                 stroke=text_stroke if text_stroke else (0, 0, 0, 0) if False else None)

    img.save(os.path.join(OUT, filename), optimize=True, compress_level=9)

def render_buttons():
    amber = ((255, 238, 140), (255, 196, 40), (214, 138, 0))
    amber_lit = ((255, 250, 205), (255, 226, 90), (255, 176, 20))
    red = ((255, 120, 110), (226, 30, 22), (150, 8, 6))
    red_lit = ((255, 175, 168), (255, 62, 52), (196, 12, 10))
    green = ((165, 255, 165), (40, 205, 45), (12, 130, 20))
    green_lit = ((215, 255, 215), (95, 240, 95), (25, 170, 30))
    cream = ((255, 255, 255), (238, 230, 210), (198, 186, 156))
    cream_lit = ((255, 255, 255), (252, 248, 236), (226, 216, 188))
    maroon = ((150, 66, 58), (102, 30, 26), (62, 14, 12))
    maroon_lit = ((196, 96, 86), (150, 44, 38), (96, 20, 16))
    gray = ((206, 206, 210), (158, 158, 164), (108, 108, 116))
    gray_lit = ((232, 232, 238), (196, 196, 204), (142, 142, 152))

    render_button_shell("hold_off.png", "HOLD", *amber, (20, 12, 0))
    render_button_shell("hold_on.png", "HOLD", *amber_lit, (20, 12, 0))
    render_button_shell("deal_draw.png", "DEAL", *red, (255, 240, 235))
    render_button_shell("deal_draw_on.png", "DRAW", *red_lit, (255, 245, 240))
    render_button_shell("bet.png", "BET", *green, (4, 40, 6))
    render_button_shell("bet_on.png", "BET", *green_lit, (4, 40, 6))
    render_button_shell("cancel_hold.png", "CANCEL", *cream, (30, 22, 8))
    render_button_shell("cancel_hold_on.png", "CANCEL", *cream_lit, (30, 22, 8))
    render_button_shell("big.png", "BIG", *gray, (28, 28, 32))
    render_button_shell("big_on.png", "BIG", *gray_lit, (28, 28, 32))
    render_button_shell("small.png", "SMALL", *gray, (28, 28, 32))
    render_button_shell("small_on.png", "SMALL", *gray_lit, (28, 28, 32))
    render_button_shell("take_half.png", "TAKE HALF", *maroon, (255, 226, 218))
    render_button_shell("take_half_on.png", "TAKE HALF", *maroon_lit, (255, 232, 226))
    render_button_shell("take_score.png", "TAKE SCORE", *maroon, (255, 226, 218))
    render_button_shell("take_score_on.png", "TAKE SCORE", *maroon_lit, (255, 232, 226))

# ------------------------------------------------------------------- main ----

def main():
    print("rendering cards ->", CARDS_OUT)
    for r in RANKS:
        for s in SUITS:
            render_card(r, s).save(os.path.join(CARDS_OUT, f"{r}{s}.png"), optimize=True)
    render_card_back().save(os.path.join(CARDS_OUT, "bside.png"), optimize=True)
    print("rendering board")
    render_board()
    print("rendering buttons")
    render_buttons()
    print("done ->", OUT)

if __name__ == "__main__":
    main()
