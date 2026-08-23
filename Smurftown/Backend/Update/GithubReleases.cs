using System.Net;
using System.Net.Http;
using System.Text.Json;
using Serilog;

namespace Smurftown.Backend.Update
{
    /// <summary>
    ///     One published release of <c>tibbots/smurftown</c>, reduced to the three things an
    ///     update needs: which version it is, what can be downloaded, and where a human would
    ///     read about it.
    /// </summary>
    /// <param name="Version">
    ///     The tag, and this repository's tags carry <b>no</b> <c>v</c> prefix - <c>2.0.1</c>,
    ///     never <c>v2.0.1</c>. That is not cosmetic: <c>release.yml</c> compares the tag
    ///     against the <c>&lt;Version&gt;</c> of the csproj literally and refuses to build if
    ///     the two differ. The tag therefore <i>is</i> the version.
    /// </param>
    /// <param name="PageUrl">The release page, for the case where we cannot install ourselves.</param>
    public sealed record GithubRelease(string Version, string PageUrl, IReadOnlyList<GithubAsset> Assets)
    {
        /// <summary>
        ///     The one ZIP of the release.
        ///     <para>
        ///         <b>Searched, not constructed.</b> The obvious alternative would be to build
        ///         <c>Smurftown_{version}_win-x64.zip</c> from the version - the name <c>dev</c>
        ///         gives it in <c>cmd_release</c>. That would make a file name a contract
        ///         between two places that cannot see each other, and the day somebody changes
        ///         the RID or drops the version from the name, the updater stops finding
        ///         anything, with no error anywhere near the change.
        ///     </para>
        ///     <para>
        ///         <b>Exactly one</b>, and null for none or several: a release with two ZIPs is
        ///         one where somebody has to decide which, and guessing would be the worse
        ///         answer. The name that comes back out of here is also the one looked up in
        ///         <c>checksums.txt</c> - so the two can never disagree.
        ///     </para>
        /// </summary>
        public GithubAsset? Package
        {
            get
            {
                var zips = Assets
                    .Where(a => a.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                    .ToList();
                return zips.Count == 1 ? zips[0] : null;
            }
        }

        /// <summary>The checksum list <c>dev</c> writes beside the ZIP, or null if it is missing.</summary>
        public GithubAsset? Checksums =>
            Assets.FirstOrDefault(a => a.Name.Equals("checksums.txt", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>A file attached to a release.</summary>
    public sealed record GithubAsset(string Name, string Url, long Size);

    /// <summary>
    ///     Asks GitHub what the newest release is.
    ///     <para>
    ///         <b>Anonymous, and that is the whole point.</b> <c>tibbots/smurftown</c> is a
    ///         public repository, so the releases API answers without a token - no secret on
    ///         any machine, nothing that expires, nothing that could leak out of a shipped
    ///         binary. The price is the unauthenticated rate limit of 60 requests per hour and
    ///         IP address, which a check running once an hour does not come close to - it
    ///         spends one of the sixty, and only while the application is open.
    ///     </para>
    ///     <para>
    ///         <b>This is the only outbound traffic the application has.</b> It sends a URL and
    ///         a user agent, nothing else - no account data, no identifier, no telemetry.
    ///         Whoever adds a second request here changes a property that
    ///         <c>docs/security.md</c> states, and it belongs stated there as well.
    ///     </para>
    /// </summary>
    public static class GithubReleases
    {
        public const string Repository = "tibbots/smurftown";

        /// <summary>
        ///     Where a human lands when we cannot install for them. The <c>latest</c> route
        ///     redirects to whatever is current, so it stays right without a version in it.
        /// </summary>
        public const string ReleasesPage = "https://github.com/tibbots/smurftown/releases/latest";

        private const string LatestApi = "https://api.github.com/repos/tibbots/smurftown/releases/latest";

        /// <summary>
        ///     <b>One instance for the life of the process</b>, which is the documented way to
        ///     use this class: a <c>using</c> per call exhausts the socket pool under
        ///     repetition. <see cref="UpdateInstaller" /> shares this one rather than opening a
        ///     second.
        ///     <para>
        ///         The <b>user agent is mandatory</b>, not decoration. GitHub answers a request
        ///         without one with <c>403</c>, and the body then says nothing about the real
        ///         cause - which is half an hour of looking in the wrong place.
        ///     </para>
        /// </summary>
        internal static readonly HttpClient Http = Create();

        private static HttpClient Create()
        {
            var http = new HttpClient
            {
                // Generous, because this instance also carries the 34 MB download. The check
                // itself must never hold anything up and gets its own, far shorter budget
                // from the caller's cancellation token.
                Timeout = TimeSpan.FromMinutes(10)
            };

            http.DefaultRequestHeaders.Add("User-Agent", $"Smurftown/{AppVersion.Current}");
            http.DefaultRequestHeaders.Add("Accept", "application/vnd.github+json");
            http.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
            return http;
        }

        /// <summary>
        ///     The newest release, or <b>null</b> for every reason there could be: no network,
        ///     rate limit reached, GitHub down, an answer we do not understand.
        ///     <para>
        ///         <b>Null instead of an exception</b>, deliberately. The caller of this method
        ///         sits on a path where there is nothing to report to anybody: the human did
        ///         not ask for an update check, they started an account manager. A failed check
        ///         is a line in the log and otherwise a non-event - an error dialog for a
        ///         laptop that happens to be offline would be a defect, not a feature.
        ///     </para>
        /// </summary>
        public static async Task<GithubRelease?> Latest(CancellationToken cancel = default)
        {
            try
            {
                using var response = await Http.GetAsync(LatestApi, cancel);

                // 404 IS THE NORMAL ANSWER OF A REPOSITORY THAT HAS NEVER PUBLISHED ONE,
                // and it stays that way while tags exist: a tag is not a release, and this
                // route also skips drafts and prereleases. Measured on 22.08.2026 against
                // tibbots/smurftown, which carried the tags 1.0.0, 2.0.0 and 2.1.0 and
                // answered 404 for all of them.
                //
                // Information and not Warning, therefore. As a warning this would stand in
                // the log of every single start until somebody publishes the first release -
                // and a line that is always there is one nobody reads any more.
                if (response.StatusCode == HttpStatusCode.NotFound)
                {
                    Log.Information("Update check: {Repository} has no published release", Repository);
                    return null;
                }

                if (!response.IsSuccessStatusCode)
                {
                    // The rate limit deserves its own line: it is the one failure that is not
                    // a defect and that goes away by itself, and without the counter in the
                    // message it looks exactly like a broken request.
                    var remaining = response.Headers.TryGetValues("X-RateLimit-Remaining", out var values)
                        ? string.Concat(values)
                        : "?";
                    Log.Warning("Update check: GitHub answered {Status} (rate limit remaining {Remaining})",
                        (int)response.StatusCode, remaining);
                    return null;
                }

                await using var body = await response.Content.ReadAsStreamAsync(cancel);
                using var json = await JsonDocument.ParseAsync(body, cancellationToken: cancel);
                return Parse(json.RootElement);
            }
            catch (OperationCanceledException)
            {
                // The application is shutting down, or the caller's budget ran out. Not an
                // error, and above all not a log line that reads like one.
                return null;
            }
            catch (Exception e)
            {
                Log.Warning(e, "Update check failed");
                return null;
            }
        }

        private static GithubRelease? Parse(JsonElement root)
        {
            var tag = root.TryGetProperty("tag_name", out var tagName) ? tagName.GetString() : null;
            if (string.IsNullOrWhiteSpace(tag))
            {
                Log.Warning("Update check: the release carries no tag_name");
                return null;
            }

            var page = root.TryGetProperty("html_url", out var url) ? url.GetString() : null;

            var assets = new List<GithubAsset>();
            if (root.TryGetProperty("assets", out var list) && list.ValueKind == JsonValueKind.Array)
                foreach (var asset in list.EnumerateArray())
                {
                    var name = asset.TryGetProperty("name", out var n) ? n.GetString() : null;
                    var download = asset.TryGetProperty("browser_download_url", out var d) ? d.GetString() : null;
                    var size = asset.TryGetProperty("size", out var s) && s.TryGetInt64(out var bytes) ? bytes : 0L;

                    if (!string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(download))
                        assets.Add(new GithubAsset(name, download, size));
                }

            // Trimmed, because a tag typed with a trailing space would otherwise fail to parse
            // as a version, and the update would then silently never appear.
            return new GithubRelease(tag.Trim(),
                string.IsNullOrWhiteSpace(page) ? ReleasesPage : page,
                assets);
        }
    }
}
