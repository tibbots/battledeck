#!/usr/bin/env python3
"""
Generates the HotS rank medals in Smurftown/UI/Images/Ranks/.

Why this script exists: there is no official source with finished medals per
division. Both source images show the tiers exclusively with a baked-in "1"; the
divisions 2-5 have to be generated.

12 files are generated:
  {tier}.png            Bronze..Diamond WITHOUT a division digit (5x)
  {tier}_fill.png       light in the medal's own channel, for the progress (5x)
  master.png            from the official animated medal, its baked-in score inpainted away
  grandmaster.png       carries no number in the HIRES source

Until 24.08.2026 there were 25 tier files, one per division, each with the digit
baked in. The digit is now drawn by the application (RankMedalView), and the ring
carries the progress inside the division. Both wanted the same thing from this
script: ONE image per tier instead of five. What is baked in cannot be lit in part,
and a digit rasterised at 160 px is soft by the time the row shows it at 78.

norank.png has NOT been part of this since 21.08.2026. It used to be the bronze
medal without a digit and without a title line, desaturated and darkened - which
made it look like a real bronze medal on a dimly lit screen. In its place now sits
the magenta-colored circle from tools/build-placement-icon.py. Whoever adds its
generation back in here overwrites it on the next run.

Sources (not in the repo, download at run time):
  TIERS_PNG   Bronze..Diamond, with alpha channel, 800x176
              https://static.wikia.nocookie.net/allstars_gamepedia/images/7/77/Ranked_Play_Tiers.png/revision/latest?cb=20160617144056&format=original
  HIRES_JPG   Grand Master on a starfield background (shows all 7 tiers)
              https://images.squarespace-cdn.com/content/v1/59af2189c534a58c97bd63b3/1520455238475-43375HIMQPZH9QXMYBWG/2018+ranked+season+1+tiers+hots.jpg?format=2500w
              (send Accept: image/jpeg, otherwise the CDN serves WebP)
  MASTER_GIF  the official animated Master medal, 380x380, 235 frames, with a
              baked-in "1000" season score
              https://bnetcmsus-a.akamaihd.net/cms/content_folder_media/5XW63A65ZXGW1462404445716.gif

Approach:
  Bronze..Diamond  The medals in the sheet do NOT sit on a 160px grid -
                   their symmetry axes are at x = 80.5 / 236.5 / 390 / 550 / 710.5.
                   Whoever cuts stubbornly at 160-boundaries gets the medal up to
                   10px too far left in the image, and with it a digit visibly
                   shifted to the right. Hence: measure the axis per medal (alpha
                   silhouette against its own mirror image), crop centered on that,
                   remove clipped neighbors via connected-component analysis.
                   Then remove the digit via diffusion inpainting and set the
                   division anew. Cloning from neighboring regions and rotation were
                   discarded: the former drags ring edges and the title line into
                   the center, the latter brings the digit itself right back into
                   the image.
  Master           rebuilt from MASTER_GIF. The medal itself stands still across
                   all 235 frames, only glow and particles animate - a per-pixel
                   median over the frames therefore removes particles, pulsing
                   glow and GIF dithering in one step. The baked-in "1000" is
                   removed with the same diffusion inpainting as the division
                   digits; the glow halo around it deliberately stays and
                   diffuses into the hole, where it reads as the galaxy core
                   above the crown. Then flood-fill cutout as for Grand Master,
                   no hand masks needed.
  Grand Master     cut out from HIRES_JPG. Flood fill from the edge with a
                   protective circle around the inner disc - the GM ring has gaps,
                   without the protective circle the fill runs through to the
                   inside. Afterward keep the largest component (removes isolated
                   stars) and a hand mask against a clinging background scrap.

Dependency: Pillow.  Call:

  python tools/build-rank-assets.py <tiers.png> <hires.jpg> <master.gif>   everything
  python tools/build-rank-assets.py <tiers.png>                            tiers only

The short form exists because Master and Grand Master have neither a division nor a
ring and therefore change with nothing done here - while their two source files are
the ones that are awkward to get hold of. Whoever regenerates the tiers should not
have to fetch a 12 MB GIF for two files that come out identical.
"""
import math
import os
import sys
from collections import deque

