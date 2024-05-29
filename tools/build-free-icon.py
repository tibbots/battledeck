#!/usr/bin/env python3
"""
Generates Smurftown/UI/Images/free.png - the badge for heroes that are free to play
in the current rotation period.

Ingame this is a small blue-and-white circle: a bright ring edge, inside it a deep
blue disc, and on top the three-armed Nexus swirl in white. It sits on the hero tile
in the collection, top left.

Why drawn instead of cut out: ingame the badge measures roughly 27 pixels. A crop of
it looks like an error when scaled up - the same situation as with penalty.png. The
shape does not have to be guessed though, because the swirl exists as a clean vector
template:

  Swirl    https://baseui.akamaized.net/icons/heroes-of-the-storm.svg
           Blizzard's own game icon, three Bezier paths in a 48-unit viewport.
           Only the three arms are taken over, not the hexagon around them - ingame
           the swirl sits on a disc, not on a hexagon.
  Colors   measured from a screenshot of the badge (27 px diameter). Radial
           average: disc inside (10, 54, 194), outside (12, 49, 180), ring edge
           (158, 211, 255) with white tips, arms pure white.

Rendered at 4x supersampling and then downscaled, because Pillow does not
anti-alias lines.

Dependency: Pillow.  Call:  python tools/build-free-icon.py
"""
import math
import os
import re

from PIL import Image, ImageDraw

OUT = os.path.join('Smurftown', 'UI', 'Images', 'free.png')
SIZE = 128
SS = 4  # supersampling

# Measured colors. The disc has a barely visible gradient toward the outside - it is
# kept here anyway because it makes the disc look dimensional ingame instead of flat.
DISC_INNER = (10, 54, 194)
DISC_OUTER = (10, 47, 176)
RING = (158, 211, 255)
# Ingame the ring glows brighter at the top left. That is the game engine's bloom and
# is deliberately not baked in here - at 128 pixels a fixed painted highlight would
# look like dirt, and the badge has no light source it could match.
OUTLINE = (6, 27, 86)
SWIRL = (255, 255, 255)

# Fractions of the radius, from outside to inside: dark outline, bright ring band,
# disc. Measured from the screenshot - the ring band sits there between 0.81 and 1.0
# of the radius.
R_OUTLINE = 1.00
R_RING = 0.95
R_DISC = 0.80
# Size of the swirl. The value is not estimated but tuned: in the screenshot 28% of
# the disc area is bright, and at 0.57 the drawing matches that. Whoever changes the
# shape measures this again (bright fraction at radius < 0.78 R) instead of turning
# the dial by feel.
R_SWIRL = 0.57

# The three arms from Blizzard's SVG, viewport 48x48. Taken over verbatim - whoever
# wants to adjust them fetches the file again instead of tweaking numbers here.
ARMS = [
    'M9.593 22.306c1.078-9.7 9.522-12.216 9.522-12.216C7.976 22.127 21.27 30.57 21.27 '
    '30.57l-8.982 1.976c.18 0-3.054-3.772-2.695-10.24',
    'M29.894 37.577c-9.522 3.593-15.091-2.515-15.091-2.515 15.63 3.952 16.707-12.037 '
    '16.707-12.037l5.929 7.006s-1.617 4.85-7.546 7.546',
    'M38.337 26.618c-4.67-15.63-18.145-8.084-18.145-8.084l2.875-9.162s5.749-.719 10.958 '
    '3.772c6.288 5.39 4.312 13.474 4.312 13.474',
]

NUMBER = re.compile(r'[-+]?(?:\d*\.\d+|\d+\.?\d*)(?:[eE][-+]?\d+)?')
COMMAND = re.compile(r'[MmLlCcSsZz]')
FLATTEN = 24  # subdivisions per Bezier


