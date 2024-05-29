using Serilog;

namespace Smurftown.Backend.Automation
{
    /// <summary>
    ///     Clicks a tab that <b>cannot be calibrated</b> - because its position depends on
    ///     the width of its label and that changes with the language.
    ///     <para>
    ///         <b>The counterpart to <see cref="ScreenMap" />.</b> The calibration says where
    ///         something sits, and that holds true for almost everything: a medal, a
    ///         currency field, an input box sit at their edge, no matter which language the
    ///         client runs. A <b>tab row</b> does not - it lays out its entries side by side
    ///         in order, each as wide as its word. A single longer word further left shifts
    ///         everything that follows.
    ///     </para>
    ///     <para>
    ///         Measured: at the position where an English client shows <c>ARAM</c> (579 at
    ///         3440x1440), a German one shows <c>Heldenchaos</c> - ARAM sits at 709 there.
    ///         And <c>collection.tab</c> carried the 399 until 22.08.2026, under which
    ///         <c>SAMMLUNG</c> stands on German; on English something else already sits
    ///         there, because <c>PLAY</c> is shorter than <c>SPIELEN</c>. This bug is the
    ///         most expensive one this application knows: it aborts nothing, it opens the
    ///         wrong screen - and what gets read afterward is whatever it is.
    ///     </para>
    ///     <para>
    ///         <b>It is retried because the screen may still be under construction.</b>
    ///         Measuring stillness does not help here - behind the bar runs a moving
    ///         background that never settles. The right question is not "is something still
    ///         moving", but "does the word already stand there".
    ///     </para>
    /// </summary>
    public static class TabFinder
    {
        /// <summary>How many times the row is read before giving up.</summary>
        private const int Attempts = 3;

        /// <summary>Pause between two attempts. Every capture costs, so it stays no shorter.</summary>
        private static readonly TimeSpan Between = TimeSpan.FromMilliseconds(900);

        /// <summary>
        ///     Searches for <paramref name="word" /> in <paramref name="area" /> and clicks
        ///     its center. Returns <c>false</c> if the word did not appear on any attempt.
        ///     <para>
        ///         <b>There is deliberately no fallback to a coordinate.</b> A calibrated
        ///         point would only be correct for exactly one language, and the click next
        ///         to it silently opens the wrong screen - the same reasoning by which
        ///         <see cref="LoginLocator" /> searches for the login form in the image
        ///         instead of typing somewhere on failure.
        ///     </para>
        /// </summary>
        public static async Task<bool> ClickAsync(GameSession session, Spot area,
            string word, string what, CancellationToken token = default)
        {
            if (!TextReader.Available)
            {
                Log.Warning("Without text recognition the tab '{What}' cannot be located", what);
                return false;
            }

            var (x, y, width, height) = session.Layout.Area(area);

            for (var attempt = 1; attempt <= Attempts; attempt++)
            {
                token.ThrowIfCancellationRequested();

                var lines = await TextReader.ReadAsync(session.Capture(), x, y, width, height);

                // The SHORTEST match, not the first. In the collection's sub-tab row,
                // "Packs de héros" stands to the LEFT of "Héros" - likewise on German
                // "Heldenpakete" before "Helden", on English "Hero Packs" before "Heroes".
                // The first match would therefore consistently be the wrong tab, and a
                // purchase screen would get clicked.
                //
                // Measured on 22.08.2026 on the French client, and it is exactly the kind of
                // bug this class is meant to prevent: it aborts nothing, it opens the wrong
                // thing.
                var candidates = lines
                    .Where(line => TextNormalisation.ContainsWord(line.Text, word))
                    .OrderBy(line => TextNormalisation.StripAccents(line.Text).Trim().Length)
                    .ToList();

                var tab = candidates.FirstOrDefault();

                if (tab != null)
                {
                    if (candidates.Count > 1)
                        Log.Debug("Tab '{What}': {Count} lines contain '{Word}', taking the " +
                                  "shortest ('{Text}')", what, candidates.Count, word, tab.Text);

                    // Click near the BEGINNING of the line, not its center. The navigation
                    // bar attaches a badge to some tabs ("COLLECTION 125", "COFFRES 9"); if
                    // the recognition merges word and number into ONE line, their center
                    // lands behind the word and, in the worst case, next to the tab. The
                    // beginning, on the other hand, always hits the text - the same
                    // reasoning by which play.tab is clicked near its beginning.
                    //
                    // The cap only kicks in for overlong lines: a normal tab word is
                    // narrower than 2*limit, and there it stays at the center.
                    const int limit = 60;
                    var offset = Math.Min(tab.Width / 2, limit);

                    // Text recognition counts from the top-left corner of the CROP, not of
                    // the window - the offset must be added.
                    session.ClickAt(x + tab.X + offset, y + tab.Y + tab.Height / 2);
                    Log.Information("Tab '{What}' clicked at {X},{Y} (read '{Text}')",
                        what, x + tab.X + offset, y + tab.Y, tab.Text);
                    return true;
                }

                // Do not warn on every attempt: the message that matters stands below.
                Log.Debug("Tab '{What}' ({Word}) not found (attempt {Attempt}), read: {Text}",
                    what, word, attempt, string.Join(" | ", lines.Select(l => l.Text)));

                if (attempt < Attempts) await Task.Delay(Between, token);
            }

            Log.Warning("Tab '{What}' not found in {Attempts} attempts - looked for '{Word}' in " +
                        "the {Language} vocabulary", what, Attempts, word,
                GameVocabulary.Current.Language);
            return false;
        }
    }
}
