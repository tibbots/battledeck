import io, re

SRC = 'Battledeck/Backend/Entity/HotsHeroCatalog.Generated.cs'
ids = re.findall(r'new\("([a-z0-9-]+)"', io.open(SRC, encoding='utf-8-sig').read())
assert len(ids) == 90, len(ids)


def heroes(n, seed, must=(), never=()):
    """Deterministic selection spread across the list - no random, so the file comes
    out identical on every run."""
    out = []
    i = 0
    while len(out) < n:
        h = ids[(seed + i * 7) % 90]
        i += 1
        if h not in out and h not in never:
            out.append(h)
    for m in must:
        if m not in out:
            out[-1] = m
    return sorted(set(out))


def region(tier, div, hero_list, gold, shards, gems, level, chests, read,
           penalty=0, placements=False, points=None, points_max=None):
    p = '      '
    lines = ['%stier: %s' % (p, tier),
             '%sdivision: %d' % (p, div),
             '%spenaltyGames: %d' % (p, penalty),
             '%splacementsPending: %s' % (p, str(placements).lower())]
    if not hero_list:
        lines.append('%sheroes: []' % p)
    else:
        lines.append('%sheroes:' % p)
        lines += ['%s- %s' % (p, h) for h in hero_list]
    # Both or neither. A single one of the two is a state the app treats as unread, and
    # demo data whose ring nobody can explain is worse than one without a ring.
    if (points is None) != (points_max is None):
        raise SystemExit('rank points need both numbers: %s %d' % (tier, div))
    if points is not None:
        lines.append('%srankPoints: %d' % (p, points))
        lines.append('%srankPointsMax: %d' % (p, points_max))
    for key, val in (('gold', gold), ('shards', shards), ('gems', gems),
                     ('accountLevel', level), ('lootChests', chests)):
        lines.append('%s%s: %s' % (p, key, val))
    lines.append('%sreadAt: %s' % (p, read))
    return '\n'.join(lines)


ACC = []


def account(name, disc, email, pw, games, regions, notes='', inactive=False, hots_by=None):
    ow, ho, wo, di = games
    b = ['- name: %s' % (name if name else '""'),
         '  discriminator: "%s"' % disc,
         '  email: %s' % email,
         '  password: %s' % pw,
         '  notes: "%s"' % notes,
         '  latestInteractionAt: 2026-08-21T19:40:00',
         '  regionsByGame:']
    # The regions hang off the game, not off the account - every ticked game gets the
    # regions of the account, in the display order. An account without a single entry
    # here has no row at all, so `games` must tick at least one.
    assert any(games), 'account %s ticks no game' % name
    for game, ticked in (('hots', ho), ('overwatch', ow), ('wow', wo), ('diablo', di)):
        if not ticked:
            continue
        b.append('    %s:' % game)
        b += ['    - %s' % r for r in regions]
    b.append('  inactive: %s' % str(inactive).lower())
    if hots_by:
        b.append('  hotsByRegion:')
        for r, body in hots_by:
            b.append('    %s:' % r)
            b.append(body)
    else:
        b.append('  hotsByRegion: {}')
    ACC.append('\n'.join(b))


# 1 - two regions on ONE account: the image for regions.png
account('SMURFKING', '2481', 'smurfking@example.com', 'demo-pass-01',
        (True, True, False, False), ['Europe', 'Americas'],
        notes='Main account. EU ranked, AM for fun.',
        hots_by=[('Europe', region('Platinum', 2, heroes(47, 3, must=['tracer']),
                                   12480, 1350, 800, 214, 6, '2026-08-21T18:22:00',
                                   points=640, points_max=1000)),
                 ('Americas', region('Silver', 4, heroes(12, 40),
                                     3120, 260, 0, 63, 0, '2026-08-20T21:05:00',
                                     points=95, points_max=1000))])

# 2 - open placement matches: dimmed medal, nearly complete collection
account('NEXUSNOMAD', '1177', 'nexusnomad@example.com', 'demo-pass-02',
        (False, True, True, False), ['Europe'],
        hots_by=[('Europe', region('Diamond', 3, heroes(89, 11),
                                   51240, 9600, 2400, 487, 23, '2026-08-21T17:58:00',
                                   placements=True))])

