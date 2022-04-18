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
                var httpRequestMessage = new HttpRequestMessage(HttpMethod.Get, Url + "/gettx");

                using (var httpClient = HttpClientFactory.CreateClient())
                using (var httpResponseMessage = await httpClient.SendAsync(httpRequestMessage))
                {
                    var success = httpResponseMessage.IsSuccessStatusCode;
                    var data = new List<Transaction>();

                    if (success)
                    {
                        using (var contentStream = await httpResponseMessage.Content.ReadAsStreamAsync())
                        {
                            byte[] bytes;

                            using (BinaryReader br = new BinaryReader(contentStream))
                            {
                                bytes = br.ReadBytes((int)contentStream.Length);
                            }

                            var txSize = Marshal.SizeOf<TransactionInfo>();
                            var txCount = bytes.Length / txSize;
                            for (int i = 0; i < txCount; i++) {
                                byte[] subBytes = new byte[txSize];
                                Array.Copy(bytes, i * txSize, subBytes, 0, txSize);

                                GCHandle handle = GCHandle.Alloc(subBytes, GCHandleType.Pinned);
                                try
                                {
                                    TransactionInfo tx = (TransactionInfo)Marshal.PtrToStructure(handle.AddrOfPinnedObject(), typeof(TransactionInfo));

                                    byte[] signature = new byte[64];
                                    byte[] signingKey = new byte[32];
                                    byte[] to = new byte [25];
                                    byte[] from = new byte [25];

                                    unsafe {
                                        Marshal.Copy((IntPtr)tx.signature, signature, 0, 64);
                                        Marshal.Copy((IntPtr)tx.signingKey, signingKey, 0, 32);
                                        Marshal.Copy((IntPtr)tx.to, to, 0, 25);
                                        Marshal.Copy((IntPtr)tx.from, from, 0, 25);
                                    }

                                    var transaction = new Transaction {
                                        signature = signature.AsString(),
                                        signingKey = signingKey.AsString(),
                                        timestamp = tx.timestamp.ToString(),
                                        to = to.AsString(),
                                        from = from.AsString(),
                                        amount = tx.amount,
                                        fee = tx.fee,
                                        isTransactionFee = tx.isTransactionFee
                                    };

                                    data.Add(transaction);
                                }
                                finally
                                {
                                    handle.Free();
                                }
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
                        await httpResponseMessage.Content.ReadAsStringAsync();
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
