using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using InventoryApp.Maui.Services;
using InventoryApp.Maui.Models;

namespace InventoryApp.Maui.ViewModels
{
    public class InventoryListViewModel : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        private readonly ApiService _apiService;
        private readonly List<Product> _allProducts = new();

        public ObservableCollection<Product> Products { get; } = new();

        private string _title = "MyInventory";
        public string Title
        {
            get => _title;
            set
            {
                if (_title != value)
                {
                    _title = value;
                    OnPropertyChanged();
                }
            }
        }

        private bool _isRefreshing;
        public bool IsRefreshing
        {
            get => _isRefreshing;
            set
            {
                if (_isRefreshing != value)
                {
                    _isRefreshing = value;
                    OnPropertyChanged();
                }
            }
        }

        private string _searchText;
        public string SearchText
        {
            get => _searchText;
            set
            {
                if (_searchText != value)
                {
                    _searchText = value;
                    OnPropertyChanged();
                    PerformSearch(_searchText);
                }
            }
        }

        public ICommand LoadProductsAzureCommand { get; }
        public ICommand ProductSelectedCommand { get; }

        public InventoryListViewModel(ApiService apiService)
        {
            _apiService = apiService;
            LoadProductsAzureCommand = new Command(async () => await LoadProductsAzureAsync());
            ProductSelectedCommand = new Command<Product>(async (p) => await ProductSelectedAsync(p));
        }

        /// <summary>
        /// Fetches all products from the backend and updates the local collection.
        /// </summary>
        private async Task LoadProductsAzureAsync()
        {
            IsRefreshing = true;

            try
            {
                // Note: For future scaling, consider implementing pagination.
                var productsFromDb = await _apiService.GetProductsAsync();

                _allProducts.Clear();
                Products.Clear();

                if (productsFromDb != null)
                {
                    foreach (var product in productsFromDb)
                    {
                        _allProducts.Add(product);
                        Products.Add(product);
                    }
                }

                SearchText = string.Empty;
            }
            catch (Exception ex)
            {
                await Shell.Current.DisplayAlert("Error", $"Could not load data: {ex.Message}", "OK");
            }
            finally
            {
                IsRefreshing = false;
            }
        }

        /// <summary>
        /// Filters the currently loaded products based on the search input.
        /// </summary>
        private void PerformSearch(string searchTerm)
        {
            if (string.IsNullOrWhiteSpace(searchTerm))
            {
                Products.Clear();
                foreach (var p in _allProducts)
                {
                    Products.Add(p);
                }
                return;
            }

            var filtered = _allProducts.Where(p =>
                (p.Name != null && p.Name.Contains(searchTerm, StringComparison.OrdinalIgnoreCase)) ||
                (p.Description != null && p.Description.Contains(searchTerm, StringComparison.OrdinalIgnoreCase)) ||
                (p.Barcode != null && p.Barcode.Contains(searchTerm, StringComparison.OrdinalIgnoreCase))
            ).ToList();

            Products.Clear();
            foreach (var p in filtered)
            {
                Products.Add(p);
            }
        }

        /// <summary>
        /// Handles the stock in/out process when a user selects a product.
        /// </summary>
        private async Task ProductSelectedAsync(Product currentProduct)
        {
            if (currentProduct == null) return;

            string action = await Shell.Current.DisplayActionSheet(
                $"{currentProduct.Name} ({currentProduct.Quantity}x in stock)",
                "Cancel", null, "Stock In (+)", "Stock Out (-)");

            if (action == "Cancel" || string.IsNullOrEmpty(action)) return;

            string amountStr = await Shell.Current.DisplayPromptAsync(
                action,
                "How many items?",
                "Save", "Cancel", "e.g. 1", keyboard: Keyboard.Numeric);

            if (!string.IsNullOrWhiteSpace(amountStr) && int.TryParse(amountStr, out int amount))
            {
                if (amount <= 0)
                {
                    await App.Current.MainPage.DisplayAlert("Error", "Please enter an amount greater than 0.", "OK");
                    return;
                }

                if (action == "Stock In (+)")
                {
                    currentProduct.Quantity += amount;
                    currentProduct.LastUpdatedByEmployeeId = App.CurrentEmployeeId;
                }
                else if (action == "Stock Out (-)")
                {
                    if (amount > currentProduct.Quantity)
                    {
                        await App.Current.MainPage.DisplayAlert("Stock Error", $"Not enough in stock! Only {currentProduct.Quantity} items remaining.", "OK");
                        return;
                    }

                    currentProduct.Quantity -= amount;
                    currentProduct.LastUpdatedByEmployeeId = App.CurrentEmployeeId;
                }

                try
                {
                    // Attempt to push the updated stock to the API
                    await _apiService.UpdateProductAsync(currentProduct);

                    await Shell.Current.DisplayAlert("Success", $"New stock: {currentProduct.Quantity}x", "OK");
                    await LoadProductsAzureAsync();
                }
                catch (InvalidOperationException ex)
                {
                    // Handle HTTP 409 Conflict: Another employee modified this item in the meantime.
                    await Shell.Current.DisplayAlert("Conflict", ex.Message, "OK");

                    // Reload the list to display the most recent data from the server.
                    await LoadProductsAzureAsync();
                }
                catch (Exception ex)
                {
                    // Catch generic network or server errors.
                    await Shell.Current.DisplayAlert("Error", $"Update failed: {ex.Message}", "OK");
                }
            }
        }
    }
}