# 3 - leaver-penalty status. Does NOT own tracer and still hits the hero filter,
#     because tracer is free this period.
account('GHOSTLANE', '4092', 'ghostlane@example.com', 'demo-pass-03',
        (False, True, False, False), ['Europe'],
        hots_by=[('Europe', region('Gold', 5, heroes(31, 55, never=['tracer']),
                                   890, 40, 0, 122, 1, '2026-08-19T09:14:00',
                                   penalty=2, points=310, points_max=1000))])

# 4 - never read: no battletag, email as substitute name, dashes everywhere
account('', '', 'newcomer@example.com', 'demo-pass-04',
        (False, True, False, False), ['Europe'])

# 5 - Asia: the third region abbreviation
account('ARAMANDA', '3311', 'aramanda@example.com', 'demo-pass-05',
        (False, True, False, True), ['Asia'],
        hots_by=[('Asia', region('Bronze', 1, heroes(8, 70),
                                 450, 0, 0, 24, 0, '2026-08-18T12:30:00',
                                 points=980, points_max=1000))])

# 6 - archived: gives the archive toggle something to show
account('RETIREDALT', '7754', 'retiredalt@example.com', 'demo-pass-06',
        (False, True, False, False), ['Europe'], inactive=True,
        notes='Season 2 alt, not used any more.',
        hots_by=[('Europe', region('Gold', 2, heroes(22, 25),
                                   2010, 120, 0, 88, 0, '2026-06-02T14:00:00',
                                   points=0, points_max=1000))])

# 7 - the top of what this list shows: Diamond 1, the fullest collection and wallet.
#     THE DEMO DATA STOPS AT DIAMOND, by decision - Master and GrandMaster are not
#     given to any account here. The price is that division 0, which only those two
#     carry, is now covered by nobody; the rank grid still offers both tiers, it just
#     has no account selecting them. Whoever needs that case back adds an account for
#     it rather than promoting this one.
account('LANEBULLY', '5006', 'lanebully@example.com', 'demo-pass-07',
        (False, True, False, False), ['Europe'],
        hots_by=[('Europe', region('Diamond', 1, heroes(74, 17),
                                   28900, 4100, 1150, 366, 11, '2026-08-21T16:40:00',
                                   points=1000, points_max=1000))])

# 8 - second leaver-penalty case, so the triangle does not look like a one-off
account('SILENTPUSH', '9120', 'silentpush@example.com', 'demo-pass-08',
        (True, True, False, False), ['Europe'],
        hots_by=[('Europe', region('Silver', 2, heroes(19, 61),
                                   640, 80, 0, 57, 2, '2026-08-17T20:11:00',
                                   penalty=1, points=455, points_max=1000))])

# 9 - read, but WITHOUT points: the ring stays empty because the tooltip was never
#     taken, not because the account is at the start of its division. TOWERDIVE holds
#     that case, RETIREDALT above holds the other one - the two look the same on the
#     medal and differ only in the tooltip, so the list has to carry both.
# 9 - Americas checked but never read: the AM row shows dashes
account('TOWERDIVE', '6633', 'towerdive@example.com', 'demo-pass-09',
        (False, True, False, False), ['Europe', 'Americas'],
        hots_by=[('Europe', region('Gold', 3, heroes(38, 33),
                                   7420, 900, 300, 178, 4, '2026-08-21T15:02:00'))])

# 10 - without the HotS checkbox: invisible under the HotS filter, present under Overwatch
account('PAYLOADONLY', '3078', 'payloadonly@example.com', 'demo-pass-10',
        (True, False, False, False), ['Europe'])

# 11 - two regions AND penalty games AND an open placement, together on the SAME region:
#      the account dialog needs all three traits in one image to show the region bar,
#      the warning triangle and the dimmed medal at once.
account('MARBLEFOX', '8834', 'marblefox@example.com', 'demo-pass-11',
        (False, True, False, False), ['Europe', 'Americas'],
        notes='Ranked EU, plays AM casually.',
        hots_by=[('Europe', region('Gold', 3, heroes(28, 45),
                                   4260, 310, 0, 96, 3, '2026-08-21T14:10:00',
                                   penalty=1, placements=True)),
                 ('Americas', region('Bronze', 4, heroes(9, 20),
                                     210, 0, 0, 31, 0, '2026-08-16T10:20:00',
                                     points=140, points_max=1000))])

