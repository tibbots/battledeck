#!/usr/bin/env python3
"""
Generates the HotS rank medals in Smurftown/UI/Images/Ranks/.

Why this script exists: there is no official source with finished medals per
division. Both source images show the tiers exclusively with a baked-in "1"; the
divisions 2-5 have to be generated.

27 files are generated:
  {tier}_{1..5}.png     Bronze..Diamond with division (25x)
  master.png            Master and Grand Master carry no number in the original (2x)
  grandmaster.png

norank.png has NOT been part of this since 21.08.2026. It used to be the bronze
medal without a digit and without a title line, desaturated and darkened - which
made it look like a real bronze medal on a dimly lit screen. In its place now sits
the magenta-colored circle from tools/build-placement-icon.py. Whoever adds its
generation back in here overwrites it on the next run.

Sources (not in the repo, download at run time):
  TIERS_PNG  Bronze..Diamond, with alpha channel, 800x176
             https://static.wikia.nocookie.net/allstars_gamepedia/images/7/77/Ranked_Play_Tiers.png/revision/latest?cb=20160617144056&format=original
  HIRES_JPG  all 7 tiers incl. Master/Grand Master, on a starfield background
             https://images.squarespace-cdn.com/content/v1/59af2189c534a58c97bd63b3/1520455238475-43375HIMQPZH9QXMYBWG/2018+ranked+season+1+tiers+hots.jpg?format=2500w
             (send Accept: image/jpeg, otherwise the CDN serves WebP)

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
  Master/GM        cut out from HIRES_JPG. Flood fill from the edge with a
                   protective circle around the inner disc - the GM ring has gaps,
                   without the protective circle the fill runs through to the
                   inside. Afterward keep the largest component (removes isolated
                   stars) and two hand masks against clinging background scraps.

Dependency: Pillow.  Call:  python tools/build-rank-assets.py <tiers.png> <hires.jpg>
"""
import os
import sys
from collections import deque

from PIL import Image, ImageDraw, ImageFilter, ImageFont

OUT = os.path.join('Smurftown', 'UI', 'Images', 'Ranks')
CANVAS = (160, 176)
TIERS = ['bronze', 'silver', 'gold', 'platinum', 'diamond']
FONT = 'C:/Windows/Fonts/seguibl.ttf'

DIGIT_BOX = (65, 61, 95, 102)   # baked-in digit incl. drop shadow, axis-centered
DIGIT_CX, DIGIT_CY = 80, 82     # center point of the new digit, relative to the symmetry axis
DIGIT_HEIGHT = 30               # cap height, measured on the original


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


def draw_digit(base, char):
    for size in range(20, 90):
        font = ImageFont.truetype(FONT, size)
        box = font.getbbox(char)
        if box[3] - box[1] >= DIGIT_HEIGHT:
            break
    w, h = box[2] - box[0], box[3] - box[1]
    x, y = DIGIT_CX - w / 2 - box[0], DIGIT_CY - h / 2 - box[1]
    shadow = Image.new('RGBA', base.size, (0, 0, 0, 0))
    ImageDraw.Draw(shadow).text((x + 1, y + 2), char, font=font, fill=(0, 0, 0, 190))
    out = Image.alpha_composite(base.copy(), shadow.filter(ImageFilter.GaussianBlur(1.6)))
    ImageDraw.Draw(out).text((x, y), char, font=font, fill=(252, 250, 248, 255))
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


def normalise(im, canvas=CANVAS):
    im = im.crop(im.split()[3].getbbox())
    scale = min(canvas[0] / im.width, canvas[1] / im.height)
    im = im.resize((int(im.width * scale), int(im.height * scale)), Image.LANCZOS)
    out = Image.new('RGBA', canvas, (0, 0, 0, 0))
    out.paste(im, ((canvas[0] - im.width) // 2, (canvas[1] - im.height) // 2), im)
    return out


def main(tiers_png, hires_jpg):
    os.makedirs(OUT, exist_ok=True)
    sheet = Image.open(tiers_png).convert('RGBA')
    for i, tier in enumerate(TIERS):
        tile, axis = extract_tier(sheet, i)
        print(f'  {tier:11s} symmetry axis x={axis:6.1f}  (grid would be {i * 160 + 80})')
        base = remove_digit(tile)
        for division in range(1, 6):
            draw_digit(base, str(division)).save(os.path.join(OUT, f'{tier}_{division}.png'))

    hires = Image.open(hires_jpg)
    # crop, brightness threshold, protective circle, hand masks against background remnants
    for name, crop, circle, kills in [
        ('master', (585, 455, 905, 870), (163, 200, 100), [(258, 0, 320, 415)]),
        ('grandmaster', (925, 450, 1330, 885), (203, 205, 112), [(50, 0, 110, 84)]),
    ]:
        im = cutout(hires.crop(crop), 340, circle)
        alpha = im.split()[3]
        for rect in kills:
            ImageDraw.Draw(alpha).rectangle(rect, fill=0)
        im.putalpha(alpha)
        normalise(im).save(os.path.join(OUT, f'{name}.png'))

    print(f'{len(os.listdir(OUT))} assets in {OUT}')


if __name__ == '__main__':
    if len(sys.argv) != 3:
        sys.exit(__doc__)
    main(sys.argv[1], sys.argv[2])
