using Serilog;

namespace Smurftown.Backend.Automation
{
    /// <summary>
    ///     The controls of the login form, found in the image.
    ///     <para>
    ///         <paramref name="Region" /> is the CLOSED selector field. The entries of the
    ///         opened-up list are deliberately not listed here: they sit a calibration value
    ///         above it, and the caller knows that anyway. Until 21.08.2026, the
    ///         record carried a ready-made point <c>Europe</c> - with three regions that would have become
    ///         three fields that could drift apart from the calibration.
    ///     </para>
    /// </summary>
    public sealed record LoginForm(
        (int X, int Y) Email,
        (int X, int Y) Password,
        (int X, int Y) Submit,
        (int X, int Y) Region);

    /// <summary>
    ///     Finds the login form in the image, instead of calibrating it.
    ///     <para>
    ///         <b>Why this is the only screen that is searched for and not calibrated:</b> the form does
    ///         scale with the window height like everything else, but additionally shifts with the
    ///         window width. Measured at the same height: at 2560x1080 it sits 73 points
    ///         lower than at 1920x1080, size identical. An anchor cannot capture that, a
    ///         fixed coordinate set even less so.
    ///     </para>
    ///     <para>
    ///         <b>How to recognize it:</b> two equally wide boxes, starting equally far left,
    ///         directly one below the other, whose border carries the color 70,57,148.
    ///         This color does not occur in the starry sky behind it, and it does not change
    ///         with resolution - unlike any coordinate. Checked against captures at
    ///         3440x1440, 2560x1080, and 1920x1080.
    ///     </para>
    ///     <para>
    ///         If the search finds nothing, <c>null</c> is returned. There is deliberately no fallback to fixed
    ///         coordinates: an outdated coordinate set clicks
    ///         somewhere and types the password into the void, instead of saying that it does not
    ///         recognize the form.
    ///     </para>
    ///     <para>
    ///         <b><c>null</c> means "not yet", not "never".</b> The most common reason is
    ///         that the form has not been drawn yet at all - at startup a loading spinner turns
    ///         at its spot for minutes. That is why nothing is logged here and
    ///         nothing aborted: the caller keeps searching (<c>GameSession.Retry</c>) and
    ///         only reports once time has run out. The reason goes out for that
    ///         as <paramref name="reason" />.
    ///     </para>
    /// </summary>
    public static class LoginLocator
    {
        /// <summary>Rows lying closer together than this belong to the same edge.</summary>
        private const int EdgeGap = 4;

        /// <summary>How closely two edges must match in width and left position.</summary>
        private const int ShapeTolerance = 3;

        /// <summary>This many bright pixels a row of the region selector must have at minimum.</summary>
        private const int RegionRowPixels = 30;

        /// <param name="reason">
        ///     Why nothing was found - for the message that the caller issues once the
        ///     wait time runs out. Empty on success.
        /// </param>
        public static LoginForm? Find(Screenshot shot, ScreenMap map, Layout layout, out string reason)
        {
            reason = "";
            var login = map.Login;
            var edges = HorizontalEdges(shot, login, (int)(shot.Width * login.MinFieldWidth));
            var fields = FieldsFrom(edges, shot.Height, login);

            if (fields.Count < 2)
            {
                reason = $"{fields.Count} input field(s) found, two expected";
                return null;
            }

            var email = fields[0];
            var password = fields[1];
            var pitch = password.Y - email.Y;
            var submit = (email.X, (int)Math.Round(password.Y + pitch * login.SubmitPitchFactor));

            var region = RegionField(shot, map, layout);
            if (region == null)
            {
                reason = "both input fields present, but no region selector at the bottom left";
                return null;
            }

            Log.Information("Login form found: email {@Email}, password {@Password}, region {@Region}",
                email, password, region.Value);
            return new LoginForm(email, password, submit, region.Value);
        }

