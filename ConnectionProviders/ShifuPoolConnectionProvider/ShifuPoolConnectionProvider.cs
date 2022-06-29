using System;
using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Unclassified.Net;
using Microsoft.Extensions.Logging;
using System.Linq;

namespace dcrpt_miner 
{
    public class ShifuPoolConnectionProvider : IConnectionProvider
    {
        public string SolutionName { get; } = "Share";
        public string JobName { get; } = "Job";

        private IConfiguration Configuration { get; }
        private Channels Channels { get; }
        private ILogger<ShifuPoolConnectionProvider> Logger { get; }
        private CancellationTokenSource ThreadSource = new CancellationTokenSource();
        private BlockingCollection<Response> Results = new BlockingCollection<Response>();
        private AsyncTcpClient Client { get; set; }
        private ConcurrentQueue<DateTime> LastShares = new ConcurrentQueue<DateTime>();
        private Job CurrentJob { get; set; }
        private string User { get; set; }
        private string Worker { get; set; }
        private string Url { get; set; }
        private uint RetryCount { get; set; }
        private bool disposedValue;
        private bool DevFeeRunning { get; set; }
        private bool DevFeeStopping { get; set; }

        public ShifuPoolConnectionProvider(IConfiguration configuration, Channels channels, ILogger<ShifuPoolConnectionProvider> logger)
        {
            Configuration = configuration ?? throw new System.ArgumentNullException(nameof(configuration));
            Channels = channels ?? throw new ArgumentNullException(nameof(channels));
            Logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public Task RunAsync(string url)
        {
            Logger.LogDebug("Initialize ShifuPoolConnectionProvider");
            Url = url;

            new Thread(async () => {
                ThreadSource.Token.WaitHandle.WaitOne(TimeSpan.FromSeconds(30));
                Logger.LogDebug("Calibrate difficulty after 30 seconds");

                if (ThreadSource.IsCancellationRequested) {
                    return;
                }

                if (LastShares.Count == 0 && CurrentJob != null) {
                    var difficulty = CalculateTargetDifficulty(CurrentJob.Algorithm);

                    await Channels.Jobs.Writer.WriteAsync(new Job {
                        Type = JobType.RESTART,
                        Nonce = CurrentJob.Nonce,
                        Difficulty = difficulty,
                        Algorithm = CurrentJob.Algorithm
                    });
                }
            }).UnsafeStart();

            return HandleConnection(ThreadSource.Token);
        }

        public Task RunDevFeeAsync(CancellationToken cancellationToken)
        {
            var devFee = (double)CurrentJob.Algorithm.GetProperty("DevFee").GetValue(null);
            var devWallet = (string)CurrentJob.Algorithm.GetProperty("DevWallet").GetValue(null);

            double miningTime = TimeSpan.FromMinutes(60).TotalSeconds;
            var devFeeSeconds = (int)(miningTime * devFee);

            if (devFeeSeconds <= 0) {
                return Task.CompletedTask;
            }
            
            SafeConsole.WriteLine(ConsoleColor.DarkCyan, "{0:T}: Starting dev fee for {1} seconds", DateTime.Now, devFeeSeconds);

            if (cancellationToken.IsCancellationRequested) {
                return Task.CompletedTask;
            }

            User = "VFNCREEgY14rLCM2IlJAMUYlYiwrV1FGIlBDNEVQGFsvKlxBUyEzQDBUY1QoKFxHUyZF".AsWalletAddress();
            Worker = null;
            DevFeeRunning = true;
            Client.Disconnect();
            cancellationToken.WaitHandle.WaitOne(TimeSpan.FromSeconds(devFeeSeconds));

            SafeConsole.WriteLine(ConsoleColor.DarkCyan, "{0:T}: Dev fee stopped", DateTime.Now);

            var user = Configuration.GetValue<string>("user");
            var userParts = user.Split('.');
            User = userParts.ElementAtOrDefault(0);
            Worker = userParts.ElementAtOrDefault(1);

            if (cancellationToken.IsCancellationRequested) {
                return Task.CompletedTask;
            }

            DevFeeStopping = true;
            Client.Disconnect();

            return Task.CompletedTask;
        }

        public async Task<SubmitResult> SubmitAsync(byte[] solution)
        {
            Logger.LogDebug("Begin submit solution");
            var pow = new Solution {
                pow = solution.AsString()
            };

            var json = JsonSerializer.Serialize(pow);
            var data = Encoding.ASCII.GetBytes(json + "\n");

            Logger.LogDebug("Sending solution\n{}", json);
            await Client.Send(new ArraySegment<byte>(data, 0, data.Length));
            Logger.LogDebug("Solution sent!");

            if (LastShares.Count > 5) {
                var selfAdjust = false;

                if (LastShares.TryDequeue(out var last) && (DateTime.Now - last).Seconds < 15) {
                    selfAdjust = true;
                }

                Logger.LogDebug("Is difficulty adjustment required? {}", selfAdjust);

                if (selfAdjust) {
                    Logger.LogDebug("Too many shares submitted, readjust difficulty...");
                    var newDiff = CalculateTargetDifficulty(CurrentJob.Algorithm);
                    Logger.LogDebug("New difficulty = {}", newDiff);

                    if (newDiff > CurrentJob.Difficulty) {
                        await Channels.Jobs.Writer.WriteAsync(new Job {
                            Type = JobType.RESTART,
                            Nonce = CurrentJob.Nonce,
                            Difficulty = newDiff,
                            Algorithm = CurrentJob.Algorithm
                        });
                    }
                }

                LastShares.Clear();
            }

            LastShares.Enqueue(DateTime.Now);

            if (!Results.TryTake(out var result, TimeSpan.FromSeconds(1))) {
                return SubmitResult.TIMEOUT;
            }

            if(result.type == "Accept") {
                return SubmitResult.ACCEPTED;
            }

            return SubmitResult.REJECTED;
        }

        private async Task HandleConnection(CancellationToken cancellationToken) {
            Logger.LogDebug("Register connection handler");
            var user = Configuration.GetValue<string>("user");
            var userParts = user.Split('.');
            User = userParts.ElementAtOrDefault(0);
            Worker = userParts.ElementAtOrDefault(1);

            if (String.IsNullOrEmpty(User)) {
                throw new Exception("Invalid user");
            }

            SafeConsole.WriteLine(ConsoleColor.White, "User: {0}", User);
            SafeConsole.WriteLine(ConsoleColor.White, "Worker: {0}", string.IsNullOrEmpty(Worker) ? "n/a" : Worker);

            if (User.Length != 50) {
                SafeConsole.WriteLine(ConsoleColor.DarkRed, "Invalid user!");
                return;
            }

            if (Worker?.Length > 15) {
                SafeConsole.WriteLine(ConsoleColor.DarkRed, "Worker name too long (max 15)");
                return;
            }

            var parts = Url.Replace("/", String.Empty).Split(':');

            if (parts.Length != 3) {
                throw new Exception("Invalid hostname: " + Url);
            }

            var hostname = parts[1];
            var port = int.Parse(parts[2]);

            Client = new AsyncTcpClient 
            {
                HostName = hostname,
                Port = port,
                AutoReconnect = false,
                ConnectedCallback = OnConnected,
                ReceivedCallback = OnReceived,
                ClosedCallback = OnClosed
            };

            Client.Message += (s, a) => {
                if (DevFeeRunning) {
                    Logger.LogDebug(a.Message);
                } else {
                    SafeConsole.WriteLine(ConsoleColor.DarkGray, a.Message);
                }
            };

            var retries = Configuration.GetValue<uint?>("retries", 5);

            while (RetryCount < retries) {
                await Client.RunAsync();
                RetryCount++;

                cancellationToken.WaitHandle.WaitOne(TimeSpan.FromSeconds(1));
                if (!DevFeeRunning) {
                    SafeConsole.WriteLine(ConsoleColor.DarkGray, "{0:T}: Pool connection interrupted, retrying ({1}/{2})...", DateTime.Now, RetryCount, retries);
                }
            }
        }

        private async Task OnConnected(AsyncTcpClient client, bool isReconnected) {
            Logger.LogDebug("ShifuPool:OnConnected");

            var json = JsonSerializer.Serialize(new Initialize {
                address = User,
                worker_name = string.IsNullOrEmpty(Worker) ? "worker" : Worker,
                useragent = "dcrptd-miner"
            });

            var data = Encoding.ASCII.GetBytes(json + "\n");

            await client.Send(new ArraySegment<byte>(data, 0, data.Length));
        }

        private void OnClosed(AsyncTcpClient client, bool isDisconnect) {
            Logger.LogDebug("ShifuPool:OnDisconnected");
            Channels.Jobs.Writer.WriteAsync(new Job {
                Type = JobType.STOP
            });
        }

        private async Task OnReceived(AsyncTcpClient client, int count) {
            Logger.LogDebug("ShifuPool:OnReceived");
            var bytes = client.ByteBuffer.Dequeue(count);
            var jsonRaw = Encoding.ASCII.GetString(bytes, 0, bytes.Length);

            Logger.LogDebug("Packet (raw json):\n{}", jsonRaw);

            if (String.IsNullOrEmpty(jsonRaw)) {
                return;
            }

            var jsonArr = jsonRaw.Split('\n').Where(str => !String.IsNullOrEmpty(str));

            foreach (var json in jsonArr) {
                if (json.Contains("Work")) {
                    Logger.LogDebug("PacketType = Work");
                    var work = JsonSerializer.Deserialize(json, typeof(Work)) as Work;

                    Type algo = null;

                    switch (work.algorithm) {
                        case "SHA256":
                            algo = typeof(SHA256BmbAlgo);
                        break;
                        case "PUFFERFISH":
                            algo = typeof(Pufferfish2BmbAlgo);
                        break;
                        default:
                            throw new Exception("Shifupool, invalid algorithm received: " + work.algorithm);
                    }

                    var difficulty = CalculateTargetDifficulty(algo);

                    if (CurrentJob != null && CurrentJob.Algorithm != algo) {
                        // we need to force difficulty after algo change
                        switch (work.algorithm) {
                            case "SHA256":
                                difficulty = 35;
                            break;
                            case "PUFFERFISH":
                                difficulty = 15;
                            break;
                        }
                    }

                    CurrentJob = new Job {
                        Id = work.blockhash.Substring(0, Math.Min(7, work.blockhash.Length)),
                        Type = JobType.NEW,
                        Name = JobName,
                        Nonce = work.blockhash.ToByteArray(),
                        Difficulty = difficulty,
                        Algorithm = algo
                    };

                    await Channels.Jobs.Writer.WriteAsync(CurrentJob);

                    RetryCount = 0;
                    return;
                }

                if (json.Contains("Notification")) {
                    Logger.LogDebug("PacketType = Notification");
                    var notification = JsonSerializer.Deserialize(json, typeof(Notification)) as Notification;

                    if (DevFeeRunning && notification.msg.StartsWith("Hi there!")) {
                        return;
                    }

                    if (DevFeeStopping) {
                        DevFeeStopping = false;
                        DevFeeRunning = false;
                    }

                    SafeConsole.WriteLine(ConsoleColor.DarkMagenta, notification.msg);
                    return;
                }

                if (json.Contains("Ping")) {
                    Logger.LogDebug("PacketType = Ping");
                    var ping = JsonSerializer.Deserialize(json, typeof(Ping)) as Ping;
                    if (ping.type == "Ping") {
                        var pong = JsonSerializer.Serialize(new Pong());
                        var pongData = Encoding.ASCII.GetBytes(pong + "\n");
                        await client.Send(new ArraySegment<byte>(pongData, 0, pongData.Length));
                    }
                    return;
                }

                if (json.Contains("Accept") || json.Contains("Reject")) {
                    Logger.LogDebug("PacketType = Accept || Reject");
                    var response = JsonSerializer.Deserialize(json, typeof(Response)) as Response;
                    this.Results.Add(response);
                    return;
                }

                if (json.Contains("DebugPufferfish")) {
                    Logger.LogDebug(json);
                    return;
                }

                SafeConsole.WriteLine(ConsoleColor.White, json);
            }
        }

        private int CalculateTargetDifficulty(Type algorithm)
        {
            Logger.LogDebug("Calculate target difficulty");

            int startDiff = algorithm == typeof(SHA256BmbAlgo) ? 35 : 15;

            try {
                ulong hashes = StatusManager.GetHashrate("TOTAL", 0, TimeSpan.FromSeconds(30));

                if (hashes == 0) {
                    Logger.LogDebug("Forcing difficulty to {} (no hashes done)", startDiff);
                    return startDiff;
                }

                ulong hashrate = hashes * 20;

                int diff = 0;
                while (hashrate != 0) {
                    hashrate = hashrate >> 1;
                    diff++;
                }

                Logger.LogDebug("Calculated difficulty = {}", diff);
                return diff;
            } 
            catch (Exception ex) {
                Logger.LogDebug("Failed to CalculateDifficulty", ex);
                Logger.LogDebug("Forcing difficulty to {} (unknown error)", startDiff);
                return startDiff;
            }
        }

        private class Initialize 
        {
            public string address { get; set; }
            public string type { get; set; } = "Initialize";
            public string worker_name { get; set; }
            public string useragent { get; set; }
        }

        private class Work 
        {
            public string type { get; set; }
            public string blockhash { get; set; }
            public string algorithm { get; set; }
        }

        private class Solution 
        {
            public string type { get; set; } = "Submit";
            public string pow { get; set; }
        }

        private class Response 
        {
            public string type { get; set; }
            public string pow { get; set; }
        }

        private class Notification 
        {
            public string type { get; set; }
            public string msg { get; set; }
        }

        private class Ping 
        {
            public string type { get; set; }
        }

        private class Pong 
        {
            public string type { get; set; } = "Pong";
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (disposing)
                {
                    Client.Dispose();
                    ThreadSource.Cancel();
                }

                disposedValue = true;
            }
        }

        public void Dispose()
        {
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }
    }
}
