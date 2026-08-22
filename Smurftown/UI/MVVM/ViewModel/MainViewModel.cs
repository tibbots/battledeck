using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MvvmDialogs;
using Serilog;
using Smurftown.UI.MVVM.View;
using System.Windows.Input;
using System.Security.Principal;
using System.Windows;
using ToastNotifications.Messages;
using Smurftown.Backend.Texts;

namespace Smurftown.UI.MVVM.ViewModel
{
    class MainViewModel : ObservableObject
    {
        private object? _currentView;

        public MainViewModel()
        {
            RegisterExceptionHandler();

            // The settings are NOT applied here, but in App.OnStartup - before
            // the first window, because the language of the UI depends on it. The reasoning
            // is there.

            AccountsVM = new AccountsViewModel();
            CurrentView = AccountsVM;

            // The corner top right computes its text instead of binding a key, so after a
            // language switch it would keep the old wording. An empty name makes WPF reread
            // everything at once - the same construction as in SettingsViewModel, and for the
            // same reason: a list of individual names is incomplete by the next addition.
            Strings.Changed += () => OnPropertyChanged(string.Empty);

            // NOT awaited, and that is the point: the window must not wait for a network
            // request. What can be shown without one - the version the last run found - is
            // already on screen when this returns, the rest arrives a moment later.
            _ = UpdateOffer.Instance.Look();

            // Starts the poll for a running game client, and hands over the dialog owner in
            // the same call: the region question needs a ViewModel whose view carries
            // md:DialogServiceViews.IsRegistered, and RunningGame is nobody's DataContext -
            // it hangs in the header of THIS window.
            RunningGame.Instance.Watch(this);
        }

        /// <summary>
        ///     The version chip in the top right corner of the header, and everything behind
        ///     it. <b>Not state of this ViewModel</b>: the same object drives the
        ///     <c>ABOUT &amp; UPDATES</c> card in the settings, and an installation started
        ///     over there has to reach this corner - see <see cref="UpdateOffer" />.
        /// </summary>
        public UpdateOffer Update => UpdateOffer.Instance;

        /// <summary>
        ///     The chip left of the version, and the flow behind it: a Heroes of the Storm
        ///     client is running - read the account signed into it, without signing it out.
        ///     <b>Not state of this ViewModel</b> either: the account rows take its busy flag
        ///     so that two runs cannot click into the same client at once.
        /// </summary>
        public RunningGame Running => RunningGame.Instance;

        /// <summary>
        ///     Only built on the first visit. The settings ViewModel's constructor scans
        ///     the usual installation locations - cheap, but no reason to do it on every start
        ///     when nobody is looking.
        /// </summary>
        private SettingsViewModel? _settingsVM;

        public ICommand ShowAccountsCommand => _showAccounts ??= new RelayCommand(() => Show(AccountsVM));

        public ICommand ShowSettingsCommand =>
            _showSettings ??= new RelayCommand(() => Show(_settingsVM ??= new SettingsViewModel()));

        private RelayCommand? _showAccounts;
        private RelayCommand? _showSettings;

        /// <summary>
        ///     Which tab is currently active. The bar has two <c>ToggleButton</c>, and they
        ///     have the same trap as the game symbols of the filter bar: a click un-checks the
        ///     button itself <b>before</b> the binding writes the value here. That is why
        ///     <see cref="NotifyTabs" /> snaps it back instead of accepting a <c>false</c> -
        ///     there is no deselecting a tab, one is always open.
        /// </summary>
        public bool AccountsTabActive
        {
            get => ReferenceEquals(_currentView, AccountsVM);
            set
            {
                if (value) Show(AccountsVM);
                else NotifyTabs();
            }
        }

        public bool SettingsTabActive
        {
            get => _settingsVM != null && ReferenceEquals(_currentView, _settingsVM);
            set
            {
                if (value) Show(_settingsVM ??= new SettingsViewModel());
                else NotifyTabs();
            }
        }

        private void Show(object view)
        {
            CurrentView = view;
            NotifyTabs();
        }

        private void NotifyTabs()
        {
            OnPropertyChanged(nameof(AccountsTabActive));
            OnPropertyChanged(nameof(SettingsTabActive));
        }

        private void RegisterExceptionHandler()
        {
            if (Application.Current != null)
            {
                Application.Current.DispatcherUnhandledException += App_DispatcherUnhandledException;
              
            }
        }
        /// <summary>
        ///     Every unhandled exception of the UI ends up here. The app practically never
        ///     crashes because of it - it swallows.
        ///     <para>
        ///         <b>Log first, then toast.</b> The toast only shows
        ///         <c>Message</c>, and that is gone as soon as it is hidden: no type, no
        ///         stack trace, no timestamp. On 20.08.2026 that exact thing cost an hour -
        ///         the edit button of the account row threw a
        ///         <c>ViewNotRegisteredException</c>, because during the rebuild onto the row layout
        ///         <c>md:DialogServiceViews.IsRegistered</c> had dropped out of <c>AccountCardView.xaml</c>,
        ///         and nothing about it stood in the log.
        ///     </para>
        ///     <para>
        ///         The order is deliberate: if the toast itself goes wrong, the reason
        ///         still stands in the file already.
        ///     </para>
        /// </summary>
        private void App_DispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
        {
            Log.Error(e.Exception, "Unhandled exception in the UI");
            //ShowErrorDialog(viewModel => Dialogs.DialogService.ShowDialog(this, viewModel), e.Exception);
            // The exception text itself stays ENGLISH and is only framed. It is the same
                // string that goes into the log one line above, and a log
                // in four wordings would no longer be searchable. Without the translated
                // frame there would stand an English sentence with no context at all - with it
                // the application at least says in the language of the human WHAT happened.
                Dialogs.Toast.ShowError(Strings.Format("error.unexpected", e.Exception.Message));
            //Dialogs.DialogService.ShowMessageBox(this, caption: "An error occured", messageBoxText: e.Exception.Message);
            //MessageBox.Show($"{e.Exception.Message}", "Application Error", MessageBoxButton.OK, MessageBoxImage.Error);
            e.Handled = true; // Prevent the application from crashing
        }

        private void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            // Log and/or display the exception
            if (e.ExceptionObject is Exception ex)
            {
                MessageBox.Show($"Unhandled domain exception: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void TaskScheduler_UnobservedTaskException(object sender, UnobservedTaskExceptionEventArgs e)
        {
            // Log and/or display the exception
            MessageBox.Show($"Unobserved task exception: {e.Exception.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            e.SetObserved(); // Prevent the exception from terminating the application
        }

        public AccountsViewModel AccountsVM { get; set; }

        public object? CurrentView
        {
            get { return _currentView; }
            set
            {
                _currentView = value;
                OnPropertyChanged();
            }
        }

        private void ShowErrorDialog(Func<ErrorBoxViewModel, bool?> showDialog, Exception error)
        {
            var dialogViewModel = new ErrorBoxViewModel(error);
           showDialog(dialogViewModel); 
        }
    }
}