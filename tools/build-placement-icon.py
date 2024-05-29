# -*- coding: utf-8 -*-
"""Generates Smurftown/UI/Images/Ranks/norank.png - the sign for "no rank".

SECOND USE, and that is intentional: this circle was originally drawn as the sign for
open placement matches, because the game shows it in the profile at exactly the spot
where the rank circle otherwise sits. In the row it did not prove itself there - a
dimmed medal says "placement open" better, because it also shows the rank from the
previous season. As the sign for "no rank" it fits, though: unranked and in
placements are neighboring states, and the old solution (bronze medal without a
digit, desaturated and darkened) looked like a real bronze medal on a dimly lit
screen.

It thereby replaces the norank.png that used to come from build-rank-assets.py until
21.08.2026; generating it there has been removed, otherwise the next run would
overwrite this file.

Template: the game shows the circle in the profile at the spot of the rank circle.
The cut-out crop from the game sits next to this script as
tools/placement-referenz.png (110x110, captured on 21.08.2026 at 3440x1440 in the
profile overlay of MUGGLE#21197).

DRAWN, NOT CUT OUT - the same reasoning as with penalty.png and free.png: ingame the
circle measures roughly 90 points, and scaled up that looks like an error. The shape
is not guessed regardless; every number below is measured against the template:

    Ring segments       16      (angular profile at r=34..42, smoothed)
    Ring outer/inner    46/28   of 55 half-sides -> 1.00 / 0.61
    Swirl radius        15      -> 0.33 of the ring outer radius
    Ring bright         (228, 104, 252)
    Ring fill           ( 50,  28,  96)
    Disc                ( 47,  16, 104)
    Swirl               (211,  96, 228)

Whoever changes the shape measures the segment count again instead of nudging it by
feel - just like with free.png. The script does that itself at the end and compares
against the template.

THE SWIRL IS IMPORTED, not copied. It is the same three-armed Nexus swirl as in
free.png, and it comes from Blizzard's own game icon. Two copies of the same path
data would drift apart on the next update; that is why this script pulls ARMS and
parse_path from build-free-icon.py instead of writing them out a second time. The
detour via importlib is necessary because the file name contains a hyphen.

Call from the repo root:  python tools/build-placement-icon.py
"""
import importlib.util
import math
import os

from PIL import Image, ImageDraw

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.join('Smurftown', 'UI', 'Images', 'Ranks', 'norank.png')
REFERENCE = os.path.join(HERE, 'placement-referenz.png')
# THE DISC SITS ON THE MEDALS' CANVAS, and that is the reason for both numbers here.
# It stands in the same image area as they do (Image with Height 78, Width 71,
# Stretch Uniform) - drawn borderless, it would produce a disc that LOOKS bigger than
# any medal even though its frame is the same size: a medal is a shield and tapers
# toward the top and bottom to 41 of 158 points, a disc is full at its whole height.
#
# Measured in the 71x78 box: gold_3.png renders 70.0 x 72.7, a borderless disc 71 x 71.
# With CANVAS 320x352 (double the medals' canvas of 160x176) and SIZE 272, it renders
# 60.3 - roughly 14 percent narrower than the medal and thus visibly subordinate,
# which is correct for a "no rank" sign.
#
# Double, not the canvas itself, so there are enough real pixels: at 250 percent
# Windows scaling, the 60-point disc needs 151 of them. The same math as for the
# hero portraits.
SIZE = 272
CANVAS_W, CANVAS_H = 320, 352
SS = 4  # supersampling - Pillow does not smooth lines on its own

RING_LIGHT = (228, 104, 252)
RING_FILL = (50, 28, 96)
DISC = (47, 16, 104)
SWIRL = (211, 96, 228)

R_RING_OUTER = 1.00
R_RING_INNER = 0.61
R_SWIRL = 0.33

SEGMENTS = 16
GAP_DEGREES = 5.0   # gap between two cells
OUTLINE = 0.028     # cell-border stroke width, as a fraction of the radius


def swirl_arms():
    """ARMS and parse_path from build-free-icon.py - written once, used twice."""
    path = os.path.join(HERE, 'build-free-icon.py')
    spec = importlib.util.spec_from_file_location('build_free_icon', path)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)

    polygons = []
    for arm in module.ARMS:
        polygons.extend(module.parse_path(arm))
    return polygons


def cell(draw, cx, cy, inner, outer, start, end, fill, outline, width):
    """One ring cell as a polygon: outward clockwise, back inward."""
    steps = 12
    points = []
    for i in range(steps + 1):
        a = math.radians(start + (end - start) * i / steps)
        points.append((cx + outer * math.cos(a), cy + outer * math.sin(a)))
    for i in range(steps + 1):
        a = math.radians(end + (start - end) * i / steps)
        points.append((cx + inner * math.cos(a), cy + inner * math.sin(a)))
    draw.polygon(points, fill=fill)
    draw.line(points + [points[0]], fill=outline, width=width, joint='curve')


