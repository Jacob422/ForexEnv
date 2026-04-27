using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace ForexEnv.Services
{
    public class NbpRate
    {
        [JsonPropertyName("currency")]
        public string Currency { get; set; } = string.Empty;

        [JsonPropertyName("code")]
        public string Code { get; set; } = string.Empty;

        [JsonPropertyName("mid")]
        public decimal Mid { get; set; }
    }

    public class NbpTable
    {
        [JsonPropertyName("table")]
        public string Table { get; set; } = string.Empty;

        [JsonPropertyName("no")]
        public string No { get; set; } = string.Empty;

        [JsonPropertyName("effectiveDate")]
        public string EffectiveDate { get; set; } = string.Empty;

        [JsonPropertyName("rates")]
        public List<NbpRate> Rates { get; set; } = new();
    }

    public class NbpApiClient
    {
        private readonly HttpClient _httpClient;

        public NbpApiClient()
        {
            _httpClient = new HttpClient();
        }

        public async Task<List<NbpRate>> GetCurrentRatesAsync()
        {
            try
            {
                var response = await _httpClient.GetAsync("http://api.nbp.pl/api/exchangerates/tables/A/?format=json");
                response.EnsureSuccessStatusCode();

                var content = await response.Content.ReadAsStringAsync();
                var tables = JsonSerializer.Deserialize<List<NbpTable>>(content);

                return tables?.FirstOrDefault()?.Rates ?? new List<NbpRate>();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Błąd podczas pobierania kursów NBP: {ex.Message}");
                return new List<NbpRate>();
            }
        }
        
        public async Task<NbpRate?> GetRateAsync(string currencyCode)
        {
            var rates = await GetCurrentRatesAsync();
            return rates.FirstOrDefault(r => r.Code.Equals(currencyCode, StringComparison.OrdinalIgnoreCase));
        }
    }
}
