using Microsoft.Extensions.Configuration;
using System.Text;
using System.Text.Json;
using InventoryApp.Maui.Models;
using System.Diagnostics;


namespace InventoryApp.Maui.Services
{
    // Die Properties müssen exakt so heißen wie im JSON vom Azure-Backend.
   

    public class ApiService
    {
        private readonly HttpClient _httpClient;
        private readonly string _apiUrl;
        private readonly string _employeeApiUrl;

        public ApiService(IConfiguration config)
        {
            _httpClient = new HttpClient();

            // Load URL so that we can flexibly swap it in appsettings.json for different environments (test/live)
            _apiUrl = config["ApiSettings:ProductsEndpointUrl"];
            _employeeApiUrl = config["ApiSettings:EmployeesEndpointsUrl"];

            // Safety net: Better to let it pop right at the start, 
            // otherwise you'll be searching high and low for ‘invalid URI’ errors later on.
            if (string.IsNullOrWhiteSpace(_apiUrl))
            {
                throw new Exception("Fatal Error: 'ProductsEndpointUrl' is null or empty. Please verify your configuration source (e.g., appsettings.json keys and build action).");
            }

            if (string.IsNullOrWhiteSpace(_employeeApiUrl))
            {
                throw new Exception("Fatal Error: 'EmployeesEndpointsUrl' is null or empty. Please verify your configuration source (e.g., appsettings.json keys and build action).");
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

            // Important: Set CaseInsensitive to true! C# uses capital letters (Name), while JSON from APIs often uses lowercase letters (name) by default.
            // Without this, the lists will simply remain empty during deserialisation.
            return JsonSerializer.Deserialize<List<Product>>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }

        public async Task<bool> AddProductAsync(Product newProduct)
        {
            var json = JsonSerializer.Serialize(newProduct);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync(_apiUrl, content);
            return response.IsSuccessStatusCode;
        }

        public async Task<bool> UpdateProductAsync(Product product)
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

        /// <summary>
        /// Authenticates an employee via PIN and returns the employee details if successful.
        /// </summary>
        public async Task<Employee> LoginAsync(string pin)
        {
            var requestData = new { pinCode = pin?.Trim() };
            var requestJson = JsonSerializer.Serialize(requestData);
            var content = new StringContent(requestJson, Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync($"{_employeeApiUrl}/login", content);

            
            var responseContent = await response.Content.ReadAsStringAsync();

            if (response.IsSuccessStatusCode)
            {
                // If successful (200 OK): Convert the JSON into the Employee object
                return JsonSerializer.Deserialize<Employee>(responseContent, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            }
            else
            {
                // If an error occurs (400, 401, 404, 500, etc.): Log the actual error
                Debug.WriteLine($"API Fehler {response.StatusCode}: {responseContent}");
                return null;
            }
        }





        // A small helper object just for the login response
        private class EmployeeDto
        {
            public int Id { get; set; }
            public string Name { get; set; }
        }

        /// <summary>
        /// Updates the PIN for a specific employee.
        /// </summary>
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
                Debug.WriteLine($"API Fehler beim PIN-Wechsel {response.StatusCode}: {errorDetail}");
            }

            return response.IsSuccessStatusCode;
        }
    }
}