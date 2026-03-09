using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using InventoryApp.Maui.Services;

namespace InventoryApp.Maui.ViewModels
{
    public partial class MainViewModel : ObservableObject
    {
        private readonly ApiService _apiService;

        // Diese Eigenschaften sind an die UI gebunden
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

        // Das ist der Befehl (Command), der beim Scannen ausgeführt wird
        [RelayCommand]
        public async Task ProcessBarcodeAsync(string scannedBarcode)
        {
            IsDetecting = false;
            ResultText = $"Check barcode {scannedBarcode}...";
            ResultColor = Colors.Orange;

            try
            {
                var products = await _apiService.GetProductsAsync();
                var existingProduct = products?.FirstOrDefault(p => p.Barcode == scannedBarcode);

                if (existingProduct != null)
                {
                    ResultText = $" Found: {existingProduct.Name} (Quantity: {existingProduct.Quantity})";
                    ResultColor = Colors.Green;
                }
                else
                {
                    // --- 1. NAME ABFRAGEN (Zwangsschleife) ---
                    string newName = "";
                    while (string.IsNullOrWhiteSpace(newName))
                    {
                        newName = await Shell.Current.DisplayPromptAsync("Name (1/4)", "What is the name of this article? (required field!)", "Continue", "Cancel", "e.g. Coffe");

                        if (newName == null) // User hat "Abbrechen" gedrückt
                        {
                            ResultText = "scan canceled.";
                            ResultColor = Colors.Gray;
                            return; // Beendet den gesamten Vorgang
                        }
                    }

                    // --- 2. BESCHREIBUNG ABFRAGEN (Zwangsschleife) ---
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

                    // --- 3. MENGE ABFRAGEN (Zwangsschleife mit Zahlen-Check) ---
                    string quantityStr = "";
                    int quantity = 0;
                    while (true) // Endlosschleife, bis eine GÜLTIGE Zahl eingegeben wird
                    {
                        quantityStr = await Shell.Current.DisplayPromptAsync("Quantity (3/4)", $"How often have we '{newName}'?", "Continue", "Cancel", "e.g. 5", keyboard: Keyboard.Numeric);

                        if (quantityStr == null)
                        {
                            ResultText = "scan canceled.";
                            ResultColor = Colors.Gray;
                            return;
                        }

                        // Prüfen, ob der Text wirklich eine Zahl ist
                        if (int.TryParse(quantityStr, out quantity))
                        {
                            break; // Gültige Zahl! Schleife erfolgreich verlassen.
                        }
                        // Falls er Buchstaben eingegeben hat, dreht sich die Schleife nochmal!
                    }

                    // --- 4. PREIS ABFRAGEN (Zwangsschleife mit Komma-Check) ---
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

                        // Punkt in Komma umwandeln, falls das Handy englisch/deutsch gemischt ist
                        if (decimal.TryParse(priceStr.Replace(".", ","), out price))
                        {
                            break; // Gültige Zahl! Schleife verlassen.
                        }
                    }

                    // --- ALLE DATEN GESAMMELT: AB IN DIE DATENBANK! ---
                    var newProduct = new Product
                    {
                        Name = newName,
                        Description = description, // <-- Die vom User eingegebene Beschreibung!
                        Quantity = quantity,
                        Price = price,
                        Barcode = scannedBarcode
                    };

                    var success = await _apiService.AddProductAsync(newProduct);

                    ResultText = success ? $"{newName} ({quantity}) gespeichert!" : "Fehler beim Speichern!";
                    ResultColor = success ? Colors.Green : Colors.Red;
                }
            }
            catch (Exception ex)
            {
                ResultText = $"❌ Fehler: {ex.Message}";
                ResultColor = Colors.Red;
            }

            await Task.Delay(3000); // 3 Sekunden Pause
            ResultText = "Halte die Kamera auf einen Barcode...";
            ResultColor = Colors.Black;
            IsDetecting = true;
        }
    }
}
