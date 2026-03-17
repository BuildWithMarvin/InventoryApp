using InventoryApp.Maui.Services;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;

namespace InventoryApp.Maui.ViewModels
{
    // "partial" und "ObservableObject" sind weg, dafür klassisches Interface
    public class LoginViewModel : INotifyPropertyChanged
    {
        // --- 1. Vertrag für INotifyPropertyChanged ---
        public event PropertyChangedEventHandler PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        private readonly ApiService _apiService;

        // --- 2. Klassische Properties (Backing Fields + Getter/Setter) ---
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

                    // NEU: Auto-Login! Sobald 4 Zeichen getippt wurden, feuert der Command.
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

        // --- 3. Klassischer Command ---
        public ICommand LoginCommand { get; }

        // --- 4. Konstruktor ---
        public LoginViewModel(ApiService apiService)
        {
            _apiService = apiService;

            // Command initialisieren und mit der Methode verknüpfen
            LoginCommand = new Command(async () => await LoginAsync());
        }

        // --- 5. Logik (Unverändert, nur ohne Attribut) ---
        private async Task LoginAsync()
        {
            if (string.IsNullOrWhiteSpace(PinCode)) return;

            ErrorMessage = "Prüfe PIN...";

            int? employeeId = await _apiService.LoginAsync(PinCode);

            if (employeeId.HasValue)
            {
                App.CurrentEmployeeId = employeeId.Value;
                ErrorMessage = "";
                Application.Current.MainPage = new AppShell();
            }
            else
            {
                ErrorMessage = "❌ Falscher PIN-Code!";
                PinCode = string.Empty;
            }
        }
    }
}
