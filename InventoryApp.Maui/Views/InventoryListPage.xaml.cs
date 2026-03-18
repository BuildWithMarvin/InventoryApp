using InventoryApp.Maui.ViewModels;

namespace InventoryApp.Maui.Views 
{
    /// <summary>
    /// The UI view displaying the list of all inventory items.
    /// </summary>
    public partial class InventoryListPage : ContentPage
    {
        private readonly InventoryListViewModel _viewModel;

        public InventoryListPage(InventoryListViewModel viewModel)
        {
            InitializeComponent();

            _viewModel = viewModel;
            BindingContext = _viewModel;
        }

        /// <summary>
        /// Triggered every time the page becomes visible to the user.
        /// Ensures the inventory list is always up to date by fetching the latest data from Azure.
        /// </summary>
        protected override void OnAppearing()
        {
            base.OnAppearing();

            // Execute the load command automatically without requiring a manual pull-to-refresh
            if (_viewModel.LoadProductsAzureCommand.CanExecute(null))
            {
                _viewModel.LoadProductsAzureCommand.Execute(null);
            }
        }
    }
}