def main():
    side = SIZE * SS
    image = Image.new('RGBA', (side, side), (0, 0, 0, 0))
    draw = ImageDraw.Draw(image)

    cx = cy = side / 2
    radius = side / 2 - 1

    # The dark disc under everything. It reaches just under the ring, so there is no
    # transparent seam between the two.
    inner = radius * R_RING_INNER
    draw.ellipse([cx - inner - 1, cy - inner - 1, cx + inner + 1, cy + inner + 1],
                 fill=DISC + (255,))

    step = 360.0 / SEGMENTS
    width = max(1, int(round(radius * OUTLINE)))
    for i in range(SEGMENTS):
        start = i * step + GAP_DEGREES / 2
        end = (i + 1) * step - GAP_DEGREES / 2
        cell(draw, cx, cy, inner, radius * R_RING_OUTER, start, end,
             RING_FILL + (255,), RING_LIGHT + (255,), width)

    # The swirl, centered on the disc.
    polys = swirl_arms()
    points = [p for poly in polys for p in poly]
    src_cx = (min(p[0] for p in points) + max(p[0] for p in points)) / 2
    src_cy = (min(p[1] for p in points) + max(p[1] for p in points)) / 2
    src_radius = max(math.hypot(p[0] - src_cx, p[1] - src_cy) for p in points)
    scale = radius * R_SWIRL / src_radius

    for poly in polys:
        draw.polygon([(cx + (px - src_cx) * scale, cy + (py - src_cy) * scale)
                      for px, py in poly], fill=SWIRL + (255,))

    image = image.resize((SIZE, SIZE), Image.LANCZOS)

    # The DISC is cross-checked, not the canvas: ring_segments measures from the
    # image center in fractions of the image radius and would find nothing on the
    # padded version.
    verify(image)

    canvas = Image.new('RGBA', (CANVAS_W, CANVAS_H), (0, 0, 0, 0))
    canvas.paste(image, ((CANVAS_W - SIZE) // 2, (CANVAS_H - SIZE) // 2), image)

    os.makedirs(os.path.dirname(OUT), exist_ok=True)
    canvas.save(OUT, 'PNG', optimize=True)
    print('%s  %dx%d, disc %d  %d bytes'
          % (OUT, CANVAS_W, CANVAS_H, SIZE, os.path.getsize(OUT)))


def ring_segments(image):
    """How many bright sections a circle through the middle of the ring crosses.

    The threshold is the image's OWN mean, so that template and drawing can be
    compared despite different brightness. The bright AREA FRACTION is explicitly
    unsuitable for this: the template carries an ingame glow, the drawing does not,
    and so the mean sits at different points. This was measured - the fraction stayed
    at 0.38 regardless of how thin the outline was drawn.
    """
    side = image.size[0]
    px = image.convert('RGB').load()
    center = side / 2.0
    radius = side / 2 - 1

    # AVERAGE OVER THE RING BAND, do not measure at a single radius. Near the outer
    # edge the template's ring breaks up into fragments - measured there, it came
    # out as 7 instead of 16, without anything being wrong with the image.
    band = [(R_RING_INNER + 0.06 + i * 0.07) * radius for i in range(5)]

    profile = []
    for a in range(720):
        angle = math.radians(a / 2.0)
        value = 0.0
        for rr in band:
            r, g, b = px[int(round(center + rr * math.cos(angle))),
                         int(round(center + rr * math.sin(angle)))]
            value += r + g + b
        profile.append(value / len(band))

    smooth = [sum(profile[(i + k) % 720] for k in range(-4, 5)) / 9.0 for i in range(720)]
    mean = sum(smooth) / len(smooth)
    above = [v > mean for v in smooth]
    return sum(1 for i in range(720) if above[i] and not above[i - 1])


def verify(image):
    """Segment count against the template - with the same function on both images."""
    mine = ring_segments(image)
    if not os.path.exists(REFERENCE):
        print('  cross-check: %d segments (template missing)' % mine)
        return

    theirs = ring_segments(Image.open(REFERENCE))
    # Two segments of deviation are tolerance, not an error: the template carries a
    # glow that creates additional transitions at the cell edges.
    verdict = 'matches' if abs(mine - theirs) <= 2 else 'DEVIATES'
    print('  cross-check: %d segments, template %d - %s' % (mine, theirs, verdict))


if __name__ == '__main__':
    main()
