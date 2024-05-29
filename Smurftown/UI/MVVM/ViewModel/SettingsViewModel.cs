using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Serilog;
using Smurftown.Backend.Automation;
using Smurftown.Backend.Entity;
using Smurftown.Backend.Gateway;
using Smurftown.Backend.Texts;
using ToastNotifications.Messages;

namespace Smurftown.UI.MVVM.ViewModel
{
    /// <summary>
    ///     The settings tab.
    ///     <para>
    ///         <b>Saved immediately</b>, there is no save button. Same pattern as with the rank
    ///         and the hero picker: the selection moves straight on, there is no cancel. With two
    ///         settings a button would be a step you could forget without noticing.
    ///     </para>
    /// </summary>
    internal class SettingsViewModel : ObservableObject
    {
        private static readonly SettingsGateway _settings = SettingsGateway.Instance;

        private CancellationTokenSource? _scan;
        private RelayCommand? _scanCommand;
        private RelayCommand? _stopScanCommand;
        private string _scanStatus = "";
        private bool _scanning;
        private string? _selectedPath;
        private GameLanguage _selectedLanguage;
        private AppLanguage _selectedAppLanguage;
        private InputSpeed _selectedSpeed;

        public SettingsViewModel()
        {
            _selectedSpeed = _settings.InputSpeed;
            _selectedLanguage = _settings.ClientLanguage;
            _selectedAppLanguage = _settings.AppLanguage;

            // After a language change EVERY label on this tab is stale - the hint texts as much
            // as the entries of the selection lists. An empty name makes WPF report all
            // properties at once; listing them one by one would be a list that is incomplete
            // by the next addition.
            //
            // The subscription runs as long as the application does: this ViewModel is created
            // on the first visit to the tab and is never thrown away again.
            Strings.Changed += () => OnPropertyChanged(string.Empty);

            // On opening only the usual locations - that costs fractions of a second. The full
            // scan runs only on button press, because it takes minutes.
            foreach (var path in GameInstallations.Likely()) HotsPaths.Add(path);

            var stored = _settings.HotsPath;
            if (stored.Length > 0 && !HotsPaths.Contains(stored, StringComparer.OrdinalIgnoreCase))
            {
                // The stored path is included in the list even when the search does not find
                // it - otherwise the display would be empty although something is set.
                HotsPaths.Insert(0, stored);
            }

            _selectedPath = stored.Length > 0 ? stored : HotsPaths.FirstOrDefault();
        }

        /// <summary>Everything that was found, plus the stored path.</summary>
        public ObservableCollection<string> HotsPaths { get; } = [];

        /// <summary>
        ///     The ABOUT &amp; UPDATES card at the foot of this tab. <b>The same object the
        ///     chip in the header hangs on</b>, so an installation started here shows its
        ///     progress up there too - see <see cref="UpdateOffer" />.
        /// </summary>
        public UpdateOffer Update => UpdateOffer.Instance;

        /// <summary>
        ///     The three speed levels with their label. As a record and not a bare enum, because
        ///     a <c>ComboBox</c> would otherwise display the enum name and WPF cannot call a
        ///     method to translate it.
        /// </summary>
        /// <remarks>
        ///     <b>Computed and not assigned once</b>, ever since the labels are translated: a
        ///     list filled with <c>=</c> would carry the language that was current when the
        ///     ViewModel was built, and would keep it after a switch. The selection is not lost
        ///     in the process - the list is bound via <c>SelectedValuePath</c> to the enum value
        ///     and not to the record.
        /// </remarks>
        public IReadOnlyList<SpeedChoice> Speeds =>
        [
            new(InputSpeed.Slow, InputSpeed.Slow.DisplayName()),
            new(InputSpeed.Normal, InputSpeed.Normal.DisplayName()),
            new(InputSpeed.Fast, InputSpeed.Fast.DisplayName())
        ];

