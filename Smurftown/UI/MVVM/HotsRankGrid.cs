using System.Windows;
using System.Windows.Input;
using Smurftown.Backend.Entity;

namespace Smurftown.UI.MVVM;

/// <summary>
///     The 28 selectable ranks, in the shape the grid is drawn in: two columns, six rows
///     each, five medals per row.
///     <para>
///         <b>It exists because there are two places that let a rank be picked</b>, and one
///         copy of the layout is the only way they cannot drift: the HotS tab of the edit
///         dialog and, since 23.08.2026, the medal in the account row. The same reasoning
///         as with <see cref="HotsReadout" /> - the read-out shared by two entrances - and
///         with <see cref="HotsRankImages" />, which is right next to this and keeps the
///         other half of the derivation.
///     </para>
///     <para>
///         <b>The list is rebuilt on every rank change</b> instead of being laid out once
///         and then mutated. That is deliberate: <see cref="HotsRankChoice" /> is an
///         immutable record without notification, and a static field would be shared
///         between the dialog and every open row - one would move the highlight of the
///         other. Rebuilding 28 records costs nothing.
///     </para>
/// </summary>
internal static class HotsRankGrid
{
    /// <summary>
    ///     The six rows, split across two columns of three each - Bronze to Gold on the
    ///     left, Platinum, Diamond and the division-less tiers on the right.
    ///     <para>
    ///         Split here and not in the XAML: a split that would arise there from two
    ///         <c>ItemsControl</c> with index arithmetic would silently be wrong on the next
    ///         change to the tier list. Here it stands as one line of code next to the list
    ///         it splits.
    ///     </para>
    /// </summary>
    public static IReadOnlyList<IReadOnlyList<IReadOnlyList<HotsRankChoice>>> Columns(
        HotsRankTier current, int currentDivision, ICommand command)
    {
        var rows = Rows(current, currentDivision, command);
        var half = (rows.Count + 1) / 2;
        return [rows.Take(half).ToList(), rows.Skip(half).ToList()];
    }

    /// <summary>Builds the six rows and marks the chosen entry in the process.</summary>
    private static IReadOnlyList<IReadOnlyList<HotsRankChoice>> Rows(
        HotsRankTier current, int currentDivision, ICommand command)
    {
        var rows = new List<IReadOnlyList<HotsRankChoice>>();

        // one row per tier, descending: 5 (lowest) on the left, 1 (highest) on the right
        foreach (var tier in new[]
                 {
                     HotsRankTier.Bronze, HotsRankTier.Silver, HotsRankTier.Gold,
                     HotsRankTier.Platinum, HotsRankTier.Diamond
                 })
        {
            var row = new List<HotsRankChoice>();
            for (var division = HotsRankTiers.LowestDivision;
                 division >= HotsRankTiers.HighestDivision;
                 division--)
                // Tier AND division must match - otherwise the whole row would light up.
                row.Add(new HotsRankChoice(tier, division, HotsRankImages.Display(tier, division),
                    tier == current && division == currentDivision, command));
            rows.Add(row);
        }

        // Division-less tiers plus "unplaced" in one last row. Here only the
        // tier counts: Master and Grand Master have no division, and "no rank" even less so.
        rows.Add(new List<HotsRankChoice>
        {
            new(HotsRankTier.Master, 0, HotsRankImages.Display(HotsRankTier.Master, 0),
                current == HotsRankTier.Master, command),
            new(HotsRankTier.GrandMaster, 0, HotsRankImages.Display(HotsRankTier.GrandMaster, 0),
                current == HotsRankTier.GrandMaster, command),
            new(HotsRankTier.None, 0, HotsRankImages.NoRank, current == HotsRankTier.None, command)
        });

        return rows;
    }
}

/// <summary>
///     A selectable rank in the grid. Division is 0 where the tier does not know one.
///     <para>
///         <c>IsSelected</c> carries the highlight. It sits in the record and not as a
///         comparison in the XAML, because there two values must match at once (tier AND
///         division) and a <c>MultiDataTrigger</c> over that would be harder to read than one
///         line in the ViewModel.
///     </para>
///     <para>
///         <b><c>Command</c> sits in the record for the same reason it does in
///         <c>StartOption</c>:</b> in the account row the grid hangs in a <c>Popup</c>, and a
///         popup lies outside the layout tree of the row - a <c>RelativeSource</c> walk out of
///         three nested <c>ItemsControl</c> would have to count levels and would break
///         silently at the next nesting. A field in the record cannot bind into nothing. The
///         dialog, where the grid sits inline and a walk would work, passes the same field
///         rather than keeping a second mechanism next to it.
///     </para>
/// </summary>
public sealed record HotsRankChoice(
    HotsRankTier Tier, int Division, string ImageSource, bool IsSelected, ICommand Command)
{
    /// <summary>Not chosen means dimmed - the same language as in the hero grid.</summary>
    public double Opacity => IsSelected ? 1.0 : 0.35;

    /// <summary>
    ///     The frame sits only on the chosen one. Blue like the tabs, because a rank has no
    ///     color of its own - unlike a hero, who carries its role color.
    /// </summary>
    public Visibility RingVisibility => IsSelected ? Visibility.Visible : Visibility.Collapsed;
}
