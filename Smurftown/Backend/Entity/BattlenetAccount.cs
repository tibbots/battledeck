using YamlDotNet.Serialization;

namespace Smurftown.Backend.Entity
{
    public class BattlenetAccount : IComparable<BattlenetAccount>
    {
        private string _discriminator = "";
        private string _email = "";
        private string _name = "";
        private Dictionary<string, List<BattlenetRegion>> _regionsByGame = new();

        /// <summary>
        ///     The part of the battletag before the <c>#</c>. Since 21.08.2026 <b>no longer
        ///     maintained by hand</b>: it is taken over from the profile overlay when reading,
        ///     and until then it is empty. A freshly created account therefore has no name -
        ///     what names it is told by <see cref="DisplayName" />.
        ///     <para>
        ///         The setter catches <c>null</c> like the one for
        ///         <see cref="RegionsByGame" />: a key without a value (<c>name:</c>) would
        ///         otherwise deserialize to null, and
        ///         <c>ToUpper()</c> on that crashes the application before the list is even up.
        ///     </para>
        /// </summary>
        public required string Name
        {
            get => _name;
            set => _name = value?.ToUpper() ?? "";
        }

        /// <summary>The digits after the <c>#</c>. Read like <see cref="Name" />, not typed.</summary>
        public required string Discriminator
        {
            get => _discriminator;
            set => _discriminator = value ?? "";
        }

        public required string Email
        {
            get => _email;
            set => _email = value.ToLower();
        }

        /// <summary>
        ///     Not <c>required</c> since 24.08.2026: an account whose human does not want to
        ///     hand this application their Battle.net password can leave it empty and still use
        ///     every other feature. The one thing that costs is the automated start -
        ///     <see cref="Automation.GameSession.FillCredentials" /> refuses to type a value that
        ///     is not there, and the row's start menu hides itself for exactly this reason (see
        ///     <c>AccountCardViewModel.BuildStartOptions</c>). Reading still works: whoever starts
        ///     Heroes of the Storm and signs in themselves is picked up by the header chip, the
        ///     same way it already reads any client that is already running.
        /// </summary>
        public string Password { get; set; } = "";

        public required string Notes { get; set; }
        public required DateTime LatestInteractionAt { get; set; }

        /// <summary>
        ///     Which regions this account is played in - <b>per game</b>, since 22.08.2026.
        ///     The key is a game id out of <see cref="Games" />, the value the regions it is
        ///     played in.
        ///     <para>
        ///         <b>Why per game and not per account</b>: the same battletag can be played
        ///         in Europe and America in Heroes of the Storm while every other game only
        ///         runs in Europe. A single list on the account could not say that - it would
        ///         claim an American World of Warcraft account that does not exist, and give
        ///         it a row of its own in the overview.
        ///     </para>
        ///     <para>
        ///         <b>This dictionary replaced the four booleans</b> <c>Overwatch</c>,
        ///         <c>Hots</c>, <c>Wow</c> and <c>Diablo</c>. They said the same thing twice
        ///         over from the moment the regions moved down here, and the pair could
        ///         contradict itself: a game ticked without a single region was an account
        ///         nothing showed - the row it would be edited in did not exist. Whether a
        ///         game is played is now one question and one answer: does it have a region
        ///         (<see cref="Plays" />).
        ///     </para>
        ///     <para>
        ///         An entry with an empty list is therefore <b>not</b> written -
        ///         <see cref="SetRegions" /> removes the key instead. Read via
        ///         <see cref="RegionsFor" />, which yields an empty list for an unknown game;
        ///         a <c>data.yaml</c> from a newer version can name games this one does not
        ///         know, and that is not an error but a no.
        ///     </para>
        ///     <para>
        ///         The setter catches <c>null</c> like the one for <see cref="Name" />: a
        ///         key without a value would otherwise deserialize to null.
        ///     </para>
        /// </summary>
        public Dictionary<string, List<BattlenetRegion>> RegionsByGame
        {
            get => _regionsByGame;
            set => _regionsByGame = value ?? new Dictionary<string, List<BattlenetRegion>>();
        }

        /// <summary>
        ///     The game state per region. An entry is only created once there is something to
        ///     save - a chosen region without any read values is therefore not in here, and
        ///     that is the difference between "chosen" (<see cref="RegionsByGame" />) and
        ///     "has data".
        ///     <para>
        ///         The key is the region itself; YamlDotNet writes it as plain text
        ///         (<c>Europe:</c>). Read via <see cref="HotsIn" />, written via
        ///         <see cref="HotsFor" /> - <b>never directly</b>, otherwise merely displaying
        ///         it would create entries.
        ///     </para>
        /// </summary>
        public Dictionary<BattlenetRegion, HotsRegionData> HotsByRegion { get; set; } = new();

