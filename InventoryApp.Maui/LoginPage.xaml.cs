using InventoryApp.Maui.ViewModels;

namespace InventoryApp.Maui
{
    public partial class LoginPage : ContentPage
    {
        public LoginPage(LoginViewModel viewModel)
        {
            InitializeComponent();
            BindingContext = viewModel;
        }
    }
}