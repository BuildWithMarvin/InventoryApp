namespace InventoryApp.Maui
{
    public partial class App : Application
    {
        // Wir lassen uns die LoginPage vom Dependency Injection System (MauiProgram) geben

        public static int CurrentEmployeeId { get; set; }
        public App(LoginPage loginPage)
        {
            InitializeComponent();

            // Architektur-Trick: Die allererste Seite ist jetzt der Login. 
            // Erst wenn der erfolgreich ist, setzen wir MainPage = new AppShell();
            MainPage = loginPage;
        }
    }
}
