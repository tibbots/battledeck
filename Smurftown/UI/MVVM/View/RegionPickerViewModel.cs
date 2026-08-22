using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MvvmDialogs;
using Smurftown.Backend.Entity;
using Smurftown.Backend.Texts;
using System.Windows.Input;

namespace Smurftown.UI.MVVM.View
{
    /// <summary>One offered region: the word, the two letters, and the pick.</summary>
    public sealed record RegionChoice(string Label, string ShortName, BattlenetRegion Region, ICommand Command);

    /// <summary>
    ///     Which region the client that is running is signed into.
    ///     <para>
    ///         <b>It exists because the game does not say.</b> Rank, heroes and currencies are
    ///         stored per region, the client is signed into exactly one - and on none of the
    ///         calibrated screens does it stand which. Searched for on 22.08.2026: the main menu
    ///         shows it nowhere, and the profile overlay does not either. So the human is asked,
    ///         because guessing would write a whole reading into the wrong region and nothing
    ///         about it would look wrong afterwards.
    ///     </para>
    ///     <para>
    ///         <b>It is not asked when there is nothing to ask.</b> An account that plays Heroes
    ///         of the Storm in exactly one region has already answered; this dialog only opens
    ///         from the second one onwards. Who decides that is
    ///         <c>RunningGame.ResolveRegion</c>, not this class.
    ///     </para>
    ///     <para>
    ///         <b>Cancelling is a valid answer</b> and writes nothing. That is the reason for the
    ///         third button: without it, whoever does not know would have to guess, and a guess
    ///         here is exactly what the dialog exists to prevent.
    ///     </para>
    /// </summary>
    public class RegionPickerViewModel : ObservableObject, IModalDialogViewModel
    {
        private bool? _dialogResult;

        public RegionPickerViewModel(string battletag, IReadOnlyList<BattlenetRegion> offered)
        {
            Question = Strings.Format("region.pickQuestion", battletag);
            var pick = new RelayCommand<BattlenetRegion>(Pick);
            Choices = offered
                .Select(region => new RegionChoice(
                    region.DisplayName(), region.ShortName(), region, pick))
                .ToList();
            CancelCommand = new RelayCommand(() => DialogResult = false);
        }

        /// <summary>Names the battletag - the answer depends on which account is meant.</summary>
        public string Question { get; }

        public IReadOnlyList<RegionChoice> Choices { get; }

        public ICommand CancelCommand { get; }

        /// <summary>
        ///     What was picked, or <c>null</c> when the dialog was cancelled. Read only after
        ///     <see cref="DialogResult" /> came back <c>true</c>.
        /// </summary>
        public BattlenetRegion? Picked { get; private set; }

        public bool? DialogResult
        {
            get => _dialogResult;
            private set
            {
                _dialogResult = value;
                OnPropertyChanged();
            }
        }

        private void Pick(BattlenetRegion region)
        {
            Picked = region;
            DialogResult = true;
        }
    }
}
