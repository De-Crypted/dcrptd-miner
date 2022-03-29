using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.IO;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace dcrpt_miner 
{
    public class BambooNodeConnectionProvider : IConnectionProvider
    {
        public string SolutionName { get; } = "Block";
        private IHttpClientFactory HttpClientFactory { get; }
        private Channels Channels { get; }
        private IConfiguration Configuration { get; }
        private ILogger<BambooNodeConnectionProvider> Logger { get; }

        private Block CurrentBlock { get; set; }
        private string Url { get; set; }
        private CancellationTokenSource ThreadSource = new CancellationTokenSource();

        public BambooNodeConnectionProvider(IHttpClientFactory httpClientFactory, Channels channels, IConfiguration configuration, ILogger<BambooNodeConnectionProvider> logger)
        {
            HttpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
            Channels = channels ?? throw new ArgumentNullException(nameof(channels));
            Configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
            Logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public Task InitializeAsync()
        {
            Logger.LogDebug("Initialize ShifuPoolConnectionProvider");

            Url = Configuration.GetValue<string>("url")
                .Replace("bamboo://", "http://");

            new Thread(async () => await HandleConnection(ThreadSource.Token))
                .UnsafeStart();

            return Task.CompletedTask;
        }

        public async Task<SubmitResult> SubmitAsync(byte[] solution)
        {
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream))
            {
            
                Logger.LogDebug("Submitting block");

                writer.Write(CurrentBlock.Id);
                writer.Write(CurrentBlock.Timestamp);
                writer.Write(CurrentBlock.ChallengeSize);
                writer.Write(CurrentBlock.Transactions.Count);
                writer.Write(CurrentBlock.LastHash);
                writer.Write(CurrentBlock.RootHash);
                writer.Write(solution);

                foreach (var transaction in CurrentBlock.Transactions)
                {
                    var signature = string.IsNullOrEmpty(transaction.signature) ? 
                        new byte[64] {  0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,  0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 }: 
                        transaction.signature.ToByteArray();
                    writer.Write(signature);
                    
                    var signingKey = string.IsNullOrEmpty(transaction.signingKey) ? 
                        new byte[32] { 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 }: 
                        transaction.signingKey.ToByteArray();
                    writer.Write(signingKey);

                    writer.Write(ulong.Parse(transaction.timestamp));

                    writer.Write(transaction.to.ToByteArray());
                    writer.Write(transaction.amount);
                    writer.Write(transaction.fee);
                    writer.Write(Convert.ToUInt32(transaction.isTransactionFee));
                }

                writer.Flush();
                stream.Flush();
                stream.Position = 0;

                if (await Submit(stream)) {
                    await Channels.Jobs.Writer.WriteAsync(new Job {
                        Type = JobType.STOP
                    });

                    return SubmitResult.ACCEPTED;
                }

                return SubmitResult.REJECTED;
            }
        }

        private async Task HandleConnection(CancellationToken cancellationToken)
        {
            uint current_id = 0;

            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    cancellationToken.WaitHandle.WaitOne(500);

                    var request = await GetBlock();
                    if (!request.success || request.block <= current_id)
                    {
                        continue;
                    }

                    current_id = request.block;

                    var problem = await GetMiningProblem();
                    if (!problem.success)
                    {
                        continue;
                    }

                    var transactions = await GetTransactions();
                    if (!transactions.success)
                    {
                        Logger.LogInformation("Transactions failed");
                    }

                    Logger.LogDebug("{}: New Block = {}, Difficulty = {}, Transactions = {}",
                        DateTime.Now,
                        request.block,
                        problem.data.challengeSize,
                        transactions.data.Count);

                    using (var sha256 = SHA256.Create())
                    using (var stream = new MemoryStream())
                    {
                        var reward = new Transaction();
                        reward.to = Configuration.GetValue<string>("user");
                        reward.amount = problem.data.miningFee;
                        reward.fee = 0;
                        reward.timestamp = problem.data.lastTimestamp;
                        reward.isTransactionFee = true;
                        transactions.data.Add(reward);

                        var tree = new MerkleTree(transactions.data);

                        var timestamp = (ulong)DateTimeOffset.Now.ToUnixTimeSeconds();

                        stream.Write(tree.RootHash);
                        stream.Write(problem.data.lastHash.ToByteArray());
                        stream.Write(BitConverter.GetBytes(problem.data.challengeSize));
                        stream.Write(BitConverter.GetBytes(timestamp));
                        stream.Flush();
                        stream.Position = 0;

                        var nonce = sha256.ComputeHash(stream);

                        CurrentBlock = new Block
                        {
                            Id = request.block + 1,
                            Timestamp = timestamp,
                            ChallengeSize = problem.data.challengeSize,
                            LastHash = problem.data.lastHash.ToByteArray(),
                            RootHash = tree.RootHash,
                            Transactions = transactions.data
                        };

                        await Channels.Jobs.Writer.WriteAsync(new Job {
                            Type = JobType.NEW,
                            Nonce = nonce,
                            Difficulty = (int)problem.data.challengeSize
                        });
                    }
                }
                catch (Exception ex)
                {
                    Logger.LogError(ex, "BambooNodeConnection threw error");
                    await Channels.Jobs.Writer.WriteAsync(new Job {
                        Type = JobType.STOP
                    });
                }
            }
        }

        private async Task<(bool success, uint block)> GetBlock()
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
                Logger.LogError(ex, "GetBlock() failed");
                return (false, 0);
            }
        }

        private async Task<(bool success, MiningProblem data)> GetMiningProblem()
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

        private async Task<(bool success, List<Transaction> data)> GetTransactions()
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
                                Array.Copy(bytes, i * txSize, subBytes, (i * txSize) + txSize, txSize);

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

        private async Task<bool> Submit(Stream stream)
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
