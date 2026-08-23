using YamlDotNet.Serialization;

namespace Smurftown.Backend.Entity
{
    /// <summary>
    ///     The game state of ONE account in ONE region.
    ///     <para>
    ///         Until 21.08.2026 these eleven values sat flat on
    ///         <see cref="BattlenetAccount" />, because there was only one state per account.
    ///         That was wrong: in Heroes of the Storm progress is <b>region-bound</b> - the
    ///         same battletag has a different rank, different heroes and different gold in
    ///         Europe than in America. Whoever played both regions previously had, inevitably,
    ///         one of the two states in the file and not the other.
    ///     </para>
    ///     <para>
    ///         <b>What does NOT belong here</b>: everything that hangs on the account and
    ///         not on the game state - email, password, battletag, the four game checkboxes
    ///         and the archive status. The battletag is the clearest boundary: it is global at
    ///         Blizzard and the same in every region.
    ///     </para>
    /// </summary>
    public class HotsRegionData
    {
        private List<string> _heroes = [];

        /// <summary>Storm League rank tier in this region.</summary>
        public HotsRankTier Tier { get; set; } = HotsRankTier.None;

        /// <summary>
        ///     Division 5 (lowest) to 1 (highest). 0 if the tier has no divisions
        ///     (None, Master, GrandMaster). Normalized when saving in the dialog.
        /// </summary>
        public int Division { get; set; }

        /// <summary>
        ///     Progress inside the current division, as the game shows it while the pointer
        ///     rests on the rank: <c>328 / 1000</c>. <see cref="RankPoints" /> is the left
        ///     number, <see cref="RankPointsMax" /> the right one.
        ///     <para>
        ///         <b>Two numbers and not a percentage.</b> A stored share would throw the
        ///         bound away, and the bound is half of what the game says - the tooltip on
        ///         the medal names both. It is kept per region for the same reason the rank
        ///         is: the same battletag stands somewhere else in America than in Europe.
        ///     </para>
        ///     <para>
        ///         <c>null</c> means "never read", exactly as with <see cref="Gold" />, and
        ///         must not become 0: an account at the start of its division has zero
        ///         points, and that is a statement, not a gap. The medal shows an untouched
        ///         ring in both cases - but only the read one may claim it.
        ///     </para>
        ///     <para>
        ///         Master and Grand Master have neither. They have no next division to fill
        ///         towards, so nothing writes here for them.
        ///     </para>
        /// </summary>
        public int? RankPoints { get; set; }

        public int? RankPointsMax { get; set; }

        /// <summary>
        ///     Open penalty games after a disconnect (leaver penalty). 0 means "none".
        ///     Region-bound like everything here: whoever leaves a game in Europe may keep
        ///     playing in America.
        /// </summary>
        public int PenaltyGames { get; set; }

        /// <summary>
        ///     Placement games are still pending (usually 3), so the rank only applies again
        ///     afterward. <see cref="Tier" /> stays put in the meantime - it is the rank from
        ///     the previous season, not an invalid value.
        /// </summary>
        public bool PlacementsPending { get; set; }

        /// <summary>
        ///     Ids of the heroes this account owns IN THIS REGION, e.g.
        ///     <c>muradin</c>, <c>li-ming</c>, <c>lucio</c>.
        ///     <para>
        ///         The setter catches <c>null</c>: a key without a value (<c>heroes:</c>)
        ///         would otherwise deserialize to null and every access would run into
        ///         nothing.
        ///     </para>
        ///     Resolved via <see cref="HotsHeroCatalog.Resolve" /> - unknown
        ///     ids from a newer app version are skipped there, but not removed here.
        /// </summary>
        public List<string> Heroes
        {
            get => _heroes;
            set => _heroes = value ?? [];
        }

        /// <summary>
        ///     Gold, shards and gems, plus the account level. Written by reading from the
        ///     game, not by hand - that's why they also don't appear in the edit dialog.
        ///     <c>null</c> means "never read yet" and must be distinguished from "has zero
        ///     gold"; a value of 0 would be a statement we don't have.
        /// </summary>
        public int? Gold { get; set; }

        public int? Shards { get; set; }
        public int? Gems { get; set; }
        public int? AccountLevel { get; set; }

        /// <summary>
        ///     Unopened loot chests, as the sum over all chest types - just as the game
        ///     shows it as a badge on the LOOT tab. Here too, <c>null</c> ("not
        ///     read") must be distinguished from 0 ("none there").
        /// </summary>
        public int? LootChests { get; set; }

        /// <summary>
        ///     When the game was last read - <b>in this region</b>. Without this
        ///     timestamp there would be no way to say whether an empty hero list means
        ///     "owns none" or "was never read", and that is exactly what determines whether
        ///     a result may be overwritten.
        /// </summary>
        public DateTime? ReadAt { get; set; }

        /// <summary>
        ///     Has this region ever been read at all? Separates "owns nothing"
        ///     from "we don't know".
        ///     <para>
        ///         <c>[YamlIgnore]</c> is mandatory: YamlDotNet serializes every public
        ///         property, and a computed value would otherwise stand as its own key in
        ///         the file - kept twice over and ignored on the next read.
        ///     </para>
        /// </summary>
        [YamlIgnore]
        public bool EverRead => ReadAt != null;

        /// <summary>
        ///     An independent copy. Needed by the edit dialog: it works on copies so that
        ///     canceling really changes nothing - working on the original, the half-typed
        ///     rank would still be in the file even after "Cancel".
        /// </summary>
        public HotsRegionData Copy()
        {
            return new HotsRegionData
            {
                Tier = Tier,
                Division = Division,
                RankPoints = RankPoints,
                RankPointsMax = RankPointsMax,
                PenaltyGames = PenaltyGames,
                PlacementsPending = PlacementsPending,
                Heroes = [.. Heroes],
                Gold = Gold,
                Shards = Shards,
                Gems = Gems,
                AccountLevel = AccountLevel,
                LootChests = LootChests,
                ReadAt = ReadAt
            };
        }

        /// <summary>
        ///     How far through the division, 0…1 - or <c>null</c> where that question has no
        ///     answer: nothing read yet, no bound, or a bound of zero.
        ///     <para>
        ///         The share is computed and not stored, so that the two numbers stay the
        ///         truth and this stays a view of them. Clamped, because a game that hands
        ///         out more points than the division needs is a case for the ring to survive,
        ///         not to break on.
        ///     </para>
        /// </summary>
        [YamlIgnore]
        public double? RankProgress =>
            RankPoints is { } points && RankPointsMax is { } max && max > 0
                ? Math.Clamp(points / (double)max, 0.0, 1.0)
                : null;

        /// <summary>Display name of the rank, e.g. "Gold 3". Empty if no rank is set.</summary>
        public string RankName()
        {
            if (Tier == HotsRankTier.None) return "";
            return Tier.HasDivisions()
                ? $"{Tier.DisplayName()} {Division}"
                : Tier.DisplayName();
        }
    }
}
