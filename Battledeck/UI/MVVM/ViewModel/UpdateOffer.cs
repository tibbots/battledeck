using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Serilog;
using Battledeck.Backend.Texts;
using Battledeck.Backend.Update;
using ToastNotifications.Messages;

namespace Battledeck.UI.MVVM.ViewModel
{
    /// <summary>
    ///     Which of the four states the offer is in. Deliberately not four booleans: those
    ///     can be two at once, and then the chip says one thing and does another.
    /// </summary>
    public enum UpdateDisplay
    {
        /// <summary>Nothing found. The chip shows the running version and nothing else.</summary>
        None,

        /// <summary>A newer version exists and the chip acts on it.</summary>
        Ready,

        /// <summary>Downloading, verifying, installing. Nothing accepts a click while it runs.</summary>
        Busy,

        /// <summary>
        ///     It went wrong. The version stays on screen, but what is offered now is only
        ///     the release page - what failed here is unlikely to work on a second press.
        /// </summary>
        Failed
    }

    /// <summary>
    ///     Everything the human sees about versions: the chip in the top right corner of the
    ///     header, its flyout, and the <c>ABOUT &amp; UPDATES</c> card at the foot of the
    ///     settings tab.
    ///     <para>
    ///         <b>One object for two places, and that is the whole point of the class.</b>
    ///         Until now this state sat in <c>MainViewModel</c>, where it had exactly one
    ///         place on screen. It has two since the settings card exists, and two copies of
    ///         a state machine drift: an installation started from the settings would leave
    ///         the chip showing an offer that is already being installed.
    ///     </para>
    ///     <para>
    ///         Hand-written singleton, like <see cref="UpdateGateway" /> and the other
    ///         gateways - no holder, no container, the same pattern as everywhere here. The
    ///         <c>SettingsViewModel</c> is built on the first visit to the tab and would
    ///         otherwise start a second one.
    ///     </para>
    /// </summary>
    public sealed class UpdateOffer : ObservableObject
    {
        /// <summary>
        ///     How often the clock is asked - <b>not</b> how often GitHub is asked. That
        ///     interval is <see cref="UpdateGateway.Interval" /> and is an hour; this tick only
        ///     compares two time stamps and returns.
        ///     <para>
        ///         <b>Ten minutes and not sixty, and the difference is the whole reason the
        ///         number is not the interval.</b> A tick every hour lands against an hourly
        ///         deadline it misses by whatever the start-up took - the check then comes due
        ///         seconds after the tick that just decided against it, and the next answer is
        ///         an hour late. At ten minutes the worst case is ten minutes late, and the
        ///         five ticks in between cost a subtraction each.
        ///     </para>
        /// </summary>
        private static readonly TimeSpan Tick = TimeSpan.FromMinutes(10);

        public static readonly UpdateOffer Instance = new();

        private DispatcherTimer? _timer;

        private UpdateDisplay _display = UpdateDisplay.None;
        private string _offered = "";
        private string _step = "";
        private double _fraction = -1;
        private string _failure = "";
        private bool _checking;
        private bool _open;

        private InstallRoute _route = InstallRoute.Unknown;
        private bool _routeKnown;

        private RelayCommand? _openCommand;
        private RelayCommand? _closeCommand;
        private RelayCommand? _primaryCommand;
        private RelayCommand? _pageCommand;
        private RelayCommand? _checkCommand;

        private UpdateOffer()
        {
            // Every text here is computed rather than bound to a key, so after a language
            // switch all of them are stale. An empty name makes WPF reread the lot - the same
            // construction as in the two ViewModels, and for the same reason: a list of
            // individual names is incomplete by the next addition.
            Strings.Changed += Notify;
        }

        // ---------------------------------------------------------------- the chip ------

        public UpdateDisplay Display => _display;

        /// <summary>The running build, always shown - <c>v2.1.0</c>.</summary>
        public string Current => $"v{AppVersion.Current}";

        /// <summary>
        ///     The offered version with the arrow in front of it, so that chip reads
        ///     <c>v2.1.0 → 2.2.0</c>. Empty while there is nothing to offer.
        ///     <para>
        ///         <b>Both numbers stand there, not just the new one.</b> The old notice put
        ///         the offered version into a button and the running one into a label beside
        ///         it, with nothing saying that the two had anything to do with each other.
        ///     </para>
        /// </summary>
        public string OfferedText => _offered.Length == 0 ? "" : $" \u2192 {_offered}";

        public Visibility OfferedVisibility =>
            _offered.Length > 0 ? Visibility.Visible : Visibility.Collapsed;

        /// <summary>
        ///     What is currently happening to the offer - the percentage during the download,
        ///     the failure afterwards. Empty in the two quiet states.
        /// </summary>
        public string Trailer => _display switch
        {
            UpdateDisplay.Busy => $" \u00B7 {_step}",
            UpdateDisplay.Failed => $" \u00B7 {Strings.Current["update.failedShort"]}",
            _ => ""
        };

