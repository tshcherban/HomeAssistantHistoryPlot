using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace WpfApp1
{
    public class DataReader
    {
        private readonly string _historyBaseUrl;
        private readonly string _authToken;
        private readonly string _gasDataUrl;

        private DataReader(string haAddress, string haToken, string gasDataUrl)
        {
            _historyBaseUrl = $"http://{haAddress}/api/history/period";
            _authToken = haToken;
            _gasDataUrl = gasDataUrl;
        }

        public static DataReader GetNew(string haAddress, string haToken, string gasDataUrl)
        {
            return new DataReader(haAddress, haToken, gasDataUrl);
        }

        private static string FormatDate(DateTime dt) => dt.ToString("yyyy-MM-ddTHH:mm:ss.ffffff") + "+02:00";

        public async Task<List<DataPoint>> GetItems(string entityId, DateTime startDate, DateTime endDate)
        {
            using (var httpClient = new HttpClient())
            {
                var startTime = FormatDate(startDate);
                var endTime = FormatDate(endDate.AddDays(1).AddTicks(-1));
                var resUrl = $"{_historyBaseUrl}/{startTime}?filter_entity_id={entityId}&end_time={WebUtility.UrlEncode(endTime)}";
                using (var httpRequestMessage = new HttpRequestMessage(HttpMethod.Get, resUrl) {Headers = {Authorization = new AuthenticationHeaderValue("Bearer", _authToken)}})
                {
                    var resp = await httpClient.SendAsync(httpRequestMessage);
                    var jsonStr = await resp.Content.ReadAsStringAsync();
                    return JsonConvert.DeserializeObject<List<List<DataPoint>>>(jsonStr)[0];
                }
            }
        }

        public async Task<List<(DateTime date, double value)>> GetGasConsumption(DateTime minDate, DateTime maxDate)
        {
            var ret = new List<(DateTime date, double value)>();

            using (var httpClient = new HttpClient())
            {
                using (var httpRequestMessage = new HttpRequestMessage(HttpMethod.Get, _gasDataUrl))
                {
                    var resp = await httpClient.SendAsync(httpRequestMessage);
                    var jsonStr = await resp.Content.ReadAsStringAsync();

                    var data = JsonConvert.DeserializeObject<JArray>(jsonStr);
                    foreach (var dd in data.Skip(1))
                    {
                        var dateStr = dd[0].Value<string>();
                        if (string.IsNullOrEmpty(dateStr))
                            continue;

                        var date = DateTime.ParseExact(dateStr, "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
                        var value = dd[1].Value<double>();

                        if (date >= minDate && date <= maxDate)
                            ret.Add((date, value));
                    }
                }
            }

            return ret;
        }
    }
}