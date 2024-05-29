#!/usr/bin/env python3
"""
Generates Smurftown/UI/Images/penalty.png - the leaver-penalty warning triangle.

Why drawn instead of cut out: the template shows the icon at roughly 26px edge
length with clear JPEG artifacts (screenshot of the HotS message
"You have recently disconnected from a game", https://i.imgur.com/qXW5b9Z.jpg).
Scaled up, that looks like an error in the app. The shape is simple enough to
rebuild from geometry - the colors are measured from the template: frame and lines
~(202,58,58) to (210,17,20), interior ~(68,21,29).

Rendered at 4x supersampling and then downscaled, because Pillow does not
anti-alias lines.

Dependency: Pillow.  Call:  python tools/build-penalty-icon.py
"""
import os

from PIL import Image, ImageDraw, ImageFilter

OUT = os.path.join('Smurftown', 'UI', 'Images', 'penalty.png')
SIZE = 128
SS = 4  # supersampling

RED = (214, 42, 42, 255)      # frame, triangle, exclamation mark
RED_GLOW = (226, 46, 46, 255)  # soft glow toward the outside
FILL = (46, 14, 17, 255)       # interior, almost black with a red tint

FRAME_INSET = 7    # distance of frame to edge, leaves room for the glow
FRAME_RADIUS = 16
FRAME_WIDTH = 6

TRI_TOP = 36       # top edge of the triangle
TRI_APEX = 101     # apex, points downward
TRI_HALF = 37      # half width of the top edge
TRI_WIDTH = 6

BAR_TOP, BAR_BOTTOM = 47, 74   # exclamation-mark bar, tapered downward
BAR_HALF_TOP, BAR_HALF_BOTTOM = 5.5, 2.4
DOT_Y, DOT_R = 84, 4.0


def draw_icon(scale):
    """Draws the icon at the given magnification."""
    s = SIZE * scale
    im = Image.new('RGBA', (s, s), (0, 0, 0, 0))
    d = ImageDraw.Draw(im)

    def p(v):
        return v * scale

    frame = (p(FRAME_INSET), p(FRAME_INSET), s - p(FRAME_INSET) - 1, s - p(FRAME_INSET) - 1)
    d.rounded_rectangle(frame, radius=p(FRAME_RADIUS), fill=FILL,
                        outline=RED, width=int(p(FRAME_WIDTH)))

    cx = s / 2
    corners = [(cx - p(TRI_HALF), p(TRI_TOP)),
               (cx + p(TRI_HALF), p(TRI_TOP)),
               (cx, p(TRI_APEX))]
    d.line(corners + [corners[0]], fill=RED, width=int(p(TRI_WIDTH)), joint='curve')

    d.polygon([(cx - p(BAR_HALF_TOP), p(BAR_TOP)),
               (cx + p(BAR_HALF_TOP), p(BAR_TOP)),
               (cx + p(BAR_HALF_BOTTOM), p(BAR_BOTTOM)),
               (cx - p(BAR_HALF_BOTTOM), p(BAR_BOTTOM))], fill=RED)

    d.ellipse((cx - p(DOT_R), p(DOT_Y) - p(DOT_R),
               cx + p(DOT_R), p(DOT_Y) + p(DOT_R)), fill=RED)
    return im


def with_glow(icon):
    """Places a soft red glow under the icon - the template glows as well."""
    glow = Image.new('RGBA', icon.size, RED_GLOW[:3] + (0,))
    glow.putalpha(icon.split()[3].filter(ImageFilter.GaussianBlur(icon.width / 32)))
    return Image.alpha_composite(glow, icon)


def main():
    icon = with_glow(draw_icon(SS)).resize((SIZE, SIZE), Image.LANCZOS)
    os.makedirs(os.path.dirname(OUT), exist_ok=True)
    icon.save(OUT)
    print(f'{OUT}  {icon.size[0]}x{icon.size[1]}  {os.path.getsize(OUT) / 1024:.1f} KB')


if __name__ == '__main__':
    main()