def parse_path(d):
    """Enough for these three paths: M/m, L/l, C/c, S/s, Z/z with repeated parameter sets.

    Returns a list of polygons (point lists). Not a general SVG parser - whatever is
    missing here simply does not occur in the templates.
    """
    tokens = []
    pos = 0
    while pos < len(d):
        char = d[pos]
        if COMMAND.match(char):
            tokens.append(char)
            pos += 1
            continue
        match = NUMBER.match(d, pos)
        if match:
            tokens.append(float(match.group()))
            pos = match.end()
            continue
        pos += 1  # separator

    polys, current = [], []
    x = y = 0.0
    prev_ctrl = None
    command = None
    i = 0
    while i < len(tokens):
        if isinstance(tokens[i], str):
            command = tokens[i]
            i += 1
            if command in 'Zz':
                if current:
                    polys.append(current)
                    current = []
                continue

        def take(n):
            nonlocal i
            values = tokens[i:i + n]
            i += n
            return values

        rel = command.islower()
        upper = command.upper()

        if upper == 'M':
            dx, dy = take(2)
            x, y = (x + dx, y + dy) if rel else (dx, dy)
            if current:
                polys.append(current)
            current = [(x, y)]
            prev_ctrl = None
            command = 'l' if rel else 'L'  # further pairs after M are lines
        elif upper == 'L':
            dx, dy = take(2)
            x, y = (x + dx, y + dy) if rel else (dx, dy)
            current.append((x, y))
            prev_ctrl = None
        elif upper in ('C', 'S'):
            if upper == 'C':
                x1, y1, x2, y2, dx, dy = take(6)
                c1 = (x + x1, y + y1) if rel else (x1, y1)
            else:
                x2, y2, dx, dy = take(4)
                # The first control point mirrors the last one of the predecessor.
                c1 = (2 * x - prev_ctrl[0], 2 * y - prev_ctrl[1]) if prev_ctrl else (x, y)
            c2 = (x + x2, y + y2) if rel else (x2, y2)
            end = (x + dx, y + dy) if rel else (dx, dy)
            for step in range(1, FLATTEN + 1):
                t = step / FLATTEN
                current.append(bezier((x, y), c1, c2, end, t))
            x, y = end
            prev_ctrl = c2
        else:
            i += 1  # unknown, skip instead of guessing

    if current:
        polys.append(current)
    return polys


def bezier(p0, p1, p2, p3, t):
    u = 1 - t
    a, b, c, d = u * u * u, 3 * u * u * t, 3 * u * t * t, t * t * t
    return (a * p0[0] + b * p1[0] + c * p2[0] + d * p3[0],
            a * p0[1] + b * p1[1] + c * p2[1] + d * p3[1])


def disc(draw, cx, cy, radius, inner, outer):
    """Disc with a radial gradient, painted as rings from outside to inside."""
    steps = max(2, int(radius))
    for step in range(steps, 0, -1):
        t = step / steps
        color = tuple(round(outer[c] * t + inner[c] * (1 - t)) for c in range(3))
        r = radius * t
        draw.ellipse([cx - r, cy - r, cx + r, cy + r], fill=color + (255,))


def main():
    side = SIZE * SS
    image = Image.new('RGBA', (side, side), (0, 0, 0, 0))
    draw = ImageDraw.Draw(image)

    cx = cy = side / 2
    radius = side / 2 - 1

    # Outline, ring band, disc - each as full circles stacked on top of each other,
    # which saves the ring-area math and looks identical at this size.
    draw.ellipse([cx - radius * R_OUTLINE, cy - radius * R_OUTLINE,
                  cx + radius * R_OUTLINE, cy + radius * R_OUTLINE], fill=OUTLINE + (255,))
    draw.ellipse([cx - radius * R_RING, cy - radius * R_RING,
                  cx + radius * R_RING, cy + radius * R_RING], fill=RING + (255,))
    disc(draw, cx, cy, radius * R_DISC, DISC_INNER, DISC_OUTER)

    # Swirl: fit the arms from the template and place them centered on the disc.
    polys = []
    for arm in ARMS:
        polys.extend(parse_path(arm))

    points = [p for poly in polys for p in poly]
    min_x = min(p[0] for p in points)
    max_x = max(p[0] for p in points)
    min_y = min(p[1] for p in points)
    max_y = max(p[1] for p in points)
    src_cx, src_cy = (min_x + max_x) / 2, (min_y + max_y) / 2
    src_radius = max(math.hypot(p[0] - src_cx, p[1] - src_cy) for p in points)
    scale = radius * R_SWIRL / src_radius

    for poly in polys:
        draw.polygon([(cx + (px - src_cx) * scale, cy + (py - src_cy) * scale)
                      for px, py in poly], fill=SWIRL + (255,))

    image = image.resize((SIZE, SIZE), Image.LANCZOS)
    os.makedirs(os.path.dirname(OUT), exist_ok=True)
    image.save(OUT, 'PNG', optimize=True)
    print('%s  %dx%d  %d bytes' % (OUT, SIZE, SIZE, os.path.getsize(OUT)))


if __name__ == '__main__':
    main()
