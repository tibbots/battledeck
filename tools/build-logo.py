#!/usr/bin/env python3
"""
Generates Battledeck/UI/Images/battledeck.ico - the app's icon: taskbar, .exe, and every place
Windows shows it.

The mark is the "Low-Poly Gem": an 8-facet faceted diamond, fan-triangulated from a centre
point, flat-shaded per facet, with dark edge strokes and a light source fixed upper-left (the
top-left facets are the brightest, the bottom-right ones the darkest). It was chosen after
several design rounds mocked up as SVG in a separate review artifact - not part of this repo,
so the geometry and colors below are the only record of it and must not drift from what was
approved.

  Facets   8 triangles in a 200x200 coordinate space, all sharing the centre vertex (100,95).
           Points and fill colors are listed next to FACETS below - do not eyeball them off a
           picture, they are the approved values verbatim.
  Edges    dark stroke #081B2E around every facet, width scaled to a 1.5/200 ratio of whatever
           canvas size is rendered - thin at 512, imperceptible at 16.

This script only produces the static, resting-colour .ico. The in-app header carries a second,
animated instance of the same gem - native XAML (Polygon elements with the same points/fills)
plus a Storyboard-driven shimmer, not a bitmap - see MainWindow.xaml's row-0 corner StackPanel
and MainWindow.xaml.cs's StartLogoShimmer. Whoever changes the geometry or colors here changes
them there too; the two are meant to look like the same gem.

Rendered at 4x supersampling and then downscaled, because Pillow does not anti-alias lines or
polygon edges on its own - the same reason build-free-icon.py and build-penalty-icon.py do it.

Dependency: Pillow.  Call:  python tools/build-logo.py
"""
import os

from PIL import Image, ImageDraw

OUT = os.path.join('Battledeck', 'UI', 'Images', 'battledeck.ico')
SIZE = 512  # working resolution before ICO downsizes it further
SS = 4  # supersampling
ICO_SIZES = [(16, 16), (32, 32), (48, 48), (256, 256)]

# The coordinate space the approved geometry below is measured in - 200x200, top-left origin.
WORKING = 200

# The 8 facets, each a (points, fill) pair. Verbatim from the approved review artifact - do not
# round or adjust these to "look nicer"; a shape change belongs in that artifact first.
FACETS = [
    ([(100, 18), (150, 50), (100, 95)], '#7FD4FF'),
    ([(100, 18), (100, 95), (58, 55)], '#BFEFFF'),
    ([(58, 55), (100, 95), (35, 105)], '#4FC3F0'),
    ([(150, 50), (168, 100), (100, 95)], '#35A8E0'),
    ([(35, 105), (100, 95), (65, 150)], '#2B86C4'),
    ([(168, 100), (140, 155), (100, 95)], '#1F6BA3'),
    ([(65, 150), (100, 95), (102, 185)], '#164E7D'),
    ([(140, 155), (102, 185), (100, 95)], '#0F3A5E'),
]

EDGE_COLOR = '#081B2E'
# Proportion is the point, not the absolute number - 1.5 px of stroke at the 200-unit design
# size, carried over to whatever canvas this script actually renders at.
EDGE_RATIO = 1.5 / WORKING


def hex_to_rgb(value):
    value = value.lstrip('#')
    return tuple(int(value[i:i + 2], 16) for i in (0, 2, 4))


def main():
    side = SIZE * SS
    scale = side / WORKING
    image = Image.new('RGBA', (side, side), (0, 0, 0, 0))
    draw = ImageDraw.Draw(image)

    def scaled(points):
        return [(x * scale, y * scale) for x, y in points]

    # Fills first, full triangles - facets meet exactly at their shared edges, so there is no
    # gap to paper over between them.
    for points, fill in FACETS:
        draw.polygon(scaled(points), fill=hex_to_rgb(fill) + (255,))

    # Edges on top, one closed polyline per facet rather than per edge, so the two edges
    # meeting at a facet's own corner get a rounded joint instead of a visible notch. Shared
    # edges between neighbouring facets are drawn twice, once per facet - same color and
    # width, so the overlap is invisible.
    stroke = max(1, round(EDGE_RATIO * SIZE * SS))
    edge = hex_to_rgb(EDGE_COLOR) + (255,)
    for points, _ in FACETS:
        pts = scaled(points)
        draw.line(pts + [pts[0]], fill=edge, width=stroke, joint='curve')

    image = image.resize((SIZE, SIZE), Image.LANCZOS)
    os.makedirs(os.path.dirname(OUT), exist_ok=True)
    image.save(OUT, format='ICO', sizes=ICO_SIZES)
    print('%s  %d bytes' % (OUT, os.path.getsize(OUT)))


if __name__ == '__main__':
    main()