        /// <summary>
        ///     Archived - the account keeps existing, but no longer shows up in the overview.
        ///     This is deliberately not a deletion: the credentials are the actual value of
        ///     the app, and a mis-click in a list of 27 rows would otherwise be unrecoverable.
        ///     <para>
        ///         Not <c>required</c> and with a default of <c>false</c>, like every field
        ///         added afterwards: existing <c>data.yaml</c> files don't know the key,
        ///         and YamlDotNet then sets the default.
        ///     </para>
        /// </summary>
        public bool Inactive { get; set; }

        /// <summary>
        ///     Is the battletag known? It gets filled in when reading, a freshly created
        ///     account doesn't have one yet. This question decides whether a lone <c>#</c>
        ///     would show up somewhere - in the row, in the sorting, in the hero picker's title.
        ///     <para>
        ///         <c>[YamlIgnore]</c> is mandatory here, not a nicety: YamlDotNet
        ///         serializes <b>every</b> public property, and a computed value would
        ///         otherwise stand as its own key in <c>data.yaml</c> - kept twice over
        ///         and ignored on the next read. That is exactly why
        ///         <see cref="Battletag" />, <see cref="Covers" /> and <see cref="Plays" />
        ///         are methods. Whoever adds a property here adds the attribute along with it.
        ///     </para>
        /// </summary>
        [YamlIgnore]
        public bool HasBattletag => Name.Length > 0 && Discriminator.Length > 0;

        /// <summary>
        ///     How the account is to be named: the battletag, as long as there is one,
        ///     otherwise the email. The email is the identity anyway (see
        ///     <see cref="Equals(object?)" />) and therefore the only piece of data that
        ///     exists for every account.
        ///     <para>
        ///         Property and not a method, because the list sorts via
        ///         <c>SortDescription(nameof(DisplayName))</c> and that needs a
        ///         property name for it. <see cref="CompareTo" /> alone is not enough - an
        ///         <c>ICollectionView</c> with sort descriptions doesn't call it at all.
        ///     </para>
        /// </summary>
        [YamlIgnore]
        public string DisplayName => HasBattletag ? Battletag() : Email;

