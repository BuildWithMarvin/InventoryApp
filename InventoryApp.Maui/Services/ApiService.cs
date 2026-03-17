using Microsoft.Extensions.Configuration;
using System.Text;
using System.Text.Json;
using InventoryApp.Maui.Models;


namespace InventoryApp.Maui.Services
{
    // Die Properties müssen exakt so heißen wie im JSON vom Azure-Backend.
   

    public class ApiService
    {
        private readonly HttpClient _httpClient;
        private readonly string _apiUrl;

        public ApiService(IConfiguration config)
        {
            _httpClient = new HttpClient();

            // Load URL so that we can flexibly swap it in appsettings.json for different environments (test/live)
            _apiUrl = config["ApiSettings:ProductsEndpointUrl"];

            // Safety net: Better to let it pop right at the start, 
            // otherwise you'll be searching high and low for ‘invalid URI’ errors later on.
            if (string.IsNullOrWhiteSpace(_apiUrl))
            {
                throw new Exception("Fatal Error: 'ProductsEndpointUrl' is null or empty. Please verify your configuration source (e.g., appsettings.json keys and build action).");
            }
        }

        public async Task<Product> GetProductByBarcodeAsync(string barcode)
        {
            // Wir fragen gezielt nur diesen einen Barcode an!
            var response = await _httpClient.GetAsync($"{_apiUrl}/barcode/{barcode}");

            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync();
                return JsonSerializer.Deserialize<Product>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            }

            // Wenn StatusCode 404 (NotFound) ist, geben wir null zurück (Produkt unbekannt)
            return null;
        }

        public async Task<List<Product>> GetProducts()
        {
            var response = await _httpClient.GetAsync(_apiUrl);
            if (!response.IsSuccessStatusCode) return new List<Product>();

            var json = await response.Content.ReadAsStringAsync();

            // Important: Set CaseInsensitive to true! C# uses capital letters (Name), while JSON from APIs often uses lowercase letters (name) by default.
            // Without this, the lists will simply remain empty during deserialisation.
            return JsonSerializer.Deserialize<List<Product>>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }

        public async Task<bool> AddProduct(Product newProduct)
        {
            var json = JsonSerializer.Serialize(newProduct);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync(_apiUrl, content);
            return response.IsSuccessStatusCode;
        }

        public async Task<bool> UpdateProduct(Product product)
        {
            var json = JsonSerializer.Serialize(product);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var updateUrl = $"{_apiUrl}/{product.Id}";
            var response = await _httpClient.PutAsync(updateUrl, content);

            // If Azure rejects the update (e.g. validation), read the real error reason and pass it on,
            // instead of simply ignoring it.
            if (!response.IsSuccessStatusCode)
            {
                string errorDetails = await response.Content.ReadAsStringAsync();
                throw new Exception($"HTTP {response.StatusCode} | ID: {product.Id} | Backend Error: {errorDetails}");
            }



            return true;
        }
        // --- POST: Mitarbeiter-Login ---
        public async Task<int?> LoginAsync(string pinCode)
        {
            // Wir verpacken den PIN als JSON-String ("1234")
            var content = new StringContent($"\"{pinCode}\"", Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync($"{_apiUrl.Replace("/products", "")}/employees/login", content);

            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync();

                // Wir lesen das Employee-Objekt aus (wir brauchen ein kurzes Hilfs-DTO dafür)
                var employee = JsonSerializer.Deserialize<EmployeeDto>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                return employee?.Id; // Gibt die Datenbank-ID des Mitarbeiters zurück
            }

            return null; // Login fehlgeschlagen (Falscher PIN)
        }

        // Ein kleines Hilfs-Objekt nur für den Login-Response
        private class EmployeeDto
        {
            public int Id { get; set; }
            public string Name { get; set; }
        }
    }
}