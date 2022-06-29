using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace dcrpt_miner
{
    public class BambooNodeV1Api : IBambooNodeApi
    {
        private IHttpClientFactory HttpClientFactory { get; }
        private string Url { get; }
        private ILogger Logger { get; }

        public BambooNodeV1Api(IHttpClientFactory httpClientFactory, string url, ILoggerFactory loggerFactory)
        {
            if (string.IsNullOrEmpty(url))
            {
                throw new System.ArgumentException($"'{nameof(url)}' cannot be null or empty.", nameof(url));
            }

            if (loggerFactory is null)
            {
                throw new ArgumentNullException(nameof(loggerFactory));
            }

            HttpClientFactory = httpClientFactory ?? throw new System.ArgumentNullException(nameof(httpClientFactory));
            Url = url;
            Logger = loggerFactory.CreateLogger<BambooNodeV1Api>();

            Logger.LogDebug("Initialized BambooNodeV1Api");
        }

        public async Task<(bool success, uint block)> GetBlock()
        {
            try
            {
                var httpRequestMessage = new HttpRequestMessage(HttpMethod.Get, Url + "/block_count");

                using (var httpClient = HttpClientFactory.CreateClient())
                using (var httpResponseMessage = await httpClient.SendAsync(httpRequestMessage))
                {
                    var success = httpResponseMessage.IsSuccessStatusCode;
                    uint block = 0;

                    if (success)
                    {
                        using (var contentStream = await httpResponseMessage.Content.ReadAsStreamAsync())
                        {
                            block = await JsonSerializer.DeserializeAsync<uint>(contentStream);
                        }
                    }

                    return (success, block);
                }
            }
            catch (Exception ex)
            {
                Logger.LogDebug(ex, "GetBlock() failed");
                return (false, 0);
            }
        }

        public async Task<(bool success, MiningProblem data)> GetMiningProblem()
        {
            try
            {
                var httpRequestMessage = new HttpRequestMessage(HttpMethod.Get, Url + "/mine");

                using (var httpClient = HttpClientFactory.CreateClient())
                using (var httpResponseMessage = await httpClient.SendAsync(httpRequestMessage))
                {
                    var success = httpResponseMessage.IsSuccessStatusCode;
                    MiningProblem data = null;

                    if (success)
                    {
                        using (var contentStream = await httpResponseMessage.Content.ReadAsStreamAsync())
                        {
                            data = await JsonSerializer.DeserializeAsync<MiningProblem>(contentStream);
                        }
                    }

                    return (success, data);
                }
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "GetMiningProblem() failed");
                return (false, null);
            }
        }

        public async Task<(bool success, List<Transaction> data)> GetTransactions()
        {
            try
            {
                var httpRequestMessage = new HttpRequestMessage(HttpMethod.Get, Url + "/tx_json");

                using (var httpClient = HttpClientFactory.CreateClient())
                using (var httpResponseMessage = await httpClient.SendAsync(httpRequestMessage))
                {
                    var success = httpResponseMessage.IsSuccessStatusCode;
                    var data = new List<Transaction>();

                    if (success)
                    {
                        using (var contentStream = await httpResponseMessage.Content.ReadAsStreamAsync())
                        {
                            await foreach (var tx in JsonSerializer.DeserializeAsyncEnumerable<Transaction>(contentStream)) {
                                data.Add(tx);
                            }
                        }
                    }
                    return (success, data);
                }
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "GetTransactions() failed");
                return (false, new List<Transaction>());
            }
        }

        public async Task<bool> Submit(Stream stream)
        {
            try
            {
                var content = new StreamContent(stream);

                using (var httpClient = HttpClientFactory.CreateClient())
                using (var httpResponseMessage = await httpClient.PostAsync(Url + "/submit", content))
                {
                    if (httpResponseMessage.IsSuccessStatusCode)
                    {
                        var result = await httpResponseMessage.Content.ReadAsStringAsync();
                        Logger.LogError(result);
                        return result.Contains("SUCCESS");
                    }

                    return httpResponseMessage.IsSuccessStatusCode;
                }
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Submit() failed");
                return false;
            }
        }
    }
}