        /// <summary>
        ///     Horizontal runs in border color, adjacent rows merged into one edge.
        ///     Short runs are text and not an edge - hence the
        ///     minimum length.
        /// </summary>
        private static List<(double Y, int Left, int Right)> HorizontalEdges(
            Screenshot shot, ScreenMap.LoginSection login, int minimumRun)
        {
            var color = login.BorderColor;
            var tolerance = login.BorderTolerance;
            var rows = new List<(int Y, int Left, int Right)>();

            for (var y = 0; y < shot.Height; y++)
            {
                int run = 0, start = 0, best = 0, bestStart = 0;
                for (var x = 0; x < shot.Width; x++)
                {
                    var (r, g, b) = shot[x, y];
                    if (Math.Abs(r - color[0]) <= tolerance &&
                        Math.Abs(g - color[1]) <= tolerance &&
                        Math.Abs(b - color[2]) <= tolerance)
                    {
                        if (run == 0) start = x;
                        run++;
                        if (run > best)
                        {
                            best = run;
                            bestStart = start;
                        }
                    }
                    else
                    {
                        run = 0;
                    }
                }

                if (best >= minimumRun) rows.Add((y, bestStart, bestStart + best - 1));
            }

            var edges = new List<(double Y, int Left, int Right)>();
            var group = new List<(int Y, int Left, int Right)>();
            foreach (var row in rows)
            {
                if (group.Count > 0 && row.Y - group[^1].Y > EdgeGap)
                {
                    edges.Add(Merge(group));
                    group = [];
                }

                group.Add(row);
            }

            if (group.Count > 0) edges.Add(Merge(group));
            return edges;

            static (double Y, int Left, int Right) Merge(List<(int Y, int Left, int Right)> rows)
            {
                return (rows.Average(r => r.Y), rows.Min(r => r.Left), rows.Max(r => r.Right));
            }
        }

        /// <summary>
        ///     The input fields from the edges: two consecutive edges of equal width
        ///     and equal left position, at a plausible distance. Their midpoints are
        ///     returned, from top to bottom.
        /// </summary>
        private static List<(int X, int Y)> FieldsFrom(
            List<(double Y, int Left, int Right)> edges, int windowHeight, ScreenMap.LoginSection login)
        {
            var minimum = windowHeight * login.MinFieldHeight;
            var maximum = windowHeight * login.MaxFieldHeight;
            var fields = new List<(int X, int Y)>();

            for (var i = 0; i < edges.Count - 1; i++)
            {
                var top = edges[i];
                var bottom = edges[i + 1];
                var height = bottom.Y - top.Y;
                if (height < minimum || height > maximum) continue;
                if (Math.Abs(top.Right - top.Left - (bottom.Right - bottom.Left)) > ShapeTolerance) continue;
                if (Math.Abs(top.Left - bottom.Left) > ShapeTolerance) continue;

                fields.Add(((int)Math.Round((top.Left + top.Right) / 2.0),
                    (int)Math.Round((top.Y + bottom.Y) / 2.0)));
            }

            return fields.OrderBy(f => f.Y).ToList();
        }

        /// <summary>
        ///     The region selector at the bottom left. It does not carry a field border, but a bright
        ///     glowing edge - hence its own condition instead of the same color.
        /// </summary>
        private static (int X, int Y)? RegionField(Screenshot shot, ScreenMap map, Layout layout)
        {
            var (areaX, areaY, areaWidth, areaHeight) = layout.Area(map.Login.RegionArea);
            int left = int.MaxValue, right = int.MinValue, top = -1, bottom = -1;

            for (var y = areaY; y < areaY + areaHeight; y++)
            {
                int first = -1, last = -1, count = 0;
                for (var x = areaX; x < areaX + areaWidth; x++)
                {
                    var (r, g, b) = shot[x, y];
                    if (b <= 130 || b - r <= 45 || g <= 60) continue;
                    if (first < 0) first = x;
                    last = x;
                    count++;
                }

                if (count < RegionRowPixels) continue;
                left = Math.Min(left, first);
                right = Math.Max(right, last);
                if (top < 0) top = y;
                bottom = y;
            }

            if (top < 0) return null;
            return ((left + right) / 2, (top + bottom) / 2);
        }
    }
}