        public Visibility TrailerVisibility =>
            Trailer.Length > 0 ? Visibility.Visible : Visibility.Collapsed;

        /// <summary>
        ///     How far the download is, 0 to 1 - and less than zero for the steps that have no
        ///     length, see <see cref="UpdateProgress" />. The bar behind the text hides itself
        ///     for those instead of standing still at a number it made up.
        /// </summary>
        public double Fraction => _fraction < 0 ? 0 : _fraction;

        public Visibility FillVisibility =>
            _display == UpdateDisplay.Busy && _fraction >= 0 ? Visibility.Visible : Visibility.Collapsed;

        // ---------------------------------------------------------------- the flyout ----

        /// <summary>
        ///     Whether the panel under the chip is open. It stays shut in
        ///     <see cref="UpdateDisplay.None" />: a panel that says "no update" is a panel
        ///     nobody opens twice.
        /// </summary>
        public bool IsOpen
        {
            get => _open;
            private set
            {
                if (value == _open) return;
                _open = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(OpenVisibility));
            }
        }

        public Visibility OpenVisibility => _open ? Visibility.Visible : Visibility.Collapsed;

        public bool CanOpen => _display != UpdateDisplay.None;

        /// <summary>
        ///     Opens, and never toggles. A toggle looks right and is wrong here: the backdrop
        ///     behind the panel already closes it on any click outside, and it swallows the
        ///     click on the chip along with it - so a toggling command would close the panel
        ///     and reopen it in the same gesture.
        /// </summary>
        public ICommand OpenCommand => _openCommand ??= new RelayCommand(() => IsOpen = true);

        public ICommand CloseCommand => _closeCommand ??= new RelayCommand(() => IsOpen = false);

        public string Heading => Strings.Format("update.available", _offered);

        /// <summary>
        ///     The sentence under the heading, and it says what the button will DO:
        ///     <c>installHint</c> where this build can replace itself, <c>pageHint</c> where
        ///     it cannot - carrying the reason, so the human reads why they have to do it by
        ///     hand - and <c>failedHint</c> after something went wrong.
        /// </summary>
        public string Body => _display switch
        {
            UpdateDisplay.Failed => Strings.Format("update.failedHint", _failure),
            UpdateDisplay.Busy => _step,
            _ => Installable
                ? Strings.Format("update.installHint", _offered)
                : Strings.Format("update.pageHint", _offered, RouteReason())
        };

        public string PrimaryLabel =>
            Installable && _display != UpdateDisplay.Failed
                ? Strings.Current["update.install"]
                : Strings.Current["update.openPage"];

        public bool PrimaryEnabled => _display is UpdateDisplay.Ready or UpdateDisplay.Failed;

        public ICommand PrimaryCommand =>
            _primaryCommand ??= new RelayCommand(async void () => await Run());

        public ICommand PageCommand => _pageCommand ??= new RelayCommand(OpenReleasePage);

        public string LastCheckLine => Strings.Format("update.lastCheckLine", LastCheck);

        // ------------------------------------------------------- the settings card ------

        public string Installed => AppVersion.Current;

        /// <summary>The offered version, or the sentence that there is none.</summary>
        public string LatestText =>
            _offered.Length > 0 ? _offered : Strings.Current["update.upToDate"];

        /// <summary>The two buttons in that row exist only while something is offered.</summary>
        public Visibility ActionVisibility =>
            _offered.Length > 0 ? Visibility.Visible : Visibility.Collapsed;

        /// <summary>
        ///     When GitHub was last asked. <b>The first place this is ever shown</b> - the
        ///     value has been in <c>update.yaml</c> since the check existed and was visible to
        ///     nobody.
        /// </summary>
        public string LastCheck
        {
            get
            {
                if (_checking) return Strings.Current["update.checking"];

                var when = UpdateGateway.Instance.LastCheck;
                if (when == default) return Strings.Current["update.never"];

                // The time alone for today, date and time for anything older. A full time
                // stamp on something that happened twenty minutes ago is a date nobody reads.
                var local = when.ToLocalTime();
                return local.Date == DateTimeOffset.Now.Date
                    ? Strings.Format("update.today", local.ToString("t", CultureInfo.CurrentCulture))
                    : local.ToString("g", CultureInfo.CurrentCulture);
            }
        }

        public bool CanCheck => !_checking && _display != UpdateDisplay.Busy;

        public ICommand CheckCommand => _checkCommand ??= new RelayCommand(async void () => await CheckNow());

