namespace Smurftown.Backend.Entity
{
    /// <summary>
    ///     The fields the account list can be sorted by. <see cref="LastRead" /> and
    ///     <see cref="Name" /> apply to every game; <see cref="Rank" />, <see cref="Gold" /> and
    ///     <see cref="HeroesRead" /> only mean anything for Heroes of the Storm - the same
    ///     "hidden means without effect" rule as the hero and rank filters, see
    ///     <c>AccountsViewModel.SortFieldOptions</c>.
    /// </summary>
    public enum AccountSortField
    {
        LastRead,
        Name,
        Rank,
        Gold,
        HeroesRead
    }
}
