using Serilog;
using Smurftown.Backend.Texts;

namespace Smurftown.Backend.Automation
{
    /// <summary>
    ///     What the opening achieved. <paramref name="Remaining" /> is <c>null</c> when the
    ///     counter was no longer readable at the end - then only how many chests the run
    ///     itself worked through is certain.
    /// </summary>
    public sealed record LootResult(int Opened, int? Remaining, string Note);

    /// <summary>
    ///     Opens all unopened loot chests.
    ///     <para>
    ///         The whole flow hangs on <b>one key</b>: the space bar, three times per chest.
    ///         Once opens the chest, once flips all four cards <b>at once</b>, once accepts.
    ///         Afterwards the carousel moves on by itself to the next kind with stock; kinds
    ///         that run out disappear.
    ///     </para>
    ///     <para>
    ///         <b>Before, this was six clicks on five calibrated points</b> - open, four
    ///         hexagons individually, accept. That these are gone is not only shorter but
    ///         safer: 22 points to the right of "Annehmen" sits <b>"Neuer Versuch: 250 Gold"</b>.
    ///         As long as clicks went there, a slipped anchor was a way to burn the account's
    ///         gold. The space bar selects "Annehmen" by itself; verified against two runs
    ///         with an unchanged gold balance (20.08.2026, QUESTQUEEN).
    ///     </para>
    ///     <para>
    ///         <b>The stop condition remains the counter.</b> After every chest the badge on
    ///         the BEUTE tab is read again; if it has not dropped, it is over instead of
    ///         pressing on. This catches the case where a keypress is swallowed and the flow
    ///         falls out of step - the cost, now that the clicks are gone, is only time, but
    ///         a loop that achieves nothing should still end.
    ///     </para>
    /// </summary>
    public static class LootOpener
    {
        /// <summary>After the first press: the chest opens, four cards are dealt out.</summary>
        private static readonly TimeSpan OpenDelay = TimeSpan.FromMilliseconds(2500);

        /// <summary>After the second: all four turn over, the names fade in.</summary>
        private static readonly TimeSpan RevealDelay = TimeSpan.FromMilliseconds(2500);

        /// <summary>After the third: back to the loot page.</summary>
        private static readonly TimeSpan AcceptDelay = TimeSpan.FromMilliseconds(2500);

        /// <summary>
        ///     How many times a chest is tried at most. Two, because the game occasionally
        ///     swallows a keypress. A second pass heals that: if the first did not register it
        ///     at all, the second catches it up, and if the flow is offset by one step, it
        ///     falls back into rhythm this way. How many chests actually got finished is told
        ///     afterwards by the counter, not by the count of keypresses.
        /// </summary>
        private const int Attempts = 2;

        /// <summary>
        ///     How many times the counter is read before it counts as unreadable.
        ///     <para>
        ///         More than once, and this is measured: <b>while the chest opens, the
        ///         navigation bar disappears</b> along with the badge. Whoever reads at that
        ///         moment gets "kein BEUTE gefunden" and thus <c>null</c> - which would mean
        ///         "abort" here, even though it was just looked at too early. The same trap
        ///         as with the login form.
        ///     </para>
        /// </summary>
        private const int CountAttempts = 3;

        private static readonly TimeSpan CountPause = TimeSpan.FromMilliseconds(800);

        /// <summary>
        ///     How many rounds a run may take when the starting count could not be read. That
        ///     happens exactly when the badge shows a lone digit, which text recognition does
        ///     not return - so at most nine chests are standing there, and fifteen rounds
        ///     leave room for a swallowed key on several of them without letting a run that
        ///     never finishes go on for ever.
        /// </summary>
        private const int UnknownCeiling = 15;