        /// <summary>
        ///     Whether this installation may replace its own <c>.exe</c> - and where it stands
        ///     that it may not. That answer used to exist only as a symbol on a button and a
        ///     sentence in a tooltip.
        /// </summary>
        public string SelfUpdateText => Installable
            ? Strings.Current["update.selfUpdateYes"]
            : Strings.Format("update.selfUpdateNo", RouteReason());

        /// <summary>
        ///     The folder the running <c>.exe</c> stands in. It answers the question by
        ///     itself: whoever reads <c>C:\Program Files\…</c> there understands without
        ///     further text why the application is not allowed to overwrite itself.
        /// </summary>
        public string InstallFolder
        {
            get
            {
                var exe = Environment.ProcessPath;
                return string.IsNullOrEmpty(exe) ? "" : Path.GetDirectoryName(exe) ?? "";
            }
        }

        // ---------------------------------------------------------------- the work -------

        /// <summary>
        ///     Can this build replace itself?
        ///     <para>
        ///         <b>Cached, and that is not premature</b>: <see cref="UpdateInstaller.Route" />
        ///         answers the write question by actually writing a probe file into the
        ///         installation folder, because there is no reliable way to ask. Half a dozen
        ///         bindings read this, and <c>OnPropertyChanged(string.Empty)</c> after a
        ///         language switch makes WPF reread all of them - unguarded it would put a
        ///         file into <c>Program Files</c> on every one of those.
        ///     </para>
        /// </summary>
        private bool Installable => Route == InstallRoute.Replace;

        private InstallRoute Route
        {
            get
            {
                if (_routeKnown) return _route;

                _route = UpdateInstaller.Route();
                _routeKnown = true;
                return _route;
            }
        }

        /// <summary>
        ///     Looks for a newer version - first in what the last run noted, then, if the hour
        ///     is up, on the network. Called at startup and by <see cref="Watch" /> afterwards,
        ///     never awaited: the window must not wait for a request.
        ///     <para>
        ///         <b>Callable as often as one likes.</b> Whether it costs a request is decided
        ///         by <see cref="UpdateGateway.Due" />, and a run in progress is safe from it -
        ///         <see cref="ShowOffer" /> refuses to touch the display while the state is
        ///         <see cref="UpdateDisplay.Busy" /> or <see cref="UpdateDisplay.Failed" />.
        ///     </para>
        /// </summary>
        /// <summary>
        ///     Looks once now and keeps looking for as long as the application is open.
        ///     <para>
        ///         <b>Why this exists at all</b>: until 23.08.2026 the check ran at start and
        ///         nowhere else, on the reasoning that a session rarely lasts long enough for a
        ///         timer to fire. That reasoning held for a daily interval and stopped holding
        ///         for an hourly one - a window that has been open since morning would
        ///         otherwise show the state it had at the moment it opened, and this
        ///         application is one people leave open.
        ///     </para>
        ///     <para>
        ///         <b>It says nothing when it finds something</b>, deliberately: the chip in the
        ///         header changes and that is all. A toast in the middle of somebody's afternoon
        ///         would be the application interrupting them over a version they can install
        ///         whenever they like.
        ///     </para>
        ///     <para>
        ///         <c>DispatcherTimer</c> like <see cref="RunningGame" />: the tick continues on
        ///         the UI thread, which is where the properties behind the chip are read from.
        ///         Started once - a second call is ignored rather than opening a second timer,
        ///         because this is a singleton with two callers' worth of temptation.
        ///     </para>
        /// </summary>
        public void Watch()
        {
            // The first look is not the timer's - it has to happen now, so the chip carries
            // the version the last run noted before the first frame is drawn.
            _ = Look();

            if (_timer != null) return;

            _timer = new DispatcherTimer { Interval = Tick };
            _timer.Tick += (_, _) => _ = Look();
            _timer.Start();
        }

        public async Task Look()
        {
            try
            {
                var updates = UpdateGateway.Instance;

                // Out of the file, so the chip stands with the first frame. It costs nothing
                // and is right on the overwhelming majority of starts - a release does not
                // appear between two of them.
                ShowOffer(updates.LatestVersion);

                await updates.CheckIfDue();

                // Again, and from the same place: the check may have found something newer -
                // or found that the line above was stale, because the human installed by hand
                // in the meantime. One comparison answers both.
                ShowOffer(updates.LatestVersion);
            }
            catch (Exception e)
            {
                // Nobody asked for this, so nobody is told. GithubReleases already swallows
                // its own failures; what can still arrive here is a broken state file or a
                // path that does not exist, and neither may take the window down with it.
                Log.Warning(e, "The update check did not run");
            }
        }

