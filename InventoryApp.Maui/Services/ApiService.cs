using Microsoft.Extensions.Configuration;
using System.Text;
using System.Text.Json;

namespace InventoryApp.Maui.Services
{
    // Die Properties müssen exakt so heißen wie im JSON vom Azure-Backend.
    public class Product
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public int Quantity { get; set; }
        public decimal Price { get; set; }
        public string Barcode { get; set; }
    }

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
    }
}