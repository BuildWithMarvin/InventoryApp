using InventoryApp.Maui.Services;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;


namespace InventoryApp.Maui.ViewModels
{
    public class LoginViewModel : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        private readonly ApiService _apiService;

        private string _pinCode;
        public string PinCode
        {
            get => _pinCode;
            set
            {
                if (_pinCode != value)
                {
                    _pinCode = value;
                    OnPropertyChanged();

                    // Auto-submit when exactly 4 digits are entered
                    if (_pinCode?.Length == 4)
                    {
                        LoginCommand.Execute(null);
                    }
                }
            }
        }

        private string _errorMessage;
        public string ErrorMessage
        {
            get => _errorMessage;
            set
            {
                if (_errorMessage != value)
                {
                    _errorMessage = value;
                    OnPropertyChanged();
                }
            }
        }

        public ICommand LoginCommand { get; }

        public LoginViewModel(ApiService apiService)
        {
            _apiService = apiService;
            LoginCommand = new Command(async () => await LoginAsync());
        }

        /// <summary>
        /// Attempts to authenticate the user with the entered PIN.
        /// Handles the forced PIN change flow if required by the backend.
        /// </summary>
        private async Task LoginAsync()
        {
            if (string.IsNullOrWhiteSpace(PinCode)) return;

            ErrorMessage = "Verifying PIN...";

            var employee = await _apiService.LoginAsync(PinCode);

            if (employee != null)
            {
                if (employee.MustChangePin)
                {
                    string newPin = await Application.Current.MainPage.DisplayPromptAsync(
                        "New PIN Required",
                        "Please set your personal, secret 4-digit PIN now:",
                        "Save", "Cancel",
                        "e.g., 9988",
                        maxLength: 4,
                        keyboard: Keyboard.Numeric);

                    if (string.IsNullOrWhiteSpace(newPin) || newPin.Length != 4)
                    {
                        ErrorMessage = "PIN change cancelled or invalid.";
                        PinCode = string.Empty;
                        return;
                    }

                    bool success = await _apiService.ChangePinAsync(employee.Id, newPin);

                    if (!success)
                    {
                        ErrorMessage = "Error saving the new PIN.";
                        PinCode = string.Empty;
                        return;
                    }

                    await Application.Current.MainPage.DisplayAlert("Success", "Your PIN has been changed successfully. You will now be logged in.", "OK");
                }

                App.CurrentEmployeeId = employee.Id;
                ErrorMessage = string.Empty;

                
                Application.Current.MainPage = new AppShell();
            }
            else
            {
                ErrorMessage = "Invalid PIN code!";
                PinCode = string.Empty;
            }
        }
    }
}