        /// <summary>
        ///     The button in the settings. It does not switch the daily check off or on -
        ///     there is no such switch, see <c>docs/self-update.md</c> - it only brings the
        ///     next check forward.
        /// </summary>
        private async Task CheckNow()
        {
            if (_checking) return;

            _checking = true;
            Notify();

            try
            {
                var release = await UpdateGateway.Instance.Check();
                _checking = false;
                ShowOffer(release?.Version ?? UpdateGateway.Instance.LatestVersion);
            }
            catch (Exception e)
            {
                // LOUD, unlike the check at startup: somebody pressed a button and is waiting
                // for an answer. Saying nothing would leave them looking at a line that never
                // stops saying "asking".
                _checking = false;
                Log.Error(e, "The update check failed");
                Notify();
                Dialogs.Toast.ShowError(Strings.Format("update.failed", e.Message));
            }
        }

        private void ShowOffer(string version)
        {
            // A run in progress owns the display. Without this line the check finishing in
            // the background would overwrite the progress text with the version again.
            if (_display is UpdateDisplay.Busy or UpdateDisplay.Failed) return;

            var newer = AppVersion.IsNewerThanCurrent(version);
            _offered = newer ? version : "";
            _display = newer ? UpdateDisplay.Ready : UpdateDisplay.None;

            if (!newer) IsOpen = false;

            Notify();
        }

        private async Task Run()
        {
            // Again, and this is the reading that decides. Between the offer appearing and
            // this click, the folder may have lost its write permission - and finding that
            // out after a 34 MB download would be the wrong moment.
            _route = UpdateInstaller.Route();
            _routeKnown = true;

            if (_display == UpdateDisplay.Failed || !Installable)
            {
                OpenReleasePage();
                return;
            }

            IsOpen = false;
            _display = UpdateDisplay.Busy;
            _step = Strings.Current["update.preparing"];
            _fraction = -1;
            Notify();

            try
            {
                // The release out of this session if the check found one, otherwise fetched
                // now: the version out of the file carries no download URLs, and a start on
                // which the day was not yet up never asked GitHub at all.
                var release = UpdateGateway.Instance.Pending ?? await UpdateGateway.Instance.Check();

                if (release == null)
                {
                    // Up to date after all - somebody installed by hand, or the release was
                    // withdrawn. Not an error; the offer simply goes.
                    _display = UpdateDisplay.None;
                    _offered = "";
                    Notify();
                    return;
                }

                var progress = new Progress<UpdateProgress>(step =>
                {
                    _step = step.Text;
                    _fraction = step.Fraction;
                    OnPropertyChanged(nameof(Trailer));
                    OnPropertyChanged(nameof(Fraction));
                    OnPropertyChanged(nameof(FillVisibility));
                    OnPropertyChanged(nameof(Body));
                });

                var exe = await UpdateInstaller.Install(release, progress, CancellationToken.None);

                // UseShellExecute STAYS FALSE, and that is not a detail: only then does the
                // new process inherit this one's environment - SMURFTOWN_HOME included. With
                // it true, an update triggered from a test run would restart against the real
                // account list.
                Log.Information("Restarting into {Exe}", exe);
                Process.Start(new ProcessStartInfo(exe) { UseShellExecute = false });
                Application.Current.Shutdown();
            }
            catch (Exception e)
            {
                // LOUD, unlike the check. The human pressed a button and waited, and now has
                // the version they started with - saying nothing would leave them looking at
                // a chip that will not go back to normal.
                Log.Error(e, "The update could not be installed");
                _failure = e.Message;
                _display = UpdateDisplay.Failed;
                _fraction = -1;
                Notify();
                Dialogs.Toast.ShowError(Strings.Format("update.failed", e.Message));
            }
        }

        private void OpenReleasePage()
        {
            IsOpen = false;
            var url = UpdateGateway.Instance.Pending?.PageUrl ?? GithubReleases.ReleasesPage;

            try
            {
                // UseShellExecute, because a URL is not a program: without it .NET tries to
                // start "https://..." as an executable and throws. The opposite of the line
                // in Run, and for the opposite reason.
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            }
            catch (Exception e)
            {
                Log.Error(e, "The release page could not be opened: {Url}", url);
                Dialogs.Toast.ShowError(Strings.Format("update.failed", e.Message));
            }
        }

        private string RouteReason()
        {
            return Strings.Current[Route switch
            {
                InstallRoute.DevBuild => "update.reasonDevBuild",
                InstallRoute.NotWritable => "update.reasonNotWritable",
                _ => "update.reasonUnknown"
            }];
        }

        /// <summary>
        ///     Two dozen properties hang off one state, and they are reported as one rather
        ///     than listed at each of the five places that change it - a forgotten name would
        ///     leave the chip showing something other than what it does. The empty name is
        ///     cheap here because nothing behind these getters touches disk or network;
        ///     <see cref="Installable" /> is the one that would, and it is cached.
        /// </summary>
        private void Notify()
        {
            OnPropertyChanged(string.Empty);
        }
    }
}
