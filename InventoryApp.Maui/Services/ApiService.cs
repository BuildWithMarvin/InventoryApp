using System.Text;
using System.Text.Json;

namespace InventoryApp.Maui.Services
{
    // Unser Model (Der Bauplan)
    public class Product
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public int Quantity { get; set; }
        public decimal Price { get; set; }
        public string Barcode { get; set; }
    }

    // Unser Service (Der Briefträger zur API)
    public class ApiService
    {
        private readonly HttpClient _httpClient = new HttpClient();
        // ⚠️ DEINE ECHTE AZURE URL HIER EINTRAGEN:
        private readonly string _apiUrl = "https://inventoryapi-marvinfranke-ftdhfeh5ddhzfeav.westeurope-01.azurewebsites.net/api/products";

        public async Task<List<Product>> GetProductsAsync()
        {
            var response = await _httpClient.GetAsync(_apiUrl);
            if (!response.IsSuccessStatusCode) return new List<Product>();

            var json = await response.Content.ReadAsStringAsync();
            return JsonSerializer.Deserialize<List<Product>>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }

        public async Task<bool> AddProductAsync(Product newProduct)
        {
            var json = JsonSerializer.Serialize(newProduct);
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            var response = await _httpClient.PostAsync(_apiUrl, content);
            return response.IsSuccessStatusCode;
        }
    }
}
