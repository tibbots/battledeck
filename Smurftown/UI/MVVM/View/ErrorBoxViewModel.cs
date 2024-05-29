using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MvvmDialogs;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Input;

namespace Smurftown.UI.MVVM.View
{
    public class ErrorBoxViewModel: ObservableObject, IModalDialogViewModel
    {

		private string _errorMessage;
        private bool? _dialogResult;

        public string ErrorMessage
		{
			get { return _errorMessage; }
			set { SetProperty(ref _errorMessage, value); }
		}

		public ErrorBoxViewModel(Exception error)
		{
			ErrorMessage = error.Message;
            OkCommand = new RelayCommand(Ok);
        }

        public ICommand OkCommand { get; }
      

        public bool? DialogResult
        {
            get => _dialogResult;
            private set
            {
                _dialogResult = value;
                OnPropertyChanged();
            }
        }

        private void Ok()
        {
            DialogResult = true;
        }

    }
}
