#!/usr/bin/env python3
"""
Generates the HotS hero portraits in Battledeck/UI/Images/Heroes/ and the matching
hero table Battledeck/Backend/Entity/HotsHeroCatalog.Generated.cs.

Why one script does both: the portrait and the table entry share the id
(abathur.jpg <-> new("abathur", ...)). Two separate sources would drift apart sooner
or later - exactly like the battletag-to-Windows-user derivation, which already
exists in two places in the project. One run, one source of truth.

Generated:
  UI/Images/Heroes/{id}.jpg              91 portraits, 160x160, JPEG
  Backend/Entity/HotsHeroCatalog.Generated.cs   91 entries, sorted by name

With --catalog-only the portraits are left untouched and only the table is rewritten.
Meant for changes that affect only the table - a full run re-encodes 91 JPEGs and
turns them into 91 changed files with no visible difference.

With --missing-only only the portraits whose file does not exist yet are fetched and
written; the table is rewritten as always. That is the call for a new hero: one new
file instead of 91 changed ones.

The GERMAN names come from tools/hero-names-de.json and not from the network. That
is intentional: Blizzard's hero page loads them via an internal endpoint
(unified-gamesite-api-...k8s.apps.blz.dev) that is not a committed interface and can
disappear at any time. The list only ever changes when a hero is added - the last
time was 2026. If one is added, it shows up here immediately: if an entry is
missing, the run aborts instead of writing an empty name. The German names are
needed when reading the collection, where the game client writes them under each
card.

Why JPEG and not PNG like everything else in the folder: the portraits are
photographs without transparency, and the circular shape only comes into being in
WPF anyway (Ellipse + ImageBrush), not in the asset. PNG would cost roughly five
times as much here for the same visible content.

Why 160 pixels: the largest display is the circle in the picker at 64 DIP.
160 covers 250% Windows scaling; going beyond that only costs space.
Whoever changes the circle size in HeroPicker.xaml checks this figure again - a
source that is too small only becomes visible on a scaled-up screen.

Sources (not in the repo, fetched via the MediaWiki API at run time):
  Hero list    https://heroesofthestorm.fandom.com/wiki/Portrait
               section "Hero Portraits", template call {{HeroPortraits|...}} -
               90 names. The one hero the template does not carry is named in
               NOT_IN_TEMPLATE.
  Role         https://heroesofthestorm.fandom.com/wiki/Data:{Hero}
               field "role" - structured, so no parsing of running text.
               The template knows Melee/Ranged; ingame they are spelled out as
               "Melee Assassin" and "Ranged Assassin".
  Portrait     File:{Hero} Hero Portrait.png, 152x152 (six heroes 256x256,
               The Butcher 76x76, Deckard 139x140) - which is why it is cropped
               square and centered instead of being scaled outright.
               A hero without such a file on the wiki gets its portrait from the
               URL in PORTRAIT_ELSEWHERE: Xal'atath from hotsgg.com, 180x210 with a
               hexagonal alpha shape. The corners that come out dark lie outside the
               circle the app draws.

The id strips special characters: Lucio -> lucio, Anub'arak -> anubarak,
Lt. Morales -> lt-morales, D.Va -> dva. It is at the same time the file name and the
YAML value in data.yaml, so it has to do without accents, apostrophes, and dots.

Dependency: Pillow.  Call:  python tools/build-hero-assets.py [--catalog-only | --missing-only]
"""
import io
import json
import os
import re
import sys
import unicodedata
import urllib.parse
import urllib.request

from PIL import Image

API = 'https://heroesofthestorm.fandom.com/api.php'
UA = {'User-Agent': 'battledeck-asset-builder/1.0 (local build script)'}
BATCH = 25  # titles per API call; the Fandom API accepts up to 50, 25 keeps the URL short

IMAGES = os.path.join('Battledeck', 'UI', 'Images', 'Heroes')
CATALOG = os.path.join('Battledeck', 'Backend', 'Entity', 'HotsHeroCatalog.Generated.cs')
GERMAN_NAMES = os.path.join('tools', 'hero-names-de.json')
SIZE = 160
QUALITY = 88

