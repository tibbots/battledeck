using Serilog;

namespace Battledeck.Backend.Automation
{
    /// <summary>
    ///     The PLAY screen and its modes.
    ///     <para>
    ///         Exactly one is needed so far: <b>ARAM as the done signal</b>. After a run that
    ///         leaves the game open, the client would otherwise be sitting on some screen of
    ///         the collection - and whoever comes back to the machine cannot tell whether the
    ///         app is done or still paging through screens. On ARAM it is done, and one can
    ///         press "Ready" right away.
    ///     </para>
    ///     <para>
    ///         <b>The tab is SEARCHED FOR, not calibrated</b>, and that is the core of this
    ///         file. The tab row lays out its entries side by side by text width, and that
    ///         changes with the language: at the position where an English client shows
    ///         <c>ARAM</c> (579 at 3440x1440), a German one shows <c>Heldenchaos</c> - ARAM
    ///         sits at 709 there. A stored anchor would thus open the wrong mode as soon as
    ///         someone switches language, and silently so. The same approach as
    ///         <see cref="HeaderReader" />, which likewise searches for the loot-chest tab as
    ///         a word.
    ///     </para>
    ///     <para>
    ///         <b>The word therefore does NOT live in <see cref="GameVocabulary" /></b>:
    ///         "ARAM" is an abbreviation and identical in all checked language versions. What
    ///         would be translated would have to live there - this does not.
    ///     </para>
    /// </summary>
    public static class PlayScreen
    {
        /// <summary>How many times the tab row is read before giving up.</summary>
        private const int Attempts = 3;

        private const string Aram = "ARAM";

        /// <summary>
        ///     Switches to PLAY and there to ARAM. Returns <c>false</c> if the tab was not
        ///     found - that is not an error that should abort anything: it is a signal for
        ///     the human, not a step that data depends on.
        /// </summary>
        public static async Task<bool> ShowAramAsync(GameSession session,
            CancellationToken token = default)
        {
            var map = session.Map.Play;

            // The PLAY tab itself is uncritical: it is the first in the bar, hangs on the
            // left edge, and is clicked near its BEGINNING - "PLAY" is shorter than
            // "SPIELEN", but the start sits in the same place for both.
            session.Window.BringToFront();
            session.Click(map.Tab);
            await Task.Delay(TimeSpan.FromMilliseconds(2500), token);

            // If it fails, nothing aborts: it is a signal for the human and not a step that
            // data depends on - the client just stays wherever it stays.
            return await TabFinder.ClickAsync(session, map.ModeBar, Aram, "ARAM", token);
        }
    }
}
