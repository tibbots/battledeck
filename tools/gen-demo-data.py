import io, re

SRC = 'Smurftown/Backend/Entity/HotsHeroCatalog.Generated.cs'
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
           penalty=0, placements=False):
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
         '  overwatch: %s' % str(ow).lower(),
         '  hots: %s' % str(ho).lower(),
         '  wow: %s' % str(wo).lower(),
         '  diablo: %s' % str(di).lower(),
         '  notes: "%s"' % notes,
         '  latestInteractionAt: 2026-08-21T19:40:00',
         '  regions:']
    b += ['  - %s' % r for r in regions]
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
                                   12480, 1350, 800, 214, 6, '2026-08-21T18:22:00')),
                 ('Americas', region('Silver', 4, heroes(12, 40),
                                     3120, 260, 0, 63, 0, '2026-08-20T21:05:00'))])

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
                                   penalty=2))])

# 4 - never read: no battletag, email as substitute name, dashes everywhere
account('', '', 'newcomer@example.com', 'demo-pass-04',
        (False, True, False, False), ['Europe'])

# 5 - Asia: the third region abbreviation
account('ARAMANDA', '3311', 'aramanda@example.com', 'demo-pass-05',
        (False, True, False, True), ['Asia'],
        hots_by=[('Asia', region('Bronze', 1, heroes(8, 70),
                                 450, 0, 0, 24, 0, '2026-08-18T12:30:00'))])

# 6 - archived: gives the archive toggle something to show
account('RETIREDALT', '7754', 'retiredalt@example.com', 'demo-pass-06',
        (False, True, False, False), ['Europe'], inactive=True,
        notes='Season 2 alt, not used any more.',
        hots_by=[('Europe', region('Gold', 2, heroes(22, 25),
                                   2010, 120, 0, 88, 0, '2026-06-02T14:00:00'))])

# 7 - the top of what this list shows: Diamond 1, the fullest collection and wallet.
#     THE DEMO DATA STOPS AT DIAMOND, by decision - Master and GrandMaster are not
#     given to any account here. The price is that division 0, which only those two
#     carry, is now covered by nobody; the rank grid still offers both tiers, it just
#     has no account selecting them. Whoever needs that case back adds an account for
#     it rather than promoting this one.
account('LANEBULLY', '5006', 'lanebully@example.com', 'demo-pass-07',
        (False, True, False, False), ['Europe'],
        hots_by=[('Europe', region('Diamond', 1, heroes(74, 17),
                                   28900, 4100, 1150, 366, 11, '2026-08-21T16:40:00'))])

# 8 - second leaver-penalty case, so the triangle does not look like a one-off
account('SILENTPUSH', '9120', 'silentpush@example.com', 'demo-pass-08',
        (True, True, False, False), ['Europe'],
        hots_by=[('Europe', region('Silver', 2, heroes(19, 61),
                                   640, 80, 0, 57, 2, '2026-08-17T20:11:00',
                                   penalty=1))])

# 9 - Americas checked but never read: the AM row shows dashes
account('TOWERDIVE', '6633', 'towerdive@example.com', 'demo-pass-09',
        (False, True, False, False), ['Europe', 'Americas'],
        hots_by=[('Europe', region('Gold', 3, heroes(38, 33),
                                   7420, 900, 300, 178, 4, '2026-08-21T15:02:00'))])

# 10 - without the HotS checkbox: invisible under the HotS filter, present under Overwatch
account('PAYLOADONLY', '3078', 'payloadonly@example.com', 'demo-pass-10',
        (True, False, False, False), ['Europe'])

HEADER = """# Demo accounts for the README screenshots - NO real credentials.
#
# WHY THIS FILE EXISTS: tibbots/smurftown is a public repo, and the UI shows the
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
#   3. start Smurftown and go through the capture list - it is in
#      .claude/skills/readme-screenshots/. Captured with tools/capture-window.ps1.
#      Then quit.
#   4. rename data.yaml.real back
#
# IF SMURFTOWN DOES NOT START AFTER STEP 2, it is this file's fault and not the
# app's - the reason then shows up in ~/.smurftown/smurftown.log. Your own list is
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
#
# THE FREE ROTATION DOES NOT COME FROM THIS FILE, but from the embedded calendar
# (Backend/Entity/rotation-calendar.yaml) and follows the date of the capture. On
# 22.08.2026 Tracer is free - GHOSTLANE is tuned to that. Whoever recaptures later and
# wants to show the same effect picks a hero from the period running at that time in
# the hero filter.
#
# GENERATED BY tools/gen-demo-data.py - change things there, not here.
"""

io.open('tools/demo-data.yaml', 'w', encoding='utf-8', newline='\n').write(
    HEADER + '\n' + '\n'.join(ACC) + '\n')
print('written, accounts:', len(ACC))
