using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Security.Cryptography;
using System.IO;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Linq;
using System.Reflection;

namespace dcrpt_miner 
{
    public class BambooNodeConnectionProvider : IConnectionProvider
    {
        public string SolutionName { get; } = "Block";
        public string JobName { get; } = "Block";

        private IHttpClientFactory HttpClientFactory { get; }
        private Channels Channels { get; }
        private IConfiguration Configuration { get; }
        private ILogger<BambooNodeConnectionProvider> Logger { get; }
        public ILoggerFactory LoggerFactory { get; }

        private CancellationTokenSource ThreadSource = new CancellationTokenSource();
        private SemaphoreSlim _lock = new SemaphoreSlim(1,1);

        private Block CurrentBlock { get; set; }
        private string Url { get; set; }
        private string Wallet { get; set; }
        private IBambooNodeApi Node { get; set; }

        public BambooNodeConnectionProvider(IHttpClientFactory httpClientFactory, Channels channels, IConfiguration configuration, ILogger<BambooNodeConnectionProvider> logger, ILoggerFactory loggerFactory)
        {
            HttpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
            Channels = channels ?? throw new ArgumentNullException(nameof(channels));
            Configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
            Logger = logger ?? throw new ArgumentNullException(nameof(logger));
            LoggerFactory = loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory));
        }

        public Task RunAsync(string url)
        {
            Logger.LogDebug("Initialize BambooNodeConnectionProvider");

            Url = url.Replace("bamboo://", "http://");
            Wallet = Configuration.GetValue<string>("user").Split(".").ElementAtOrDefault(0);

            new Thread(() => HandleDevFee(ThreadSource.Token))
                .UnsafeStart();

            return HandleConnection(ThreadSource.Token);
        }

        public Task StopAsync()
        {
            Logger.LogDebug("Stop BambooNodeConnectionProvider");
            ThreadSource.Cancel();
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

                await _lock.WaitAsync();

                try {
                    if (await Node.Submit(stream)) {
                        await Channels.Jobs.Writer.WriteAsync(new Job {
                            Type = JobType.STOP
                        });

                        return SubmitResult.ACCEPTED;
                    }

                    return SubmitResult.REJECTED;
                } catch (Exception ex) {
                    throw new Exception("Submit failed", ex);
                } finally {
                    _lock.Release();
                }
            }
        }

        private void HandleDevFee(CancellationToken cancellationToken)
        {
            cancellationToken.WaitHandle.WaitOne(TimeSpan.FromMinutes(5));

            var userWallet = Configuration.GetValue<string>("user");

            while (!cancellationToken.IsCancellationRequested)
            {
                var devFee = (double)GetAlgo(CurrentBlock.Id).GetProperty("DevFee").GetValue(null);
                var devWallet = (string)GetAlgo(CurrentBlock.Id).GetProperty("DevWallet").GetValue(null);

                double miningTime = TimeSpan.FromMinutes(60).TotalSeconds;
                var devFeeSeconds = (int)(miningTime * devFee);

                if (devFeeSeconds > 0) {
                    SafeConsole.WriteLine(ConsoleColor.DarkCyan, "{0:T}: Starting dev fee for {1} seconds", DateTime.Now, devFeeSeconds);
                
                    Wallet = devWallet;
                    CreateBlockAndAnnounceJob(CurrentBlock.Id, JobType.RESTART, CurrentBlock.Problem, CurrentBlock.Transactions);

                    cancellationToken.WaitHandle.WaitOne(TimeSpan.FromSeconds(devFeeSeconds));

                    Wallet = Configuration.GetValue<string>("user").Split(".").ElementAtOrDefault(0);
                    CreateBlockAndAnnounceJob(CurrentBlock.Id, JobType.RESTART, CurrentBlock.Problem, CurrentBlock.Transactions);

                    SafeConsole.WriteLine(ConsoleColor.DarkCyan, "{0:T}: Dev fee stopped", DateTime.Now);
                }

                cancellationToken.WaitHandle.WaitOne(TimeSpan.FromSeconds(miningTime - devFeeSeconds));
            }
        }

        private async Task HandleConnection(CancellationToken cancellationToken)
        {
            var retries = Configuration.GetValue<uint?>("retries");

            if (!retries.HasValue) {
                retries = 5;
            }

            uint currentId = 0;
            uint retryCount = 0;

            Node = new BambooNodeV1Api(HttpClientFactory, Url, LoggerFactory);

            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    // Stop mining, while we try to get block information from node
                    if (retryCount > 0) {
                        await Channels.Jobs.Writer.WriteAsync(new Job {
                            Type = JobType.STOP
                        });

                        if (retryCount >= retries) {
                            await StopAsync();
                            return;
                        }
                    }

                    cancellationToken.WaitHandle.WaitOne(500);

                    (var success, var newId) = await Node.GetBlock();

                    if (!success) {
                        retryCount++;
                        PrintRetryMessage(retryCount, retries.Value);
                        cancellationToken.WaitHandle.WaitOne(TimeSpan.FromSeconds(1));
                        continue;
                    }

                    if (newId <= currentId)
                    {
                        // All good, we're still mining the same block
                        continue;
                    }

                    var problem = await Node.GetMiningProblem();
                    if (!problem.success)
                    {
                        retryCount++;
                        PrintRetryMessage(retryCount, retries.Value);
                        cancellationToken.WaitHandle.WaitOne(TimeSpan.FromSeconds(1));
                        continue;
                    }

                    var transactions = await Node.GetTransactions();
                    if (!transactions.success)
                    {
                        retryCount++;
                        PrintRetryMessage(retryCount, retries.Value);
                        cancellationToken.WaitHandle.WaitOne(TimeSpan.FromSeconds(1));
                        continue;
                    }

                    Logger.LogDebug("{}: New Block = {}, Difficulty = {}, Transactions = {}, RetryCount = {}",
                        DateTime.Now,
                        newId,
                        problem.data.challengeSize,
                        transactions.data.Count,
                        retryCount);

                    retryCount = 0;

                     CreateBlockAndAnnounceJob(newId + 1, JobType.NEW, problem.data, transactions.data);

                    currentId = newId;
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

        private void CreateBlockAndAnnounceJob(uint blockId, JobType jobType, MiningProblem problem, List<Transaction> transactions)
        {
            _lock.Wait();

            try {

                using (var sha256 = SHA256.Create())
                using (var stream = new MemoryStream())
                {
                    transactions.RemoveAll(x => x.isTransactionFee);
                    var reward = new Transaction
                    {
                        to = Wallet,
                        amount = problem.miningFee,
                        fee = 0,
                        timestamp = problem.lastTimestamp,
                        isTransactionFee = true
                    };

                    transactions.Add(reward);

                    var tree = new MerkleTree(transactions);
                    var timestamp = (ulong)DateTimeOffset.Now.ToUnixTimeSeconds();

                    stream.Write(tree.RootHash);
                    stream.Write(problem.lastHash.ToByteArray());
                    stream.Write(BitConverter.GetBytes(problem.challengeSize));
                    stream.Write(BitConverter.GetBytes(timestamp));
                    stream.Flush();
                    stream.Position = 0;

                    CurrentBlock = new Block
                    {
                        Id = blockId,
                        Timestamp = timestamp,
                        ChallengeSize = problem.challengeSize,
                        LastHash = problem.lastHash.ToByteArray(),
                        RootHash = tree.RootHash,
                        Transactions = transactions,
                        Nonce = sha256.ComputeHash(stream),
                        Problem = problem
                    };

                    Channels.Jobs.Writer.WriteAsync(new Job {
                        Type = jobType,
                        Name = JobName,
                        Nonce = CurrentBlock.Nonce,
                        Difficulty = (int)problem.challengeSize,
                        Algorithm = GetAlgo(blockId)
                    });
                }
            }
            catch (Exception ex) {
                throw new Exception("CreateBlock failed", ex);
            } finally {
                _lock.Release();
            }
        }

        private Type GetAlgo(uint id) 
        {
            return id > 124500 ? typeof(Pufferfish2BmbAlgo) : typeof(SHA256BmbAlgo);
        }

        private async Task<(bool success, Version version)> GetNodeVersion()
        {
            try
            {
                var httpRequestMessage = new HttpRequestMessage(HttpMethod.Get, Url + "/name");

                using (var httpClient = HttpClientFactory.CreateClient())
                using (var httpResponseMessage = await httpClient.SendAsync(httpRequestMessage))
                {
                    var success = httpResponseMessage.IsSuccessStatusCode;
                    Version version = null;

                    if (success)
                    {
                        using (var contentStream = await httpResponseMessage.Content.ReadAsStreamAsync())
                        {
                            var versionInfo = await JsonSerializer.DeserializeAsync<NodeVersionInfo>(contentStream);
                            version = new Version(versionInfo.version.Split('-').First());
                        }
                    }

                    return (success, version);
                }
            }
            catch (Exception ex)
            {
                Logger.LogDebug(ex, "GetNodeVersion() failed");
                return (false, null);
            }
        }

        private void PrintRetryMessage(uint retryCount, uint retries) {
            SafeConsole.WriteLine(ConsoleColor.DarkRed, "{0:T}: Node connection interrupted, retrying ({1} / {2})...", DateTime.Now, retryCount, retries);
        }
    }
}