# 12 - Americas ticked, never read: the AM filter needs a dashed row of its own, the
#      way the unnamed account (3 above) gives the EU filter one.
account('HALFMOONBAY', '2290', 'halfmoonbay@example.com', 'demo-pass-12',
        (False, True, False, False), ['Americas'])

# 13 - a second archived account, so the archive toggle does not look like a
#      one-account fixture.
account('GLASSFERN', '6647', 'glassfern@example.com', 'demo-pass-13',
        (False, True, False, False), ['Europe'], inactive=True,
        notes='Dead account, kept for the badge.',
        hots_by=[('Europe', region('Bronze', 3, heroes(15, 50),
                                   180, 0, 0, 19, 0, '2026-05-14T08:00:00',
                                   points=60, points_max=1000))])

HEADER = """# Demo accounts for the README screenshots - NO real credentials.
#
# WHY THIS FILE EXISTS: tibbots/battledeck is a public repo, and the UI shows the
# battletag and email address in plain text. An image of the real list publishes
# them permanently - GitHub keeps every version of an image in its history, even one
# replaced later. That is why the shots are taken against this file and not against
# the actual data.yaml.
#
# All addresses sit under example.com (RFC 2606, reserved for exactly this purpose),
# all passwords are obvious placeholders, all battletags made up.
#
# HOW IT IS USED - in order, and step 1 is the important one:
#
#   1. set your own list aside:  ~/.smurftown/data.yaml  ->  data.yaml.real
#   2. copy this file there as  data.yaml
#   3. start Battledeck and go through the capture list - it is in
#      .claude/skills/readme-screenshots/. Captured with tools/capture-window.ps1.
#      Then quit.
#   4. rename data.yaml.real back
#
# IF BATTLEDECK DOES NOT START AFTER STEP 2, it is this file's fault and not the
# app's - the reason then shows up in ~/.smurftown/logs/smurftown.log. Your own list is
# unaffected by this: at that point it is named data.yaml.real and is not read at
# all. Step 4 restores the starting state.
#
# EVERY ENTRY STANDS FOR A TRAIT that would otherwise not show up on any image:
#
#   SMURFKING    two regions on one account - two rows, different states
#   NEXUSNOMAD   open placement matches (dimmed medal), almost full collection
#   GHOSTLANE    leaver-penalty status (warning triangle). Does NOT own Tracer and
#                still hits the hero filter on Tracer - the hero is free this period
#   (no name)    never read: no battletag, the email names the row, "-" everywhere
#   ARAMANDA     Asia - the third region abbreviation
#   RETIREDALT   archived - the archive toggle would otherwise have nothing to show
#   LANEBULLY    the highest rank on this list - Diamond 1, and the fullest wallet.
#                No account here is Master or GrandMaster: the demo data stops at
#                Diamond, so division 0 is covered by nobody
#   SILENTPUSH   second leaver-penalty case, so the triangle does not look like a
#                one-off
#   TOWERDIVE    Americas checked but never read - "being played" is not
#                "has something in it"
#   PAYLOADONLY  without the HotS checkbox: invisible under the HotS filter, present
#                under Overwatch
#   MARBLEFOX    two regions, penalty games AND an open placement on the same region -
#                the account dialog needs all three at once
#   HALFMOONBAY  Americas ticked, never read - the AM filter's own dashed row
#   GLASSFERN    archived, second such account, on Europe
#
# THE FREE ROTATION DOES NOT COME FROM THIS FILE, but from the embedded calendar
# (Backend/Entity/rotation-calendar.yaml) and follows the date of the capture. On
# 22.08.2026 Tracer is free - GHOSTLANE is tuned to that. Whoever recaptures later and
# wants to show the same effect picks a hero from the period running at that time in
# the hero filter.
#
# THE SHAPE IS THE ONE THE APP WRITES: a schemaVersion, then the accounts under a key.
# It was a bare sequence until 1.3.0, and the app still reads that - but demo data whose
# layout no application writes any more is a fixture that tests the past.
#
# GENERATED BY tools/gen-demo-data.py - change things there, not here.
"""

# Unindented under the key, because that is what YamlDotNet emits and this file has to be
# indistinguishable from one the app wrote.
BODY = 'schemaVersion: 1\naccounts:\n' + '\n'.join(ACC) + '\n'

io.open('tools/demo-data.yaml', 'w', encoding='utf-8', newline='\n').write(HEADER + '\n' + BODY)
print('written, accounts:', len(ACC))