        /// <summary>
        ///     Sorts via <see cref="DisplayName" /> and not via <see cref="Name" />:
        ///     otherwise all not-yet-read accounts with an empty name would stand together at
        ///     the very top, in arbitrary order relative to each other.
        /// </summary>
        public int CompareTo(BattlenetAccount? other)
        {
            return other == null
                ? 1
                : string.Compare(DisplayName, other.DisplayName, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        ///     Every region in which <b>any</b> game of this account is played, in the order
        ///     of the selection list. This is the union over <see cref="RegionsByGame" /> -
        ///     and exactly the set of rows the overview builds from it.
        ///     <para>
        ///         The row deliberately carries no game: which one it shows is decided by the
        ///         game filter, and it is exclusive. A row per game and region would be the
        ///         same set once more, only with a field nothing reads.
        ///     </para>
        ///     <para>
        ///         <c>[YamlIgnore]</c> like every computed value here - see
        ///         <see cref="HasBattletag" />. It is also a property and not a method for
        ///         that reason alone: whoever adds one without the attribute writes it into
        ///         <c>data.yaml</c>, where it would be carried twice and read never.
        ///     </para>
        /// </summary>
        [YamlIgnore]
        public IReadOnlyList<BattlenetRegion> PlayedRegions =>
            BattlenetRegions.InDisplayOrder.Where(Covers).ToList();

        /// <summary>
        ///     Is this account played in this region - in <b>any</b> game? The question
        ///     decides whether the pair even gets a row at all; which game that row then
        ///     shows is a second question (<see cref="PlaysIn" />).
        /// </summary>
        public bool Covers(BattlenetRegion region)
        {
            return _regionsByGame.Values.Any(regions => regions.Contains(region));
        }

        /// <summary>
        ///     The regions this game is played in - empty for a game that is not played, and
        ///     likewise for one this version does not know.
        /// </summary>
        public IReadOnlyList<BattlenetRegion> RegionsFor(string? game)
        {
            if (game == null) return [];
            return _regionsByGame.TryGetValue(game, out var regions) ? regions : [];
        }

        /// <summary>
        ///     Is this game played in this region? The one question the overview's filter
        ///     asks - and the reason a World of Warcraft account played only in Europe no
        ///     longer shows up under America.
        /// </summary>
        public bool PlaysIn(string? game, BattlenetRegion region)
        {
            return RegionsFor(game).Contains(region);
        }

        /// <summary>
        ///     Sets the regions of one game. An <b>empty</b> list removes the game instead of
        ///     storing it empty: a game without a region is a game that is not played, and
        ///     two ways of saying that would be two states to keep apart.
        ///     <para>
        ///         The order of the stored list is that of the selection list, not that of
        ///         the clicks - <c>data.yaml</c> should not change just because someone
        ///         ticked America before Europe.
        ///     </para>
        /// </summary>
        public void SetRegions(string game, IEnumerable<BattlenetRegion> regions)
        {
            var ordered = BattlenetRegions.InDisplayOrder.Where(regions.Contains).ToList();
            if (ordered.Count == 0) _regionsByGame.Remove(game);
            else _regionsByGame[game] = ordered;
        }

        /// <summary>
        ///     The game state of this region, or <c>null</c> if there isn't one yet.
        ///     <para>
        ///         <b>Nullable and not an empty record</b>: "never read in this region"
        ///         is a statement the UI has to show - an empty state would look like
        ///         "owns nothing, has zero gold". The same distinction as with
        ///         <see cref="HotsRegionData.ReadAt" />, just one level higher.
        ///     </para>
        ///     <para>
        ///         A shared <c>Empty</c> record would be the obvious shortcut and a
        ///         landmine: the class has setters, and whoever wrote to the shared object
        ///         would change the state of all accounts at once.
        ///     </para>
        /// </summary>
        public HotsRegionData? HotsIn(BattlenetRegion region)
        {
            return HotsByRegion.GetValueOrDefault(region);
        }

        /// <summary>
        ///     The game state of this region for <b>writing</b> - creates it if it doesn't
        ///     exist yet. Only call from write paths (reading from the game, the dialog): every
        ///     call can create an entry in <c>data.yaml</c>.
        /// </summary>
        public HotsRegionData HotsFor(BattlenetRegion region)
        {
            if (HotsByRegion.TryGetValue(region, out var existing)) return existing;

            var fresh = new HotsRegionData();
            HotsByRegion[region] = fresh;
            return fresh;
        }

        public string Battletag()
        {
            return (Name + "#" + Discriminator).ToUpper();
        }

        /// <summary>
        ///     The counterpart to <see cref="Battletag" />: splits <c>NAME#12345</c> back
        ///     into its two parts. <c>false</c> if the text cannot be a battletag.
        ///     <para>
        ///         This is needed in exactly one place - when reading the profile overlay
        ///         finds a different battletag than the stored one and the app has to decide
        ///         whether that was a rename. The form check there is the first of two
        ///         safeguards: it catches reading errors that don't even look like a
        ///         battletag in the first place.
        ///     </para>
        ///     <para>
        ///         The limits are Blizzard's: a name starts with a letter, consists of
        ///         letters and digits and is 3 to 16 characters long; the number behind it is
        ///         3 to 6 digits. Deliberately strict - here a wrongly rejected battletag is
        ///         cheap (the human retypes it), while a wrongly accepted one renames an
        ///         account.
        ///     </para>
        /// </summary>
        public static bool TrySplitBattletag(string? text, out string name, out string discriminator)
        {
            name = "";
            discriminator = "";
            if (string.IsNullOrWhiteSpace(text)) return false;

            var parts = text.Trim().Split('#');
            if (parts.Length != 2) return false;

            var left = parts[0];
            var right = parts[1];

            if (left.Length is < 3 or > 16) return false;
            if (!char.IsLetter(left[0])) return false;
            if (!left.All(char.IsLetterOrDigit)) return false;

            if (right.Length is < 3 or > 6) return false;
            if (!right.All(char.IsAsciiDigit)) return false;

            name = left;
            discriminator = right;
            return true;
        }

        /// <summary>
        ///     Is this game played at all - in any region? The ids are in
        ///     <see cref="Games" />. An unknown id is not an error, but a no -
        ///     a <c>data.yaml</c> from a newer version can name games this version
        ///     doesn't know.
        ///     <para>
        ///         Until 22.08.2026 there were four booleans behind this. They are gone:
        ///         a game is played exactly when it has a region, and that leaves no second
        ///         answer that could contradict the first. See <see cref="RegionsByGame" />.
        ///     </para>
        ///     <para>
        ///         <b>Mind where this is the wrong question.</b> Everything shown in a row
        ///         belongs to one region, and there <see cref="PlaysIn" /> applies - this
        ///         one only asks whether the game occurs anywhere at all.
        ///     </para>
        /// </summary>
        public bool Plays(string? game)
        {
            return RegionsFor(game).Count > 0;
        }

        private bool Equals(BattlenetAccount other)
        {
            return _email == other._email;
        }

        public override bool Equals(object? obj)
        {
            if (ReferenceEquals(null, obj)) return false;
            if (ReferenceEquals(this, obj)) return true;
            return obj.GetType() == this.GetType() && Equals((BattlenetAccount)obj);
        }

        public override int GetHashCode()
        {
            return _email.GetHashCode();
        }
    }
}