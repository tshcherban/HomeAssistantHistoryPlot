using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace WpfApp1
{
    public class HomeAssistantConnector
    {
        private readonly string HistoryBaseUrl;
        private readonly string AuthToken;

        public HomeAssistantConnector()
        {
            var cfgPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "ha.txt");
            var lines = File.ReadAllLines(cfgPath);

            HistoryBaseUrl = $"http://{lines[0]}/api/history/period";
            AuthToken = lines[1];
        }

        private static string FormatDate(DateTime dt) => dt.ToString("yyyy-MM-ddTHH:mm:ss.ffffff") + "+02:00";

        public List<DataPoint> GetItems(string entityId, DateTime startDate, DateTime endDate)
        {
            using (var httpClient = new HttpClient())
            {
                var startTime = FormatDate(startDate);
                var endTime = FormatDate(endDate.AddDays(1).AddTicks(-1));
                var resUrl = $"{HistoryBaseUrl}/{startTime}?filter_entity_id={entityId}&end_time={WebUtility.UrlEncode(endTime)}";
                using (var httpRequestMessage = new HttpRequestMessage(HttpMethod.Get, resUrl) {Headers = {Authorization = new AuthenticationHeaderValue("Bearer", AuthToken)}})
                {
                    return Task.Run(() =>
                    {
                        var resp = httpClient.SendAsync(httpRequestMessage).Result;
                        var jsonStr = resp.Content.ReadAsStringAsync().Result;
                        return JsonConvert.DeserializeObject<List<List<DataPoint>>>(jsonStr)[0];
                    }).Result;
                }
            }
        }
    }
}