        /// <summary>
        ///     The client languages, records for the same reason as the speed levels: WPF cannot
        ///     call a method to translate an enum name.
        /// </summary>
        /// <remarks>
        ///     Generated from the enumeration and not listed by hand, now that there are five
        ///     versions: a hand-maintained list would have exactly one place to forget on the
        ///     next addition - and the version would then exist in the code, but not be
        ///     selectable.
        /// </remarks>
        public IReadOnlyList<LanguageChoice> Languages { get; } =
            Enum.GetValues<GameLanguage>()
                .Select(language => new LanguageChoice(language, language.DisplayName()))
                .ToArray();

        /// <summary>
        ///     The languages of the interface. Generated from the enumeration, for the same
        ///     reason as <see cref="Languages" />.
        /// </summary>
        /// <remarks>
        ///     <b>Not called <c>AppLanguages</c></b>: that is already the name of the static
        ///     class with the extension methods, and a property of the same name would shadow
        ///     it in this file. "Interface" is also the word that appears on screen and separates
        ///     this setting from the client language above it.
        ///     <para>
        ///         It is assigned once and not computed, unlike <see cref="Speeds" />: the names
        ///         of the languages are in their own language and precisely do not change on
        ///         switching.
        ///     </para>
        /// </remarks>
        public IReadOnlyList<AppLanguageChoice> InterfaceLanguages { get; } =
            Enum.GetValues<AppLanguage>()
                .Select(language => new AppLanguageChoice(language, language.DisplayName()))
                .ToArray();

        public string? SelectedPath
        {
            get => _selectedPath;
            set
            {
                if (value == _selectedPath) return;
                _selectedPath = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(PathState));
                Store();
            }
        }

