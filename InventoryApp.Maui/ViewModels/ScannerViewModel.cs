using InventoryApp.Maui.Models;
using InventoryApp.Maui.Services;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;

using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows.Input;


namespace InventoryApp.Maui.ViewModels
{
    public partial class ScannerViewModel : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        private readonly ApiService _apiService;

        private bool _isDetecting = true;
        public bool IsDetecting
        {
            get => _isDetecting;
            set
            {
                if (_isDetecting != value)
                {
                    _isDetecting = value;
                    OnPropertyChanged();
                }
            }
        }

        private string _resultText = "Point the camera at a barcode...";
        public string ResultText
        {
            get => _resultText;
            set
            {
                if (_resultText != value)
                {
                    _resultText = value;
                    OnPropertyChanged();
                }
            }
        }

        private Color _resultColor = Colors.Black;
        public Color ResultColor
        {
            get => _resultColor;
            set
            {
                if (_resultColor != value)
                {
                    _resultColor = value;
                    OnPropertyChanged();
                }
            }
        }

        public ICommand ProcessBarcodeCommand { get; }

        public ScannerViewModel(ApiService apiService)
        {
            _apiService = apiService;
            ProcessBarcodeCommand = new Command<string>(async (barcode) => await ProcessBarcodeAsync(barcode));
        }

        /// <summary>
        /// Processes a scanned barcode. Checks if the product exists to update stock, 
        /// or prompts the user to create a new product if it is unknown.
        /// </summary>
        public async Task ProcessBarcodeAsync(string scannedBarcode)
        {
            // Pause the scanner to prevent duplicate triggers while processing
            IsDetecting = false;
            ResultText = $"Checking barcode {scannedBarcode}...";
            ResultColor = Colors.Orange;

            try
            {
                var currentProduct = await _apiService.GetProductByBarcodeAsync(scannedBarcode);

                if (currentProduct != null)
                {
                    // --- EXISTING PRODUCT: Update Stock ---
                    string action = await Shell.Current.DisplayActionSheet(
                        $"{currentProduct.Name} ({currentProduct.Quantity}x in stock)",
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
                            if (amount <= 0)
                            {
                                await Application.Current.MainPage.DisplayAlert("Error", "Please enter an amount greater than 0.", "OK");
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
                                    await Application.Current.MainPage.DisplayAlert("Stock Error", $"Not enough in stock! Only {currentProduct.Quantity} items remaining.", "OK");
                                    return;
                                }

                                currentProduct.Quantity -= amount;
                                currentProduct.LastUpdatedByEmployeeId = App.CurrentEmployeeId;
                            }

                            var success = await _apiService.UpdateProductAsync(currentProduct);

                            ResultText = success ? $"{currentProduct.Name} updated! New stock: {currentProduct.Quantity}" : "Error updating!";
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
                    // --- UNKNOWN PRODUCT: Create New ---
                    string newName = "";
                    while (string.IsNullOrWhiteSpace(newName))
                    {
                        newName = await Shell.Current.DisplayPromptAsync("Name (1/4)", "What is the name of this article? (required)", "Continue", "Cancel", "e.g. Coffee");
                        if (newName == null)
                        {
                            CancelScan("Scan canceled.");
                            return;
                        }
                    }

                    string description = "";
                    while (string.IsNullOrWhiteSpace(description))
                    {
                        description = await Shell.Current.DisplayPromptAsync("Description (2/4)", "Please enter a brief description:", "Continue", "Cancel", "e.g. Outdoor gear");
                        if (description == null)
                        {
                            CancelScan("Scan canceled.");
                            return;
                        }
                    }

                    string quantityStr = "";
                    int quantity = 0;
                    while (true)
                    {
                        quantityStr = await Shell.Current.DisplayPromptAsync("Quantity (3/4)", $"How many items of '{newName}' are in stock?", "Continue", "Cancel", "e.g. 5", keyboard: Keyboard.Numeric);
                        if (quantityStr == null)
                        {
                            CancelScan("Scan canceled.");
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
                            CancelScan("Scan canceled.");
                            return;
                        }

                        // Robust parsing for both '.' and ',' regardless of the device's regional settings
                        string normalizedPrice = priceStr.Replace(",", ".");
                        if (decimal.TryParse(normalizedPrice, NumberStyles.Number, CultureInfo.InvariantCulture, out price)) break;
                    }

                    var newProduct = new Product
                    {
                        Name = newName,
                        Description = description,
                        Quantity = quantity,
                        Price = price,
                        Barcode = scannedBarcode
                    };

                    var success = await _apiService.AddProductAsync(newProduct);

                    ResultText = success ? $"{newName} ({quantity}x) stored!" : "Error saving!";
                    ResultColor = success ? Colors.Green : Colors.Red;
                }
            }
            catch (Exception ex)
            {
                ResultText = $"Error: {ex.Message}";
                ResultColor = Colors.Red;
            }
            finally
            {
                // Wait briefly to let the user read the result, then reset the scanner
                await Task.Delay(3000);
                ResultText = "Point the camera at a barcode...";
                ResultColor = Colors.Black;
                IsDetecting = true;
            }
        }

        /// <summary>
        /// Helper method to cleanly reset the UI when a creation process is canceled.
        /// </summary>
        private void CancelScan(string message)
        {
            ResultText = message;
            ResultColor = Colors.Gray;
        }
    }
}