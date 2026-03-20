using Microsoft.Extensions.Configuration;
using System.Text;
using System.Text.Json;
using InventoryApp.Maui.Models;
using System.Diagnostics;

   

    namespace InventoryApp.Maui.Services
    {
        public class ApiService
        {
            private readonly HttpClient _httpClient;
            private readonly string _apiUrl;
            private readonly string _employeeApiUrl;

            public ApiService(IConfiguration config)
            {
                _httpClient = new HttpClient();

                _apiUrl = config["ApiSettings:ProductsEndpointUrl"];
                _employeeApiUrl = config["ApiSettings:EmployeesEndpointsUrl"];

                if (string.IsNullOrWhiteSpace(_apiUrl))
                {
                    throw new Exception("Fatal Error: 'ProductsEndpointUrl' is null or empty. Please verify appsettings.json.");
                }

                if (string.IsNullOrWhiteSpace(_employeeApiUrl))
                {
                    throw new Exception("Fatal Error: 'EmployeesEndpointsUrl' is null or empty. Please verify appsettings.json.");
                }
            }

            public async Task<Product> GetProductByBarcodeAsync(string barcode)
            {
                var response = await _httpClient.GetAsync($"{_apiUrl}/barcode/{barcode}");

                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync();
                    return JsonSerializer.Deserialize<Product>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                }

                return null;
            }

            public async Task<List<Product>> GetProductsAsync()
            {
                var response = await _httpClient.GetAsync(_apiUrl);
                if (!response.IsSuccessStatusCode) return new List<Product>();

                var json = await response.Content.ReadAsStringAsync();

                // Note: PropertyNameCaseInsensitive is required because C# properties (PascalCase) 
                // usually differ from JSON payload standards (camelCase).
                return JsonSerializer.Deserialize<List<Product>>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            }

            public async Task<bool> AddProductAsync(Product newProduct)
            {
                var json = JsonSerializer.Serialize(newProduct);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var response = await _httpClient.PostAsync(_apiUrl, content);
                return response.IsSuccessStatusCode;
            }

            /// <summary>
            /// Updates an existing product. Throws an InvalidOperationException on concurrency conflicts (HTTP 409).
            /// </summary>
            public async Task<bool> UpdateProductAsync(Product product)
            {
                var json = JsonSerializer.Serialize(product);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var updateUrl = $"{_apiUrl}/{product.Id}";
                var response = await _httpClient.PutAsync(updateUrl, content);

                if (response.StatusCode == System.Net.HttpStatusCode.Conflict)
                {
                    string conflictDetails = await response.Content.ReadAsStringAsync();
                    throw new InvalidOperationException(conflictDetails);
                }

                if (!response.IsSuccessStatusCode)
                {
                    string errorDetails = await response.Content.ReadAsStringAsync();
                    throw new Exception($"HTTP {response.StatusCode} | ID: {product.Id} | Backend Error: {errorDetails}");
                }

                return true;
            }

            public async Task<Employee> LoginAsync(string pin)
            {
                var requestData = new { pinCode = pin?.Trim() };
                var requestJson = JsonSerializer.Serialize(requestData);
                var content = new StringContent(requestJson, Encoding.UTF8, "application/json");

                var response = await _httpClient.PostAsync($"{_employeeApiUrl}/login", content);
                var responseContent = await response.Content.ReadAsStringAsync();

                if (response.IsSuccessStatusCode)
                {
                    return JsonSerializer.Deserialize<Employee>(responseContent, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                }
                else
                {
                    Debug.WriteLine($"API Error {response.StatusCode}: {responseContent}");
                    return null;
                }
            }

            public async Task<bool> ChangePinAsync(int employeeId, string newPin)
            {
                var requestData = new
                {
                    EmployeeId = employeeId,
                    NewPin = newPin?.Trim()
                };

                var json = JsonSerializer.Serialize(requestData);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var response = await _httpClient.PostAsync($"{_employeeApiUrl}/change-pin", content);

                if (!response.IsSuccessStatusCode)
                {
                    var errorDetail = await response.Content.ReadAsStringAsync();
                    Debug.WriteLine($"API Error changing PIN {response.StatusCode}: {errorDetail}");
                }

                return response.IsSuccessStatusCode;
            }
        }
    }
}