        public InputSpeed SelectedSpeed
        {
            get => _selectedSpeed;
            set
            {
                if (value == _selectedSpeed) return;
                _selectedSpeed = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(SpeedHint));
                Store();
            }
        }

        /// <summary>
        ///     The language the game client runs in. It affects text recognition exclusively -
        ///     the calibration is language-independent, an English client does not shift a
        ///     single anchor.
        /// </summary>
        public GameLanguage SelectedLanguage
        {
            get => _selectedLanguage;
            set
            {
                if (value == _selectedLanguage) return;
                _selectedLanguage = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(LanguageHint));
                Store();
            }
        }

        /// <summary>
        ///     What is going on with the chosen path. A set path that points into nothing is the
        ///     case that must be visible - otherwise it only surfaces at the next game start.
        /// </summary>
        /// <summary>
        ///     The language Smurftown itself speaks.
        ///     <para>
        ///         <b>It takes effect immediately</b>, without a restart: <c>Store</c> saves and
        ///         calls <c>SettingsGateway.Apply</c>, and that reloads the dictionary via
        ///         <see cref="Strings.Use" /> and reports the change - whereupon every XAML
        ///         binding rereads. An open dialog does not follow along; harmless, because the
        ///         settings tab is open when switching, and no dialog is.
        ///     </para>
        ///     <para>
        ///         <b>There is deliberately NO separate <c>Strings.Use</c> here.</b> One stood
        ///         here once, and then the dictionary loaded twice on every change - once from
        ///         here, once via <c>Apply</c>. Same effect, but two places doing the same thing,
        ///         and two lines in the log per switch. The path via the gateway is also the
        ///         correct one: if saving fails, <c>Apply</c> is never reached, and the interface
        ///         stays on the language that is also in the file.
        ///     </para>
        /// </summary>
        public AppLanguage SelectedAppLanguage
        {
            get => _selectedAppLanguage;
            set
            {
                if (value == _selectedAppLanguage) return;
                _selectedAppLanguage = value;
                OnPropertyChanged();
                Store();
            }
        }

        public string PathState
        {
            get
            {
                if (string.IsNullOrEmpty(_selectedPath))
                    return Strings.Current["settings.pathMissing"];

                return File.Exists(_selectedPath) ? "" : Strings.Current["settings.pathGone"];
            }
        }

        public string SpeedHint =>
            Strings.Current[$"settings.speedHint.{_selectedSpeed.ToString().ToLowerInvariant()}"];

        public string LanguageHint => Strings.Current[_selectedLanguage == GameLanguage.English
            ? "settings.clientLanguageUnverified"
            : "settings.clientLanguageMeasured"];

        public string ScanStatus
        {
            get => _scanStatus;
            private set
            {
                if (value == _scanStatus) return;
                _scanStatus = value;
                OnPropertyChanged();
            }
        }

        public bool Scanning
        {
            get => _scanning;
            private set
            {
                if (value == _scanning) return;
                _scanning = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(ScanVisibility));
                OnPropertyChanged(nameof(StopVisibility));
            }
        }

        public Visibility ScanVisibility => _scanning ? Visibility.Collapsed : Visibility.Visible;
        public Visibility StopVisibility => _scanning ? Visibility.Visible : Visibility.Collapsed;

        public ICommand ScanCommand => _scanCommand ??= new RelayCommand(async void () => await ScanAll());

        public ICommand StopScanCommand => _stopScanCommand ??= new RelayCommand(() => _scan?.Cancel());

        /// <summary>
        ///     Scan all fixed drives. Runs in the background and reports where it currently is -
        ///     a full scan takes minutes, and a silent interface looks like a crashed one during
        ///     that time.
        /// </summary>
        private async Task ScanAll()
        {
            if (_scanning) return;

            _scan = new CancellationTokenSource();
            Scanning = true;
            ScanStatus = "Scanning...";

            try
            {
                var progress = new Progress<string>(where => ScanStatus = Strings.Format("settings.scanning", where));
                var token = _scan.Token;
                var found = await Task.Run(() => GameInstallations.ScanAll(progress, token), token);

                foreach (var path in found)
                {
                    if (!HotsPaths.Contains(path, StringComparer.OrdinalIgnoreCase)) HotsPaths.Add(path);
                }

                ScanStatus = found.Count switch
                {
                    0 => Strings.Current["settings.scanNone"],
                    1 => Strings.Current["settings.scanOne"],
                    _ => Strings.Format("settings.scanMany", found.Count)
                };

                // Only adopt when nothing was set yet - changing a current selection because a
                // scan found something else first would be a surprise.
                if (string.IsNullOrEmpty(_selectedPath)) SelectedPath = HotsPaths.FirstOrDefault();
            }
            catch (OperationCanceledException)
            {
                ScanStatus = "Scan stopped.";
            }
            catch (Exception e)
            {
                Log.Error(e, "Full scan failed");
                ScanStatus = Strings.Format("settings.scanFailed", e.Message);
            }
            finally
            {
                Scanning = false;
                _scan?.Dispose();
                _scan = null;
            }
        }

        private void Store()
        {
            try
            {
                _settings.Save(new Settings
                {
                    HotsPath = _selectedPath ?? "",
                    InputSpeed = _selectedSpeed,
                    ClientLanguage = _selectedLanguage,
                    AppLanguage = _selectedAppLanguage
                });
            }
            catch (Exception e)
            {
                // The error is already in the log, the gateway does that. Here it becomes
                // noticeable to the human, instead of quietly vanishing.
                Dialogs.Toast.ShowError(Strings.Format("settings.notSaved", e.Message));
            }
        }
    }

    /// <summary>A speed level, as the selection list needs it.</summary>
    public sealed record SpeedChoice(InputSpeed Value, string Label);

    /// <summary>A client language, as the selection list needs it.</summary>
    public sealed record LanguageChoice(GameLanguage Value, string Label);

    /// <summary>An interface language, as the selection list needs it.</summary>
    public sealed record AppLanguageChoice(AppLanguage Value, string Label);
}
