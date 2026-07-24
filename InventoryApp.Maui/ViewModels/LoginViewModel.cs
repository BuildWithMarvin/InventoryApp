using InventoryApp.Maui.Services;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows.Input;
using Microsoft.Maui.Controls;

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

        private string _badgeBarcode;
        public string BadgeBarcode
        {
            get => _badgeBarcode;
            set
            {
                if (_badgeBarcode != value)
                {
                    _badgeBarcode = value;
                    OnPropertyChanged();
                   
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

      
        private async Task LoginAsync()
        {
            if (string.IsNullOrWhiteSpace(BadgeBarcode)) return;

            ErrorMessage = "Check ID...";

            
            var employee = await _apiService.LoginAsync(BadgeBarcode);

            if (employee != null)
            {
                App.CurrentEmployeeId = employee.Id;
                ErrorMessage = string.Empty;

                Application.Current.MainPage = new AppShell();
            }
            else
            {
                ErrorMessage = "Invalid employee ID card";
                BadgeBarcode = string.Empty;
            }
        }
    }
}
