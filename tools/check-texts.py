#!/usr/bin/env python3
"""Checks the text files against the code - and against each other.

    python tools/check-texts.py

WHY THIS SCRIPT EXISTS

A missing key does NOT show up when building. XAML does not know the keys, C# only
sees a string, and Strings silently falls back to English at run time. So the bug
only shows up to whoever uses the app in the affected language - and even then only
if they notice that a line reads in English.

Four things are checked:

  1. Every key used in the code is in en.yaml.
  2. Every key from en.yaml is also used (dead entries).
  3. Every translation has the same keys as en.yaml.
  4. Every translation has the SAME PLACEHOLDERS per key as the English one.

Point 4 is the most important. A {2} in a text that is only given two values throws
a FormatException at run time - Strings catches it and falls back to English, but
the text is then permanently broken in that language.

DYNAMIC KEYS

Four groups are assembled in the code instead of being spelled out - rank.*, role.*,
region.* and speed.* from the respective enum name, plus settings.speedHint.*. They
cannot be found by searching and are therefore hardcoded below as DYNAMIC. Whoever
adds an enum value there adds it here too.
"""
import pathlib
import re
import sys

ROOT = pathlib.Path(__file__).resolve().parent.parent
TEXTS = ROOT / "Smurftown" / "Backend" / "Texts"
SOURCE = ROOT / "Smurftown"

# Keys the code assembles at run time. See the module comment.
DYNAMIC = {
    "rank.%s" % t for t in
    ["none", "bronze", "silver", "gold", "platinum", "diamond", "master", "grandmaster"]
} | {
    "role.%s" % r for r in
    ["tank", "bruiser", "meleeassassin", "rangedassassin", "healer", "support"]
} | {
    "region.%s" % r for r in ["europe", "americas", "asia"]
} | {
    "speed.%s" % s for s in ["slow", "normal", "fast"]
} | {
    "settings.speedHint.%s" % s for s in ["slow", "normal", "fast"]
}

USED = [
    # XAML: {loc:Str key}
    re.compile(r"\{loc:Str\s+([^}\s]+)\s*\}"),
    # C#: EVERY literal that follows the key schema. Deliberately broad and not tied
    # to Strings.Current[...] - half of the calls sit in a conditional expression
    # (Strings.Current[x ? "row.restore" : "row.archive"]) or are spread across
    # several lines, and a pattern aimed at that would miss them.
    #
    # The price is false positives on other dotted literals. They only ever hit the
    # dead-entries list, never the question "is a used key missing" - and that is the
    # one that matters.
    re.compile(r'"([a-z][A-Za-z]*(?:\.[a-zA-Z][A-Za-z0-9]*)+)"'),
]

# Follows the key schema but is not one.
NOT_A_KEY = re.compile(r"\.(yaml|yml|exe|png|jpg|cs|xaml|log|txt|dll|json|bak)$")

PLACEHOLDER = re.compile(r"\{(\d+)\}")


def read_yaml(path):
    """A deliberately small parser: flat keys, values raw or quoted.

    No yaml module, so the script runs without an install - the files are flat and
    need nothing more.
    """
    out = {}
    for number, raw in enumerate(path.read_text(encoding="utf-8").splitlines(), 1):
        line = raw.rstrip()
        if not line.strip() or line.lstrip().startswith("#"):
            continue
        if ":" not in line:
            print("  %s:%d without a colon: %s" % (path.name, number, line))
            continue
        key, value = line.split(":", 1)
        key, value = key.strip(), value.strip()
        if value.startswith('"'):
            end = value.rfind('"')
            value = value[1:end] if end > 0 else value[1:]
            value = value.replace("\\n", "\n").replace("\\xB7", "·")
        else:
            value = re.sub(r"\s+#.*$", "", value)
        out[key] = value
    return out


