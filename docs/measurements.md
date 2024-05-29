# Measured values not yet in any code

From the calibration session of **21.08.2026**. Every value is backed by measurements at
**two** resolutions — 3440×1440 (fullscreen) and 3251×1361 (windowed mode, scale 0.9451) —
and is therefore ready to enter per the rule in [calibration.md](calibration.md). Whoever moves one
of these into `screen-map.yaml` strikes it from this list.

All figures in points of the reference size 3440×1440.

## Login screen

| Point | Anchor | Offset | measured at 1361 | predicted |
|---|---|---|---|---|
| "Optionen" button | bottomRight | x −176, y −179 | (3084.3 / 1190.3) | (3085 / 1192) |

> **The region offsets have been in the code since 21.08.2026** — `americasAbove` 137,
> `europeAbove` 99, `asiaAbove` 61, row spacing 38 — and are therefore struck from this
> list.

> **The anchors of the gear menu have been in the code since 21.08.2026** (the `menu`
> section in screen-map.yaml) and are struck from this list. The measured values are
> recorded in the comment of the calibration file.

## Placement games

On the GEWERTET screen it reads **"Spiele 0 von 3"** together with "0 Siege" — the count
that the profile overlay does not have (there it shows only the word `Platzierung`).
Whoever wants the counter instead of the bool gets it there.

> **The ARAM step has been built since 21.08.2026** — and without an anchor: `PlayScreen`
> searches for the word in the tab row, because its entries sit by text width and shift
> with the language. The measured values (German 709, English 579 at y = 125) are kept
> only here now, as evidence for why an anchor would be wrong.

## A hand-entered value that has been disproven

For `MUGGLE#21197`, `data.yaml` has `hotsPenaltyGames: 1`; the game reports **3**. The
value will correct itself once the read-out runs.
