using System.Text.RegularExpressions;
using Serilog;

namespace Smurftown.Backend.Automation
{
    /// <summary>
    ///     How many penalty games are open.
    ///     <para>
    ///         <see cref="Games" /> is <c>null</c> when this could not be clarified -
    ///         <b>not</b> 0. This distinction carries everything: 0 means "no penalty,
    ///         confirmed", <c>null</c> means "we do not know" and must not touch the stored
    ///         value.
    ///     </para>
    /// </summary>
    public sealed record PenaltyReading(int? Games, string Note);

    /// <summary>
    ///     Reads the leaver-penalty status from the menu.
    ///     <para>
    ///         <b>Two steps, and the first is cheap.</b> Whether a penalty is running at all
    ///         is told by a red-bordered warning triangle to the right below the profile
    ///         picture - this can be detected by color without text recognition. Measured in
    ///         the 38x37 calibration box: <b>660</b> red pixels for MUGGLE#21197 with a
    ///         penalty, <b>0</b> for GODOR#21291 without. At the second resolution
    ///         (3251x1361) it was 557 - the number scales with the area, the threshold of 100
    ///         carries both with a wide margin.
    ///     </para>
    ///     <para>
    ///         <b>The count, on the other hand, stands only in the tooltip text</b> that the
    ///         game shows on hovering the symbol. The symbol itself only says THAT a penalty
    ///         is running.
    ///     </para>
    ///     <para>
    ///         <b>Why a plain digit search is enough</b>: the tooltip text contains exactly
    ///         ONE number in both languages - "...wenn du 3 Spiele in den Modi Schnellsuche
    ///         oder ARAM ohne Verbindungsabbruch gewonnen hast" or, in English,
    ///         "...until you win 3 Quick Match or ARAM games without disconnecting". That is
    ///         why there is no word for this in <see cref="GameVocabulary" />: there is
    ///         nothing to translate. If the recognition finds more than one number or none,
    ///         nothing is written - a wrong count would be worse than none at all.
    ///     </para>
    ///     <para>
    ///         <b>The absence of the symbol is a statement</b>, but only on a menu screen.
    ///         During a loading process there is likewise nothing there - and writing
    ///         "no penalty" because there simply was nothing to see right then would delete a
    ///         correct value. That is why the screen check comes before everything else.
    ///     </para>
    /// </summary>
    public static class PenaltyReader
    {
        /// <summary>
        ///     How many points in the box must be strongly red for the symbol to count as
        ///     present. The condition is deliberately strict (red clearly above green and
        ///     blue): in the menu, a blue trophy symbol and a purple arrow symbol sit next to
        ///     it, and they must not count.
        /// </summary>
        private const int MinRedPixels = 100;

        /// <summary>How many times the tooltip text is read before giving up.</summary>
        private const int ReadAttempts = 3;

        private static readonly Regex Numbers = new(@"\d+", RegexOptions.Compiled);

        public static async Task<PenaltyReading> ReadAsync(GameSession session,
            CancellationToken token = default)
        {
            var map = session.Map.Penalty;
            var shot = session.Capture();

            if (session.ScreenOf(shot) != GameScreen.Menu)
                return new PenaltyReading(null, "Leaver penalty not read - this is not a menu screen.");

            var area = session.Layout.Area(map.Icon);
            var red = CountRed(shot, area);
            Log.Debug("Penalty icon: {Red} red pixels in {@Area}", red, area);
            if (red < MinRedPixels) return new PenaltyReading(0, "No leaver penalty.");

            if (!TextReader.Available)
                return new PenaltyReading(null,
                    "Leaver penalty active, but without text recognition the count stays unread.");

            var (x, y) = session.Layout.Point(map.Icon);
            var (tx, ty, tw, th) = session.Layout.Area(map.Tooltip);
            var text = "";

            for (var attempt = 1; attempt <= ReadAttempts; attempt++)
            {
                token.ThrowIfCancellationRequested();

                // Point at it AGAIN before every capture. A capture brings the window to the
                // front, and a tooltip hangs off the cursor - whoever points once and then
                // photographs three times may photograph into empty space twice.
                session.HoverAt(x, y);
                await Task.Delay(TimeSpan.FromMilliseconds(900), token);

                text = await TextReader.ReadTextAsync(session.Capture(), tx, ty, tw, th);
                var found = Numbers.Matches(text)
                    .Select(m => int.TryParse(m.Value, out var v) ? v : -1)
                    .Where(v => v is > 0 and < 100)
                    .Distinct()
                    .ToArray();

                if (found.Length == 1) return new PenaltyReading(found[0], $"Leaver penalty: {found[0]} game(s).");
                if (found.Length > 1)
                    Log.Warning("Penalty tooltip holds {Count} numbers ({Numbers}) - expected one",
                        found.Length, string.Join(", ", found));
            }

            var path = GameSession.SaveDiagnostic(session.Capture(), "penalty-count-unreadable");
            return new PenaltyReading(null,
                $"Leaver penalty active, but the count was unreadable in {ReadAttempts} attempts. " +
                $"Screenshot: {path}");
        }

        /// <summary>
        ///     Points that are strongly red: the red share at least twice as high as green and
        ///     blue. The threshold of 90 keeps out the dark background, the ratio keeps out
        ///     the neighboring symbols.
        /// </summary>
        private static int CountRed(Screenshot shot, (int X, int Y, int Width, int Height) area)
        {
            var count = 0;
            var right = Math.Min(area.X + area.Width, shot.Width);
            var bottom = Math.Min(area.Y + area.Height, shot.Height);

            for (var y = Math.Max(area.Y, 0); y < bottom; y++)
            for (var x = Math.Max(area.X, 0); x < right; x++)
            {
                var (r, g, b) = shot[x, y];
                if (r > 90 && r > g * 2 && r > b * 2) count++;
            }

            return count;
        }
    }
}
