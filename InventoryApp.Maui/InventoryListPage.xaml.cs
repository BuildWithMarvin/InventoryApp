using InventoryApp.Maui.ViewModels;

namespace InventoryApp.Maui
{
    public partial class InventoryListPage : ContentPage
    {
        private readonly InventoryListViewModel _viewModel;

        public InventoryListPage(InventoryListViewModel viewModel)
        {
            InitializeComponent();
            _viewModel = viewModel;
            BindingContext = _viewModel; 
        }

        // Diese Methode wird JEDES MAL aufgerufen, wenn die Seite auf dem Bildschirm erscheint!
        protected override void OnAppearing()
        {
            base.OnAppearing();
            
            // Führe den Lade-Befehl aus dem ViewModel aus
            _viewModel.LoadProductsAzureCommand.Execute(null);
        }
    }
}