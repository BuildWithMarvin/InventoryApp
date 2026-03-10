using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using InventoryApp.Maui.Services;

namespace InventoryApp.Maui.ViewModels
{
    public partial class MainViewModel : ObservableObject
    {
        private readonly ApiService _apiService;

        // UI-State (is automatically converted into real properties by the MVVM Toolkit)
        [ObservableProperty]
        private string resultText = "Point the camera at a barcode....";

        [ObservableProperty]
        private Color resultColor = Colors.Black;

        [ObservableProperty]
        private bool isDetecting = true;

        public MainViewModel(ApiService apiService)
        {
            _apiService = apiService;
        }

        [RelayCommand]
        public async Task ProcessBarcodeAsync(string scannedBarcode)
        {
            // Pause the scanner immediately so that it does not fire the same barcode 50 times per second. 
            IsDetecting = false;
            ResultText = $"Check barcode {scannedBarcode}...";
            ResultColor = Colors.Orange;

            try
            {
                // TODO (V2): Convert to _apiService.GetProductByBarcode(scannedBarcode)!
                // Currently, we download the entire warehouse with every scan. This is acceptable for 50 items, but for 50,000 items, it significantly impacts performance.
                var products = await _apiService.GetProducts();
                var existingProduct = products?.FirstOrDefault(p => p.Barcode == scannedBarcode);

                if (existingProduct != null)
                {
                    // ==========================================
                    // SCENARIO A: PRODUKT BEKANNT -> BESTAND UPDATE
                    // ==========================================

                    string action = await Shell.Current.DisplayActionSheet(
                        $"{existingProduct.Name} ({existingProduct.Quantity}x in stock)",
                        "Cancel", null, "Stock In (+)", "Stock Out (-)");

                    if (action == "Cancel" || string.IsNullOrEmpty(action))
                    {
                        ResultText = "Action canceled.";
                        ResultColor = Colors.Gray;
                    }
                    else
                    {
                        string amountStr = await Shell.Current.DisplayPromptAsync(
                            action,
                            "How many items?",
                            "Save", "Cancel", "e.g. 1", keyboard: Keyboard.Numeric);

                        if (!string.IsNullOrWhiteSpace(amountStr) && int.TryParse(amountStr, out int amount))
                        {
                            if (action == "Stock In (+)")
                            {
                                existingProduct.Quantity += amount;
                            }
                            else if (action == "Stock Out (-)")
                            {
                                existingProduct.Quantity -= amount;
                                // Sanity check: No negative stock possible
                                if (existingProduct.Quantity < 0) existingProduct.Quantity = 0;
                            }

                            var success = await _apiService.UpdateProduct(existingProduct);

                            ResultText = success ? $"{existingProduct.Name} updated! New stock: {existingProduct.Quantity}" : "Error updating!";
                            ResultColor = success ? Colors.Green : Colors.Red;
                        }
                        else
                        {
                            ResultText = amountStr == null ? "Action canceled." : "Invalid number. Canceled.";
                            ResultColor = Colors.Gray;
                        }
                    }
                }
                else
                {

                    // SCENARIO B: UNKNOWN PRODUCT -> CREATE NEW


                    // We force the user to enter data using a while loop. 
                    // Half records in the database will only cause problems later on.

                    string newName = "";
                    while (string.IsNullOrWhiteSpace(newName))
                    {
                        newName = await Shell.Current.DisplayPromptAsync("Name (1/4)", "What is the name of this article? (required field!)", "Continue", "Cancel", "e.g. Coffee");
                        if (newName == null)
                        {
                            ResultText = "scan canceled.";
                            ResultColor = Colors.Gray;
                            return; // emergency exit
                        }
                    }

                    string description = "";
                    while (string.IsNullOrWhiteSpace(description))
                    {
                        description = await Shell.Current.DisplayPromptAsync("Description (2/4)", "Please enter a brief description:", "Continue", "Cancel", "e.g. outdoor-gear");
                        if (description == null)
                        {
                            ResultText = "scan canceled.";
                            ResultColor = Colors.Gray;
                            return;
                        }
                    }

                    string quantityStr = "";
                    int quantity = 0;
                    while (true)
                    {
                        quantityStr = await Shell.Current.DisplayPromptAsync("Quantity (3/4)", $"How often have we '{newName}'?", "Continue", "Cancel", "e.g. 5", keyboard: Keyboard.Numeric);
                        if (quantityStr == null)
                        {
                            ResultText = "scan canceled.";
                            ResultColor = Colors.Gray;
                            return;
                        }

                        if (int.TryParse(quantityStr, out quantity)) break;
                    }

                    string priceStr = "";
                    decimal price = 0.00m;
                    while (true)
                    {
                        priceStr = await Shell.Current.DisplayPromptAsync("Price (4/4)", "How much does one piece cost?", "Save", "Cancel", "e.g. 2.99", keyboard: Keyboard.Numeric);
                        if (priceStr == null)
                        {
                            ResultText = "scan canceled.";
                            ResultColor = Colors.Gray;
                            return;
                        }

                        // Pragmatic fix for the comma/full stop problem on different mobile phone keyboards (DE vs. EN)
                        if (decimal.TryParse(priceStr.Replace(".", ","), out price)) break;
                    }

                    var newProduct = new Product
                    {
                        Name = newName,
                        Description = description,
                        Quantity = quantity,
                        Price = price,
                        Barcode = scannedBarcode
                    };

                    var success = await _apiService.AddProduct(newProduct);

                    ResultText = success ? $"{newName} ({quantity}) stored!" : "error saving!";
                    ResultColor = success ? Colors.Green : Colors.Red;
                }
            }
            catch (Exception ex)
            {
                ResultText = $"Error: {ex.Message}";
                ResultColor = Colors.Red;
            }

            // Scanner reset: Give the user a moment to read the result before the scanner is reactivated.
            await Task.Delay(3000);
            ResultText = "Point the camera at a barcode....";
            ResultColor = Colors.Black;
            IsDetecting = true;
        }
    }
}