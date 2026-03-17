using InventoryApp.Maui.Models;
using InventoryApp.Maui.Services;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;

namespace InventoryApp.Maui.ViewModels
{
    public partial class MainViewModel : INotifyPropertyChanged
    {
        
        public event PropertyChangedEventHandler PropertyChanged;
        private readonly ApiService _apiService;

        // UI-State (is automatically converted into real properties by the MVVM Toolkit)
        

        protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        private bool _isDetecting = true;
        public bool IsDetecting
        {
            get => _isDetecting;
            set
            {
                if (_isDetecting != value)
                {
                    _isDetecting = value;
                    OnPropertyChanged(); // Meldet der UI die Änderung
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
       

        public MainViewModel(ApiService apiService)
        {
            _apiService = apiService;

            // Commands mit den MAUI-Standard-Befehlen verknüpfen
            ProcessBarcodeCommand = new Command<string>(async (barcode) => await ProcessBarcodeAsync(barcode));
            
        }
        public async Task ProcessBarcodeAsync(string scannedBarcode)
        {
            // Pause the scanner immediately so that it does not fire the same barcode 50 times per second. 
            IsDetecting = false;
            ResultText = $"Check barcode {scannedBarcode}...";
            ResultColor = Colors.Orange;

            try
            {
                var currentProduct = await _apiService.GetProductByBarcodeAsync(scannedBarcode);

                if (currentProduct != null)
                {
                    // ==========================================
                    // SCENARIO A: PRODUKT BEKANNT -> BESTAND UPDATE
                    // ==========================================

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
            finally
            {
                // ==========================================
                // FINALLY: Wird IMMER ausgeführt!
                // Egal ob Fehler, erfolgreicher Scan oder Abbruch durch den User.
                // ==========================================

                // Gib dem User 2-3 Sekunden Zeit, das Ergebnis ("scan canceled." oder "saved!") zu lesen
                await Task.Delay(3000);

                // Scanner & UI wieder scharfschalten
                ResultText = "Point the camera at a barcode....";
                ResultColor = Colors.Black;
                IsDetecting = true;
            }
 }
    }
}