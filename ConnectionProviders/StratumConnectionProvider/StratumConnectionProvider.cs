using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Globalization;
using System.Linq;
using System.Net.NetworkInformation;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Unclassified.Net;

namespace dcrpt_miner 
{
    public class StratumConnectionProvider : IConnectionProvider
    {
        public string SolutionName { get; } = "Share";
        public string JobName { get; } = "Job";

        private IConfiguration Configuration { get; }
        private Channels Channels { get; }
        private ILogger<StratumConnectionProvider> Logger { get; }
        private CancellationTokenSource ThreadSource = new CancellationTokenSource();
        private BlockingCollection<bool> ACK = new BlockingCollection<bool>();
        private AsyncTcpClient Client { get; set; }

        private int ID { get; set; }
        private string User { get; set; }
        private string Password { get; set; }
        private string Url { get; set; }
        private uint RetryCount { get; set; }
        private bool DevFeeRunning { get; set; }
        private bool DevFeeStopping { get; set; }
        private decimal Difficulty { get; set; }
        private Job CurrentJob { get; set; }

        private bool disposedValue;

        public string Server {
            get {
                var uri = new Uri(Url);
                return uri.Host + ":" + uri.Port;
            }
        }
        public string Protocol => "stratum+tcp";

        public StratumConnectionProvider(IConfiguration configuration, Channels channels, ILogger<StratumConnectionProvider> logger)
        {
            Configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
            Channels = channels ?? throw new ArgumentNullException(nameof(channels));
            Logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public long Ping()
        {
            using(var ping = new Ping()) {
                var uri = new Uri(Url);
                var reply = ping.Send(uri.DnsSafeHost);
                return reply.RoundtripTime;
            }
        }

        public Task RunAsync(string url)
        {
            Logger.LogDebug("Initialize StratumConnectionManager");
            Url = url;

            return HandleConnection(ThreadSource.Token);
        }

        public Task RunDevFeeAsync()
        {
            var cancellationToken = ThreadSource.Token;

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
            DevFeeRunning = true;

            Client.Disconnect();

            cancellationToken.WaitHandle.WaitOne(TimeSpan.FromSeconds(devFeeSeconds));

            SafeConsole.WriteLine(ConsoleColor.DarkCyan, "{0:T}: Dev fee stopped", DateTime.Now);

            User = Configuration.GetValue<string>("user");

            if (cancellationToken.IsCancellationRequested) {
                return Task.CompletedTask;
            }

            DevFeeStopping = true;
            Client.Disconnect();

            return Task.CompletedTask;
        }

        public async Task<SubmitResult> SubmitAsync(JobSolution solution)
        {
            Logger.LogDebug("SubmitAsync");

            var json = JsonSerializer.Serialize(new StratumCommand {
                id = ID++,
                method = "mining.submit",
                parameters = new ArrayList
                {
                    User,
                    solution.Nonce.AsString(),
                    solution.Solution.AsString()
                }
            });

            var data = Encoding.ASCII.GetBytes(json + "\n");

            ACK.Clear();

            await Client.Send(new ArraySegment<byte>(data, 0, data.Length));

            if (!ACK.TryTake(out var result, TimeSpan.FromSeconds(3))) {
                return SubmitResult.TIMEOUT;
            }

            return result ? SubmitResult.ACCEPTED : SubmitResult.REJECTED;
        }

        private async Task HandleConnection(CancellationToken cancellationToken) 
        {
            User = Configuration.GetValue<string>("user");
            Password = Configuration.GetValue<string>("password");

            if (String.IsNullOrEmpty(User)) {
                throw new Exception("Invalid user");
            }

            SafeConsole.WriteLine(ConsoleColor.White, "User: {0}", User);

            var uri = new Uri(Url);

            Client = new AsyncTcpClient 
            {
                HostName = uri.Host,
                Port = uri.Port,
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

                if (cancellationToken.IsCancellationRequested) {
                    return;
                }

                if (!DevFeeRunning) {
                    SafeConsole.WriteLine(ConsoleColor.DarkGray, "{0:T}: Pool connection interrupted, retrying ({1}/{2})...", DateTime.Now, RetryCount, retries);
                }
            }
        }

        private async Task OnConnected(AsyncTcpClient client, bool isReconnected) {
            Logger.LogDebug("OnConnected");

            var json = JsonSerializer.Serialize(new StratumCommand {
                id = ID++,
                method = "mining.subscribe",
                parameters = new ArrayList()
            });

            var data = Encoding.ASCII.GetBytes(json + "\n");

            await client.Send(new ArraySegment<byte>(data, 0, data.Length));

            json = JsonSerializer.Serialize(new StratumCommand {
                id = ID++,
                method = "mining.authorize",
                parameters = new ArrayList
                {
                    User,
                    Password
                }
            });

            data = Encoding.ASCII.GetBytes(json + "\n");

            await client.Send(new ArraySegment<byte>(data, 0, data.Length));
        }

        private void OnClosed(AsyncTcpClient client, bool isDisconnect) {
            Logger.LogDebug("OnDisconnected");
            Channels.Jobs.Writer.WriteAsync(new Job {
                Type = JobType.STOP
            });
        }

        private async Task OnReceived(AsyncTcpClient client, int count) {
            Logger.LogDebug("OnReceived");
            var bytes = client.ByteBuffer.Dequeue(count);
            var jsonRaw = Encoding.ASCII.GetString(bytes, 0, bytes.Length);

            Logger.LogDebug("Packet (raw json):\n{}", jsonRaw);

            if (String.IsNullOrEmpty(jsonRaw)) {
                return;
            }

            var jsonArr = jsonRaw.Split('\n').Where(str => !String.IsNullOrEmpty(str));

            foreach (var json in jsonArr) {
                if (string.IsNullOrEmpty(json)) {
                    continue;
                };
                
                if (json.Contains("\"method\"")) {
                    var command = JsonSerializer.Deserialize<StratumCommand>(json);

                    switch (command.method) {
                        case "mining.notify":
                            var blockId = uint.Parse(command.parameters[0].ToString());
                            var nonce = Convert.FromBase64String(command.parameters[1].ToString());

                            CurrentJob = new Job {
                                Id = nonce.AsString().Substring(0, Math.Min(7, nonce.Length)),
                                Type = JobType.NEW,
                                Name = JobName,
                                Nonce = nonce,
                                Difficulty = Difficulty,
                                Algorithm = typeof(Pufferfish2BmbAlgo)//blockId > 124500 ? typeof(Pufferfish2BmbAlgo) : typeof(SHA256BmbAlgo)
                            };

                            await Channels.Jobs.Writer.WriteAsync(CurrentJob);
                        break;
                        case "mining.set_difficulty":
                        var diff = decimal.Parse(command.parameters[0].ToString(), CultureInfo.InvariantCulture);

                        if (diff != Difficulty) {
                            Difficulty = diff;

                            if (CurrentJob != null) {
                                CurrentJob.Difficulty = diff;
                                await Channels.Jobs.Writer.WriteAsync(CurrentJob);
                            }
                        }

                        break;
                    }

                    continue;
                } else {
                    var response = JsonSerializer.Deserialize<StratumResponse>(json);
                    
                    if (response.result == null) {
                        continue;
                    }

                    if (bool.TryParse(response.result.ToString(), out var result)) {
                        ACK.TryAdd(result);
                    }
                    continue;
                }
            }
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

                // TODO: free unmanaged resources (unmanaged objects) and override finalizer
                // TODO: set large fields to null
                disposedValue = true;
            }
        }

        // // TODO: override finalizer only if 'Dispose(bool disposing)' has code to free unmanaged resources
        // ~StratumConnectionProvider()
        // {
        //     // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
        //     Dispose(disposing: false);
        // }

        public void Dispose()
        {
            // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
            Dispose(disposing: true);
            System.GC.SuppressFinalize(this);
        }
    }

    public class StratumCommand
    {
        public string method { get; set; }
        public System.Nullable<int> id { get; set; }
        [JsonPropertyName("params")]
        public ArrayList parameters { get; set; }
    }

    public class StratumResponse
    {
        // public ArrayList error { get; set; }
        public System.Nullable<int> id { get; set; }
        public object result { get; set; }
    }
}