from PIL import Image, ImageChops, ImageDraw, ImageFilter, ImageSequence

OUT = os.path.join('Smurftown', 'UI', 'Images', 'Ranks')
CANVAS = (160, 176)
TIERS = ['bronze', 'silver', 'gold', 'platinum', 'diamond']
FONT = 'C:/Windows/Fonts/seguibl.ttf'

DIGIT_BOX = (65, 61, 95, 102)   # baked-in digit incl. drop shadow, axis-centered

# Where the division digit goes. NOTHING HERE DRAWS IT ANY MORE - the application
# does, in RankMedalView, and reads these as fractions of the canvas. They stay here
# because here is where they were measured, against the original sheet. Whoever
# moves them changes the app, not this script.
DIGIT_CX, DIGIT_CY = 80, 82     # center point of the digit, relative to the symmetry axis
DIGIT_HEIGHT = 30               # cap height, measured on the original

# THE CHANNEL, PER TIER. Every medal carries TWO metal rings, and between them runs a
# dark groove - that groove is where the game shows progress inside the division, and
# it is what fills up. Nothing here is darkened: the empty state IS the artwork, and
# the progress puts light into a channel that is already there.
#
# Measured on 24.08.2026 as the trough in the radial profile between the two bright
# bands, over the top sector where neither crest nor laurel reaches in. The five do not
# share it - Diamond's groove sits further out and is wider than the rest.
#
# TORUS is the inner ring, and is not written out to a file. It stands here because the
# colour of the light is taken from it: the channel glows in its own medal's metal, and
# reading that off beats five colours typed in by hand.
#
#              cx      cy     channel      torus
CHANNEL = {
    'bronze':   (81.13, 78.80, 57.0, 62.0, 48.0, 56.5),
    'silver':   (80.87, 79.77, 56.5, 61.0, 47.5, 56.0),
    'gold':     (80.13, 80.24, 57.0, 62.0, 48.0, 56.5),
    'platinum': (80.06, 80.50, 56.0, 61.0, 47.5, 56.0),
    'diamond':  (80.70, 81.05, 58.5, 64.0, 50.0, 58.5),
}

# The crest sits ON the channel at six o'clock, drawn in FRONT of it. The wedge is left
# out, or the light would run across the crest. One number for all five and deliberately
# a touch generous: what it leaves dark hides behind the crest anyway, while a wedge too
# narrow paints over the crest itself - and that is visible at once.
RING_WEDGE = 38                 # half width in degrees, around six o'clock
RING_SUPERSAMPLE = 4

# The light. Taken from the medal's own metal and then pulled towards white - a groove
# glowing in exactly the colour of the ring beside it would not read as lit but as a
# groove somebody had filled in.
#
# HOW FAR TOWARDS WHITE IS SET BY THE ROW, not by the medal at full size. The account
# row shows the medal at 78 points, where the channel is 2.2 points wide; at a gentler
# 0.42 the arc read there as "this medal is a little brighter on one side" rather than
# as a reading. Pushed all the way to white it stops belonging to the metal - on bronze
# it then looks like a wire laid into the groove.
FILL_TOWARDS_WHITE = 0.60
FILL_CORE_TOWARDS_WHITE = 0.88  # the brighter middle of the channel
FILL_CORE_WIDTH = 0.28          # its half width, as a share of the channel
FILL_BLOOM_RADIUS = 2.0         # spills over both metal edges, which is what makes it glow
FILL_BLOOM_STRENGTH = 0.50

# master.png - all values measured on the 380x380 median of MASTER_GIF
MASTER_SCORE_BOX = (124, 162, 252, 220)  # digit core of the "1000" (x 130-246 / y 168-214) + margin
MASTER_KEEP = (190, 190, 100)            # protective circle around the inner disc
MASTER_THRESHOLD = 200                   # robust: 140 and 260 yield the same silhouette


