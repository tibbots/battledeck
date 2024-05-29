---
name: readme-translations
version: 1
description: Keep README.de.md, README.fr.md and README.es.md in sync with the English README.md — the language bar, the terminology taken from the app's own shipped UI strings, and the checks that catch drift. Triggers on README translation, translate the README, update READMEs, language bar, README.de, README.fr, README.es, "README uebersetzen".
---

# Keeping the four READMEs in sync

The README is the public landing page and exists in four languages — the same four the application
itself speaks.

| File | Language | Role |
|---|---|---|
| `README.md` | English | **the source** |
| `README.de.md` | German | translation |
| `README.fr.md` | French | translation |
| `README.es.md` | Spanish | translation |

**English is the source.** A change goes there first, the three translations follow. Never edit a
translation to say something the English one does not.

## The language bar

Line 3 of every file, current language **bold and unlinked**, the others linked:

```
README.md      **English** · [Deutsch](README.de.md) · [Français](README.fr.md) · [Español](README.es.md)
README.de.md   [English](README.md) · **Deutsch** · [Français](README.fr.md) · [Español](README.es.md)
README.fr.md   [English](README.md) · [Deutsch](README.de.md) · **Français** · [Español](README.es.md)
README.es.md   [English](README.md) · [Deutsch](README.de.md) · [Français](README.fr.md) · **Español**
```

Separator is `·` (U+00B7). Files are UTF-8.

## Terminology — the part that matters

**The app's own shipped UI strings are the authority**, not a translator's judgement. A reader
compares the README against what they see on screen, so the README has to use the same words.

| Language | Read this first |
|---|---|
| German | `Smurftown/Backend/Texts/de.yaml` |
| French | `Smurftown/Backend/Texts/fr.yaml` |
| Spanish | `Smurftown/Backend/Texts/es.yaml` |
| all | `Smurftown/Backend/Texts/en.yaml` — the English original of the same strings |

Terms to look up rather than invent: free rotation, loot chests, shards, gems, penalty games,
placement, rank/tier/division, archive, region, collection, settings, client language, input speed.

**For in-game screens and tabs**, the authority is `Smurftown/Backend/Automation/GameVocabulary.cs`
— those words are measured against the real client (e.g. `COFFRES` for loot in French,
`Ligue Storm` for the ranked league, where the league name stays English).

**Spanish ships one UI translation** although the app distinguishes two Spanish *game* clients
(Spain / Latin America). That split hangs on hero names only. Follow `es.yaml` and write neutral
Spanish.

## Rules

1. **Translate** prose, headings, table cells and image alt texts.
2. **Do not translate**: image paths (`docs/images/*.png`), file paths
   (`C:\Users\YOUR_USER\.smurftown`, `data.yaml`, `settings.yaml`), product names (Smurftown,
   Battle.net, Heroes of the Storm, Windows, SmartScreen, .NET), version numbers, URLs, code spans.
3. **Preserve every factual and privacy claim exactly.** The README says where the data lives,
   that passwords are stored in plain text, and that the application makes **one** request a day
   — the update check against `api.github.com`, which cannot be switched off. Do not soften,
   strengthen or drop one of those in any language. In particular do not write "no server" or
   "no network traffic" in any of the four: it was true until the update check shipped and is the
   single claim most likely to be restored by somebody translating from memory.
4. **Keep the markdown structure identical** — same headings in the same order, same tables, same
   blockquotes, same image positions.
5. **Typography per language**: German umlauts as real characters (ä ö ü ß), French accents plus
   the non-breaking space before `: ? ! »` in prose (never inside code spans), Spanish accents plus
   the opening ¿ and ¡ — the FAQ headings are questions and need them.
6. **Third-party UI literals stay in the language of that UI**, or in English where no verified
   wording exists: Blizzard's in-game menu path and the Windows SmartScreen strings are the two
   cases. Decide once and apply it the same way in all three translations.

## Checks after every change

```bash
# same headings, same order
for f in README.md README.de.md README.fr.md README.es.md; do
  echo "$f: $(grep -cE '^#{1,3} ' $f) headings"
done

# identical image references
for f in README.de.md README.fr.md README.es.md; do
  diff <(grep -oE '\(docs/images/[^)]*\)' README.md) <(grep -oE '\(docs/images/[^)]*\)' $f) \
    && echo "$f images OK"
done

# valid UTF-8
for f in README*.md; do
  python -c "import io,sys; io.open(sys.argv[1],encoding='utf-8').read(); print(sys.argv[1],'UTF-8 OK')" $f
done

# the language bar is present and points at existing files
grep -n 'README.de.md\|README.fr.md\|README.es.md' README*.md | head
```

Also confirm by eye: every markdown table has matching pipe counts per row, and the Spanish FAQ
headings open with ¿.

## Known drift to watch

The client-language table in the README lists which game client languages the reading supports.
`GameVocabulary` ships five variants, two of them with values marked as not yet measured. **If the
table and the code disagree, fix the table in all four files** — and say "untested" rather than
"unsupported" if that is what is true. A Spanish reader who sees their own language marked
unsupported while the app ships a Spanish dictionary will report it as a bug.
