using InventoryApp.Maui.Views;


namespace InventoryApp.Maui
{
    public partial class App : Application
    {

        public static int CurrentEmployeeId { get; set; }
        public App(LoginPage loginPage)
        {
            InitializeComponent();

            

            MainPage = loginPage;
        }
    }
}
