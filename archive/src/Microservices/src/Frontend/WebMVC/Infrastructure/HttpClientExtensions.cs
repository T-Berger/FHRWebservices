﻿using Newtonsoft.Json;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;

namespace WebMVC.Infrastructure
{
    public static class HttpClientExtensions
    {
        public static Task<HttpResponseMessage> PostAsJsonAsync<T>(this HttpClient client, string url, T obj)
        {
            return SendHelper(client, HttpMethod.Post, url, obj);
        }
        public static Task<HttpResponseMessage> PutAsJsonAsync<T>(this HttpClient client, string url, T obj)
        {
            return SendHelper(client, HttpMethod.Put, url, obj);
        }

        private static Task<HttpResponseMessage> SendHelper<T>(HttpClient client, HttpMethod method, string url, T obj)
        {
            var request = new HttpRequestMessage(method, url);
            var serialized = JsonConvert.SerializeObject(obj);
            var payload = new StringContent(serialized);
            payload.Headers.ContentType = new MediaTypeHeaderValue("application/json");
            request.Content = payload;

            return client.SendAsync(request);
        }

        public static async Task<T> ReadAsAsync<T>(this HttpContent content) where T : class
        {
            var rawJson = await content.ReadAsStringAsync();
            if (string.IsNullOrWhiteSpace(rawJson))
                return null;

            return JsonConvert.DeserializeObject<T>(rawJson);
        }
    }
}