def largest_component(alpha):
    """Keeps only the largest connected area - removes clipped neighbors."""
    w, h = alpha.size
    ap = alpha.load()
    seen = [[False] * w for _ in range(h)]
    best = []
    for sy in range(h):
        for sx in range(w):
            if ap[sx, sy] <= 10 or seen[sy][sx]:
                continue
            comp, dq = [], deque([(sx, sy)])
            seen[sy][sx] = True
            while dq:
                x, y = dq.popleft()
                comp.append((x, y))
                for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1)):
                    nx, ny = x + dx, y + dy
                    if 0 <= nx < w and 0 <= ny < h and ap[nx, ny] > 10 and not seen[ny][nx]:
                        seen[ny][nx] = True
                        dq.append((nx, ny))
            if len(comp) > len(best):
                best = comp
    out = Image.new('L', (w, h), 0)
    op = out.load()
    for x, y in best:
        op[x, y] = ap[x, y]
    return out


def symmetry_axis(alpha, lo, hi):
    """Vertical symmetry axis of the medal within the window [lo,hi), accurate to 0.1px."""
    ap = alpha.load()
    w, h = alpha.size
    best = (float('inf'), (lo + hi) / 2)
    for step in range(lo * 10, hi * 10):
        c = step / 10.0
        err = count = 0
        for y in range(10, h - 6, 2):
            for d in range(5, 70, 2):
                xl, xr = int(round(c - d)), int(round(c + d))
                if xl < 0 or xr >= w:
                    continue
                err += (ap[xl, y] - ap[xr, y]) ** 2
                count += 1
        if count and err / count < best[0]:
            best = (err / count, c)
    return best[1]


def extract_tier(sheet, index):
    """Cuts one medal out of the sheet, centered on its axis."""
    alpha = sheet.split()[3]
    axis = symmetry_axis(alpha, index * 160 + 40, index * 160 + 120)
    left = int(round(axis)) - CANVAS[0] // 2
    tile = sheet.crop((left, 0, left + CANVAS[0], CANVAS[1])).convert('RGBA')
    tile.putalpha(largest_component(tile.split()[3]))
    return tile, axis


def inpaint(im, draw_hole, iterations=40, radius=6, feather=1.5):
    """Diffusion inpainting: the masked area fills in from the edge inward."""
    rgb, alpha = im.convert('RGB'), im.split()[3]
    hole = Image.new('L', im.size, 0)
    draw_hole(ImageDraw.Draw(hole))
    keep = Image.eval(hole.filter(ImageFilter.GaussianBlur(feather)), lambda v: 255 - v)
    cur = rgb.copy()
    for _ in range(iterations):
        cur = Image.composite(cur, cur.filter(ImageFilter.GaussianBlur(radius)), keep)
    out = cur.convert('RGBA')
    out.putalpha(alpha)
    return out


def remove_digit(im):
    return inpaint(im, lambda d: d.ellipse(DIGIT_BOX, fill=255))


def annulus(size, cx, cy, r_in, r_out, wedge=RING_WEDGE):
    """A ring band as a soft-edged mask, minus the wedge the crest sits in.

    Drawn at RING_SUPERSAMPLE and scaled down, because a circle rasterised at 1:1
    has a staircase edge that survives every later blur as a visible ripple."""
    ss = RING_SUPERSAMPLE
    w, h = size
    m = Image.new('L', (w * ss, h * ss), 0)
    d = ImageDraw.Draw(m)
    d.ellipse([(cx - r_out) * ss, (cy - r_out) * ss, (cx + r_out) * ss, (cy + r_out) * ss], fill=255)
    d.ellipse([(cx - r_in) * ss, (cy - r_in) * ss, (cx + r_in) * ss, (cy + r_in) * ss], fill=0)
    if wedge:
        reach = (r_out + 4) * ss
        d.pieslice([cx * ss - reach, cy * ss - reach, cx * ss + reach, cy * ss + reach],
                   90 - wedge, 90 + wedge, fill=0)
    return m.resize((w, h), Image.LANCZOS).filter(ImageFilter.GaussianBlur(0.6))