# Template value -> C# enum name. Anything else is an error, not a normal case: if an
# unknown role shows up, the wiki has changed, and extending the enum is a deliberate
# decision, not a silent fallback.
ROLES = {
    'Tank': 'Tank',
    'Bruiser': 'Bruiser',
    'Melee': 'MeleeAssassin',
    'Ranged': 'RangedAssassin',
    'Healer': 'Healer',
    'Support': 'Support',
}

# Heroes the HeroPortraits template does not list although their Data: page exists.
NOT_IN_TEMPLATE = ["Xal'atath"]

# Heroes without a "Hero Portrait" file on the wiki, and where theirs comes from instead.
PORTRAIT_ELSEWHERE = {
    "Xal'atath": 'https://www.hotsgg.com/heroes/Xal%27atath.png',
}


def api(**params):
    params.setdefault('format', 'json')
    params.setdefault('formatversion', '2')
    url = API + '?' + urllib.parse.urlencode(params)
    with urllib.request.urlopen(urllib.request.Request(url, headers=UA)) as r:
        return json.load(r)


def fetch(url):
    with urllib.request.urlopen(urllib.request.Request(url, headers=UA)) as r:
        return r.read()


def hero_id(name):
    """Id: ASCII, lowercase, without apostrophe and dot. Both file name and YAML value."""
    s = unicodedata.normalize('NFKD', name).encode('ascii', 'ignore').decode()
    s = s.replace("'", '').replace('.', '').lower()
    return re.sub(r'[^a-z0-9]+', '-', s).strip('-')


def hero_names():
    """The names from the template call {{HeroPortraits|...}} on the Portrait page, plus NOT_IN_TEMPLATE."""
    wikitext = api(action='parse', page='Portrait', prop='wikitext')['parse']['wikitext']
    match = re.search(r'\{\{HeroPortraits\|(.*?)\}\}', wikitext, re.S)
    if not match:
        raise SystemExit('Template HeroPortraits not found - has the page layout changed?')
    listed = [n.strip() for n in match.group(1).split('|') if n.strip()]
    return listed + [n for n in NOT_IN_TEMPLATE if n not in listed]


def pages(titles, **extra):
    """Fetch page content/metadata in batches, undo title normalization."""
    out = {}
    for i in range(0, len(titles), BATCH):
        chunk = titles[i:i + BATCH]
        data = api(action='query', titles='|'.join(chunk), **extra)['query']
        back = {n['to']: n['from'] for n in data.get('normalized', [])}
        for page in data['pages']:
            out[back.get(page['title'], page['title'])] = page
    return out


def roles_by_hero(names):
    """Field 'role' from Data:{Hero}. If a page is missing, the list is out of date - abort."""
    got = pages(['Data:' + n for n in names],
                prop='revisions', rvprop='content', rvslots='main')
    roles = {}
    for name in names:
        page = got['Data:' + name]
        if 'missing' in page:
            raise SystemExit(f'Data:{name} does not exist')
        text = page['revisions'][0]['slots']['main']['content']
        found = re.search(r'^\|\s*role\s*=\s*(.*?)\s*$', text, re.M)
        if not found or found.group(1) not in ROLES:
            raise SystemExit(f'{name}: unknown role {found.group(1) if found else "(missing)"}')
        roles[name] = ROLES[found.group(1)]
    return roles


def portrait_urls(names):
    """Download URLs of the portrait files. If one is missing, the run aborts instead of leaving a gap."""
    urls = {n: PORTRAIT_ELSEWHERE[n] for n in names if n in PORTRAIT_ELSEWHERE}
    on_wiki = [n for n in names if n not in urls]
    got = pages([f'File:{n} Hero Portrait.png' for n in on_wiki],
                prop='imageinfo', iiprop='url')
    for name in on_wiki:
        page = got[f'File:{name} Hero Portrait.png']
        info = page.get('imageinfo')
        if not info:
            raise SystemExit(f'Portrait for {name} missing on the wiki')
        urls[name] = info[0]['url']
    return urls