def used_keys():
    found = set()
    for pattern in ("**/*.cs", "**/*.xaml"):
        for f in SOURCE.glob(pattern):
            if "obj" in f.parts or "bin" in f.parts:
                continue
            text = f.read_text(encoding="utf-8", errors="replace")
            for rx in USED:
                found.update(rx.findall(text))
    return {k for k in found if not NOT_A_KEY.search(k)} | DYNAMIC


def stray_literals():
    """Visible text in XAML that does NOT go through loc:Str.

    Two forms, and both actually slipped through on 22.08.2026: an attribute
    (Text=, Content=, ToolTip=) with a literal instead of a binding - and text as
    ELEMENT CONTENT (<TextBlock>FILTER</TextBlock>), which a pattern aimed at
    attributes does not see at all. Both were only found by looking at the running
    application, in a language where it stood out.
    """
    attr = re.compile(r'\b(Text|Content|ToolTip|Header)\s*=\s*"([^"{][^"]*)"')
    inner = re.compile(r">\s*([A-Za-z][A-Za-z0-9 '\-.,+/]{2,})\s*<", re.S)
    # No translatable text: plain characters (&#x2715; is the close cross), product
    # names, and the region abbreviations - those read the same in all four
    # languages.
    fine = re.compile(r"^([#_x\[\]\s;&]|&#x[0-9A-Fa-f]+;|\d)*$"
                      r"|^(Smurftown|SMURFTOWN|HEROES OF THE STORM|Heroes of the Storm|Battle\.net"
                      r"|EU|AM|AS|OK|Height)$")

    out = []
    for f in list(SOURCE.glob("**/View/*.xaml")) + [SOURCE / "MainWindow.xaml"]:
        if not f.exists():
            continue
        text = re.sub(r"<!--.*?-->", "", f.read_text(encoding="utf-8"), flags=re.S)
        for number, line in enumerate(text.splitlines(), 1):
            for m in attr.finditer(line):
                if not fine.match(m.group(2).strip()):
                    out.append((f.name, number, m.group(2)))
            for m in inner.finditer(line):
                value = m.group(1).strip()
                if not fine.match(value) and re.search(r"[A-Za-z]{3}", value):
                    out.append((f.name, number, value))
    return out


def main():
    english = read_yaml(TEXTS / "en.yaml")
    used = used_keys()
    problems = 0

    stray = stray_literals()
    if stray:
        problems += len(stray)
        print("fixed text in XAML, not routed through loc:Str (%d):" % len(stray))
        for name, number, value in stray:
            print("    %s:%d  %s" % (name, number, value[:70]))
        print()

    missing = sorted(used - set(english))
    if missing:
        problems += len(missing)
        print("MISSING from en.yaml but used (%d):" % len(missing))
        for key in missing:
            print("   ", key)

    unused = sorted(set(english) - used)
    if unused:
        print("\nin en.yaml, used nowhere (%d):" % len(unused))
        for key in unused:
            print("   ", key)

    for tag in ["de", "fr", "es"]:
        path = TEXTS / ("%s.yaml" % tag)
        if not path.exists():
            print("\n%s.yaml missing" % tag)
            problems += 1
            continue

        other = read_yaml(path)
        gaps = sorted(set(english) - set(other))
        extra = sorted(set(other) - set(english))
        bad = []

        for key, value in other.items():
            if key not in english:
                continue
            want = set(PLACEHOLDER.findall(english[key]))
            have = set(PLACEHOLDER.findall(value))
            if want != have:
                bad.append((key, sorted(want), sorted(have)))

        print("\n%s.yaml: %d texts" % (tag, len(other)), end="")
        if not gaps and not extra and not bad:
            print("  - complete")
            continue

        print()
        problems += len(gaps) + len(extra) + len(bad)
        for key in gaps:
            print("   missing:      %s" % key)
        for key in extra:
            print("   extra:        %s" % key)
        for key, want, have in bad:
            print("   placeholders: %s  english %s, here %s" % (key, want, have))

    print("\n%s" % ("all good" if problems == 0 else "%d issues" % problems))
    return 1 if problems else 0


if __name__ == "__main__":
    sys.exit(main())
