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
        // ========================================================
        // 1. INotifyPropertyChanged VERTRAG
        // ========================================================
        public event PropertyChangedEventHandler PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        private readonly ApiService _apiService;

        // Cache for local search.
        private List<Product> _allProducts = new();

        // List linked to the UI. Filled dynamically from _allProducts during search.
        public ObservableCollection<Product> Products { get; } = new();

        // ========================================================
        // 2. KLASSISCHE PROPERTIES
        // ========================================================

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

                    // NEU: Statt der Toolkit-Magie rufen wir die Suche hier explizit auf!
                    PerformSearch(_searchText);
                }
            }
        }

        // ========================================================
        // 3. KLASSISCHE COMMANDS
        // ========================================================
        public ICommand LoadProductsAzureCommand { get; }
        public ICommand ProductSelectedCommand { get; }

        // ========================================================
        // 4. KONSTRUKTOR
        // ========================================================
        public InventoryListViewModel(ApiService apiService)
        {
            _apiService = apiService;

            // Commands mit den Methoden verknüpfen
            LoadProductsAzureCommand = new Command(async () => await LoadProductsAzure());

            // Wichtig: Command<Product>, da das XAML das angeklickte Produkt übergibt
            ProductSelectedCommand = new Command<Product>(async (p) => await ProductSelected(p));
        }

        // ========================================================
        // 5. DEINE LOGIK (Unverändert)
        // ========================================================

        private async Task LoadProductsAzure()
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

        // Diese Methode ersetzt das alte 'OnSearchTextChanged'
        private void PerformSearch(string value)
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

        private async Task ProductSelected(Product currentProduct)
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
                // FAIL EARLY 1: Ist die Eingabe überhaupt eine gültige Zahl größer als 0?
                if (amount <= 0)
                {
                    await App.Current.MainPage.DisplayAlert("Fehler", "Bitte eine Menge größer als 0 eingeben.", "OK");
                    return; // Bricht die Methode hier sofort ab!
                }

                if (action == "Stock In (+)")
                {
                    currentProduct.Quantity += amount;
                    currentProduct.LastUpdatedByEmployeeId = App.CurrentEmployeeId;
                }
                else if (action == "Stock Out (-)")
                {
                    // FAIL EARLY 2: Reicht der aktuelle Bestand für diese Entnahme?
                    if (amount > currentProduct.Quantity)
                    {
                        await App.Current.MainPage.DisplayAlert("Bestandsfehler", $"Nicht genug auf Lager! Es sind nur noch {currentProduct.Quantity} Stück da.", "OK");
                        return; // Bricht die Methode hier sofort ab, ES WIRD NICHTS GESPEICHERT!
                    }

                    // Wenn wir hier ankommen, ist die Entnahme zu 100 % gültig.
                    currentProduct.Quantity -= amount;
                    currentProduct.LastUpdatedByEmployeeId = App.CurrentEmployeeId;
                }

                // Dieser API-Aufruf passiert jetzt NUR NOCH, wenn alle Daten sauber und logisch sind.
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