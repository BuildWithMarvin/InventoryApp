using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using InventoryApp.Maui.Services;

namespace InventoryApp.Maui.ViewModels
{
    public partial class InventoryListViewModel : ObservableObject
    {
        private readonly ApiService _apiService;

        // Cache for local search. This means we don't have to send a new request to the Azure API every time a letter is typed 
        // in the search bar..
        private List<Product> _allProducts = new();

        [ObservableProperty]
        private string title = "MyInventory";

        [ObservableProperty]
        private bool isRefreshing;

        [ObservableProperty]
        private string searchText;

        // List linked to the UI. Filled dynamically from _allProducts during search.
        public ObservableCollection<Product> Products { get; } = new();

        public InventoryListViewModel(ApiService apiService)
        {
            _apiService = apiService;
        }

        [RelayCommand]
        public async Task LoadProductsAzure()
        {
            IsRefreshing = true;

            try
            {
                // TODO (V2): For very large warehouses, switch to pagination .
                // Currently, we load everything into the mobile phone's memory at once.
                var productsFromDb = await _apiService.GetProducts();

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

        // Hook method from the MVVM Toolkit. Fires automatically as soon as SearchText changes.
        partial void OnSearchTextChanged(string value)
        {
            // Pure in-memory search (client-side filtering). Extremely fast because there is no network latency.
            if (string.IsNullOrWhiteSpace(value))
            {
                Products.Clear();
                foreach (var p in _allProducts) Products.Add(p);
                return;
            }

            var filtered = _allProducts.Where(p =>
                (p.Name != null && p.Name.Contains(value, StringComparison.OrdinalIgnoreCase)) ||
                (p.Description != null && p.Description.Contains(value, StringComparison.OrdinalIgnoreCase)) ||
                (p.Barcode != null && p.Barcode.Contains(value, StringComparison.OrdinalIgnoreCase))
            ).ToList();

            Products.Clear();
            foreach (var p in filtered) Products.Add(p);
        }

        [RelayCommand]
        public async Task ProductSelected(Product currentProduct)
        {
            // Sanity check: Intercept if the binding unexpectedly throws null from the UI.
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
                if (action == "Stock In (+)")
                {
                    currentProduct.Quantity += amount;
                }
                else if (action == "Stock Out (-)")
                {
                    currentProduct.Quantity -= amount;
                    if (currentProduct.Quantity < 0) currentProduct.Quantity = 0; // Kein Minusbestand
                }

                var success = await _apiService.UpdateProduct(currentProduct);

                if (success)
                {
                    await Shell.Current.DisplayAlert("Success", $"New stock: {currentProduct.Quantity}x", "OK");

                    // TODO for V2: We are currently reloading the entire list from the backend 
                    // just because ONE item has changed. Better: Only update the affected product in the local 
                    // _allProducts. However, this is sufficient for the MVP for now.
                    await LoadProductsAzure();
                }
                else
                {
                    await Shell.Current.DisplayAlert("Error", "Update failed on Azure!", "OK");
                }
            }
        }
    }
}