def metal_colour(tile, cx, cy, r_in, r_out):
    """The medal's own metal, read off its inner ring where neither crest nor laurel
    reaches in. Averaged over the brighter half only - averaging the whole band would
    drag the shadowed side in and hand back a colour the medal never shows."""
    px = tile.load()
    w, h = tile.size
    samples = []
    for step in range(0, 360, 2):
        if 90 - RING_WEDGE <= step <= 90 + RING_WEDGE:
            continue
        a = math.radians(step)
        r = r_in
        while r <= r_out:
            x, y = int(round(cx + r * math.cos(a))), int(round(cy + r * math.sin(a)))
            if 0 <= x < w and 0 <= y < h:
                cr, cg, cb, ca = px[x, y]
                if ca > 200:
                    samples.append((0.299 * cr + 0.587 * cg + 0.114 * cb, cr, cg, cb))
            r += 1.0
    samples.sort(reverse=True)
    top = samples[:max(1, len(samples) // 2)]
    return tuple(int(sum(s[i] for s in top) / len(top)) for i in (1, 2, 3))


def channel_fill(tile, tier):
    """The lit channel - transparent everywhere else.

    The application lays this over the finished medal and clips it to the part the
    account HAS reached. Nothing gets darkened anywhere: the empty groove is the
    artwork itself, and this file is only the light that goes into it.

    The bloom is not decoration. A band of colour with a clean edge reads as paint in
    a groove; light spilling onto the metal on both sides is what reads as glowing."""
    cx, cy, r_in, r_out, torus_in, torus_out = CHANNEL[tier]
    colour = metal_colour(tile, cx, cy, torus_in, torus_out)
    lit = tuple(int(c + (255 - c) * FILL_TOWARDS_WHITE) for c in colour)
    print(f'  {tier:11s} channel {r_in}..{r_out}  metal {colour} -> light {lit}')

    band = annulus(tile.size, cx, cy, r_in, r_out)
    bloom = Image.eval(band.filter(ImageFilter.GaussianBlur(FILL_BLOOM_RADIUS)),
                       lambda v: int(v * FILL_BLOOM_STRENGTH))

    # Nothing may glow where the medal is not - the laurel has gaps the bloom would
    # otherwise shine straight through.
    alpha = ImageChops.multiply(ImageChops.lighter(band, bloom), tile.split()[3])

    out = Image.new('RGBA', tile.size, lit + (0,))
    out.putalpha(alpha)

    # A brighter middle. Light in a groove is brightest where the groove is deepest, and
    # without that the band reads as flat paint - most of all at the size the row uses,
    # where the whole channel is barely two points wide.
    middle, half = (r_in + r_out) / 2, (r_out - r_in) * FILL_CORE_WIDTH
    core = Image.new('RGBA', tile.size,
                     tuple(int(c + (255 - c) * FILL_CORE_TOWARDS_WHITE) for c in colour) + (0,))
    core.putalpha(ImageChops.multiply(
        annulus(tile.size, cx, cy, middle - half, middle + half), tile.split()[3]))
    out.alpha_composite(core)
    return out


def cutout(im, threshold, keep_circle):
    im = im.convert('RGB')
    w, h = im.size
    px = im.load()
    cx, cy, r = keep_circle
    bright = [[sum(px[x, y]) for x in range(w)] for y in range(h)]
    bg = [[False] * w for _ in range(h)]
    queue = deque()

    def seed(x, y):
        if bg[y][x] or bright[y][x] >= threshold:
            return
        if (x - cx) ** 2 + (y - cy) ** 2 < r * r:   # protect the inner disc
            return
        bg[y][x] = True
        queue.append((x, y))

    for x in range(w):
        seed(x, 0), seed(x, h - 1)
    for y in range(h):
        seed(0, y), seed(w - 1, y)
    while queue:
        x, y = queue.popleft()
        for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1)):
            if 0 <= x + dx < w and 0 <= y + dy < h:
                seed(x + dx, y + dy)

    mask = Image.new('L', (w, h), 0)
    mp = mask.load()
    for y in range(h):
        for x in range(w):
            if not bg[y][x]:
                mp[x, y] = 255
    out = im.convert('RGBA')
    out.putalpha(largest_component(mask).filter(ImageFilter.GaussianBlur(0.8)))
    return out