def write_portrait(raw, path):
    """Crop square and centered, resize to SIZE, save as JPEG."""
    im = Image.open(io.BytesIO(raw)).convert('RGB')
    w, h = im.size
    side = min(w, h)
    left, top = (w - side) // 2, (h - side) // 2
    im = im.crop((left, top, left + side, top + side))
    im = im.resize((SIZE, SIZE), Image.LANCZOS)
    im.save(path, 'JPEG', quality=QUALITY, optimize=True, progressive=False)
    return os.path.getsize(path)


def german_names(ids):
    """German display names from the data sheet, checked strictly against the ids.

    No default value and no fallback to the English name: a silent fallback would
    mean the text recognition later searches for a name the German client never
    shows - and the hero silently drops out of the read-out.
    """
    with io.open(GERMAN_NAMES, encoding='utf-8') as f:
        names = json.load(f)
    missing = [i for i in ids if i not in names]
    extra = [k for k in names if k not in ids]
    if missing or extra:
        raise SystemExit(
            f'{GERMAN_NAMES} does not match the hero list: '
            f'missing {missing}, extra {extra}')
    return names


def write_catalog(heroes):
    """The generated half of HotsHeroCatalog. The handwritten half sits next to it."""
    lines = [
        '// <auto-generated>',
        '//     Generated by tools/build-hero-assets.py - do not edit by hand.',
        '//     Source: heroesofthestorm.fandom.com, page "Portrait" (list) and',
        '//     "Data:{Hero}" (role), German names from tools/hero-names-de.json.',
        '//     Regenerate instead of editing here.',
        '// </auto-generated>',
        '',
        'namespace Battledeck.Backend.Entity',
        '{',
        '    public static partial class HotsHeroCatalog',
        '    {',
        '        /// <summary>All heroes, sorted by display name. Order is the display order.</summary>',
        '        public static readonly IReadOnlyList<HotsHero> All = new HotsHero[]',
        '        {',
    ]
    for hero_key, name, german, role in heroes:
        lines.append(
            f'            new("{hero_key}", "{name}", "{german}", HotsHeroRole.{role}),')
    lines += [
        '        };',
        '    }',
        '}',
        '',
    ]
    # BOM like the rest of the project's C# files, so names such as "Lucio" with an
    # accent arrive the same way in every editor.
    with io.open(CATALOG, 'w', encoding='utf-8-sig', newline='\r\n') as f:
        f.write('\n'.join(lines))


def main():
    catalog_only = '--catalog-only' in sys.argv
    missing_only = '--missing-only' in sys.argv
    os.makedirs(IMAGES, exist_ok=True)

    names = hero_names()
    print(f'{len(names)} heroes')

    roles = roles_by_hero(names)

    ids = {}
    for name in names:
        key = hero_id(name)
        if key in ids:
            raise SystemExit(f'Id {key} duplicated: {ids[key]} and {name}')
        ids[key] = name

    german = german_names(list(ids))

    total = 0
    written = []
    if catalog_only:
        print('--catalog-only: portraits stay unchanged')
    else:
        paths = {n: os.path.join(IMAGES, hero_id(n) + '.jpg') for n in names}
        written = sorted(
            (n for n in names if not (missing_only and os.path.exists(paths[n]))),
            key=str.lower)
        if missing_only:
            print(f'--missing-only: {len(names) - len(written)} existing portraits stay unchanged')
        urls = portrait_urls(written) if written else {}
        for name in written:
            total += write_portrait(fetch(urls[name]), paths[name])

    heroes = [(hero_id(n), n, german[hero_id(n)], roles[n]) for n in sorted(names, key=str.lower)]
    write_catalog(heroes)

    per_role = {}
    for _, _, _, role in heroes:
        per_role[role] = per_role.get(role, 0) + 1
    if not catalog_only:
        print(f'{len(written)} portraits written, {total / 1024:.0f} KiB total -> {IMAGES}')
    print('Roles: ' + ', '.join(f'{r} {n}' for r, n in sorted(per_role.items())))
    print(f'Table -> {CATALOG}')


if __name__ == '__main__':
    main()