        public static async Task<LootResult> OpenAllAsync(GameSession session,
            IProgress<string>? progress = null, CancellationToken token = default)
        {
            if (!TextReader.Available)
                return new LootResult(0, null,
                    "Without text recognition the chest counter cannot be read - nothing opened.");

            // Searched for, not clicked: the same tab that CountLootChestsAsync just below
            // finds via its WORD. Until 22.08.2026 a fixed point stood here (loot.tab at
            // x=630) - measured on German, where PLAY and COLLECTION come before it. On any
            // shorter variant a different tab sits there, and the click silently opens the
            // wrong screen.
            session.Window.BringToFront();
            if (!await TabFinder.ClickAsync(session, session.Map.Loot.NavBar,
                    GameVocabulary.Current.LootTab, "loot", token))
                return new LootResult(0, null,
                    "The loot tab was not found - nothing opened. Check that the client language " +
                    "matches the one set in Smurftown.");

            await Task.Delay(3000, token);

            var state = await CountWithRetry(session, token);
            if (state == null)
            {
                var path = GameSession.SaveDiagnostic(session.Capture(), "chest-counter-unreadable");
                return new LootResult(0, null,
                    $"Chest counter unreadable - nothing was opened. Screenshot: {path}");
            }

            if (!state.AnyLeft) return new LootResult(0, 0, "No unopened chests.");

            var start = state.Number;
            var opened = 0;
            Log.Information("Loot: {Count} unopened chests", Describe(start));

            // The ceiling is the count from the start. No chests are added in the meantime,
            // so every further round is a sign that something is wrong. If the start itself
            // could not be read, the badge was showing a lone digit - at most nine, see
            // HeaderReader.HasBadgeBox - and UnknownCeiling leaves room for a swallowed key
            // on each of them.
            var ceiling = start ?? UnknownCeiling;

            while (state.AnyLeft && opened < ceiling)
            {
                token.ThrowIfCancellationRequested();
                progress?.Report(Strings.FormatForLog("progress.chest", opened + 1, ceiling));

                var before = state;
                var after = await OpenOne(session, before, token);

                if (after == null)
                {
                    var path = GameSession.SaveDiagnostic(session.Capture(), "chest-not-opened");
                    return new LootResult(opened, before.Number,
                        $"{opened} of {Describe(start)} chests opened, then the counter stayed " +
                        $"at {Describe(before.Number)} - aborted. Screenshot: {path}");
                }

                if (before.Number is { } was && after.Number is { } now)
                {
                    // A ROUND OPENS ONE CHEST, at most two - OpenOne presses again when the
                    // game swallowed a key, and that retry can carry a second one along. A
                    // bigger drop is not a faster run, it is a misread counter. Measured on
                    // 23.08.2026: the badge went unread, came back as 0, and the 44 chests
                    // still standing were booked as opened in a single round - the run then
                    // reported "65 chests opened" while 21 had been. There was a guard for a
                    // counter that does not move and none for one that jumps.
                    var dropped = was - now;
                    if (dropped > Attempts)
                    {
                        var path = GameSession.SaveDiagnostic(session.Capture(), "chest-counter-jumped");
                        return new LootResult(opened, before.Number,
                            $"{opened} of {Describe(start)} chests opened, then the counter " +
                            $"jumped from {was} to {now} in one round - aborted. " +
                            $"Screenshot: {path}");
                    }

                    opened += dropped;
                }
                else
                {
                    // One of the two sides has no number - the badge is showing a lone digit,
                    // and text recognition does not return those. The round is then counted
                    // rather than subtracted, and the badge alone decides when to stop.
                    opened++;
                }

                state = after;
            }

            Log.Information("Loot: {Opened} chests opened, {Remaining} left",
                opened, Describe(state.Number));

            return new LootResult(opened, state.Number,
                !state.AnyLeft
                    ? $"{opened} chests opened."
                    : $"{opened} of {Describe(start)} chests opened, {Describe(state.Number)} left.");
        }

        /// <summary>A count for a message - or a phrase, where none could be read.</summary>
        private static string Describe(int? count)
        {
            return count?.ToString() ?? "an unreadable number of";
        }

        /// <summary>
        ///     One chest: open, reveal, accept - the same key three times. Returns the new
        ///     state, or <c>null</c> if nothing moved after all attempts.
        /// </summary>
        private static async Task<LootCount?> OpenOne(GameSession session, LootCount before,
            CancellationToken token)
        {
            for (var attempt = 1; attempt <= Attempts; attempt++)
            {
                session.PressSpace();
                await Task.Delay(OpenDelay, token);

                session.PressSpace();
                await Task.Delay(RevealDelay, token);

                session.PressSpace();
                await Task.Delay(AcceptDelay, token);

                var now = await CountWithRetry(session, token);
                if (now == null) continue;

                // Progress shows itself in two ways: the counter dropped, or the badge is
                // gone altogether. In the range where the number cannot be read at all,
                // neither side carries one - and then the round counts as done. The badge is
                // the stop condition there, and the ceiling in the caller bounds how often we
                // may come back here.
                if (!now.AnyLeft) return now;
                if (before.Number is { } was && now.Number is { } left && left < was) return now;
                if (before.Number == null && now.Number == null) return now;

                Log.Warning("Loot: counter still at {Before} after attempt {Attempt}",
                    Describe(before.Number), attempt);
            }

            return null;
        }

        private static async Task<LootCount?> CountWithRetry(GameSession session,
            CancellationToken token)
        {
            for (var attempt = 1; attempt <= CountAttempts; attempt++)
            {
                token.ThrowIfCancellationRequested();
                if (attempt > 1) await Task.Delay(CountPause, token);

                var count = await HeaderReader.CountLootChestsAsync(session, session.Capture());
                if (count != null) return count;
            }

            return null;
        }
    }
}