def median_of_frames(gif):
    """Per-pixel median over all frames. The medal stands still, only glow and
    particles animate - the median removes both, plus the GIF dithering."""
    frames = [f.convert('RGB') for f in ImageSequence.Iterator(gif)]
    px = [f.load() for f in frames]
    w, h = frames[0].size
    mid = len(frames) // 2
    out = Image.new('RGB', (w, h))
    op = out.load()
    for y in range(h):
        for x in range(w):
            op[x, y] = tuple(sorted(p[x, y][c] for p in px)[mid] for c in range(3))
    return out


def build_master(gif):
    base = median_of_frames(gif).convert('RGBA')
    print(f'  master      median over {gif.n_frames} frames')
    # The hole covers only the digit core of the "1000". The glow halo around it
    # stays and diffuses into the hole - it reads as the galaxy core above the
    # crown; a hole covering the halo too would leave a flat, empty disc.
    base = inpaint(base, lambda d: d.rounded_rectangle(MASTER_SCORE_BOX, radius=24, fill=255),
                   iterations=60, radius=6, feather=2.0)
    return normalise(cutout(base, MASTER_THRESHOLD, MASTER_KEEP))


def normalise(im, canvas=CANVAS):
    im = im.crop(im.split()[3].getbbox())
    scale = min(canvas[0] / im.width, canvas[1] / im.height)
    im = im.resize((int(im.width * scale), int(im.height * scale)), Image.LANCZOS)
    out = Image.new('RGBA', canvas, (0, 0, 0, 0))
    out.paste(im, ((canvas[0] - im.width) // 2, (canvas[1] - im.height) // 2), im)
    return out


def main(tiers_png, hires_jpg=None, master_gif=None):
    os.makedirs(OUT, exist_ok=True)
    sheet = Image.open(tiers_png).convert('RGBA')
    for i, tier in enumerate(TIERS):
        tile, axis = extract_tier(sheet, i)
        print(f'  {tier:11s} symmetry axis x={axis:6.1f}  (grid would be {i * 160 + 80})')
        base = remove_digit(tile)
        base.save(os.path.join(OUT, f'{tier}.png'))
        # Cut from the medal WITH its digit already removed, so both files line up pixel
        # for pixel - the light has to land exactly in the groove.
        channel_fill(base, tier).save(os.path.join(OUT, f'{tier}_fill.png'))

    if master_gif is None or hires_jpg is None:
        print('  master/grandmaster skipped - sources not given, existing files left alone')
    else:
        build_master(Image.open(master_gif)).save(os.path.join(OUT, 'master.png'))

        # crop, brightness threshold, protective circle, hand mask against a background remnant
        gm = cutout(Image.open(hires_jpg).crop((925, 450, 1330, 885)), 340, (203, 205, 112))
        alpha = gm.split()[3]
        ImageDraw.Draw(alpha).rectangle((50, 0, 110, 84), fill=0)
        gm.putalpha(alpha)
        normalise(gm).save(os.path.join(OUT, 'grandmaster.png'))

    print(f'{len(os.listdir(OUT))} assets in {OUT}')


if __name__ == '__main__':
    if len(sys.argv) not in (2, 4):
        sys.exit(__doc__)
    main(*sys.argv[1:])
