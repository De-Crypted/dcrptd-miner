using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Unclassified.Net;
using Microsoft.Extensions.Logging;

namespace dcrpt_miner 
{
    public class ShifuPoolConnectionProvider : IConnectionProvider
    {
        public IConfiguration Configuration { get; }
        public Channels Channels { get; }
        public ILogger<ShifuPoolConnectionProvider> Logger { get; }

        private CancellationTokenSource ThreadSource = new CancellationTokenSource();
        private BlockingCollection<Response> Results = new BlockingCollection<Response>(1);

        private AsyncTcpClient Client { get; set; }
        private ConcurrentQueue<DateTime> LastShares = new ConcurrentQueue<DateTime>();
        private Job CurrentJob { get; set; }
        private string User { get; set; }
        
        public ShifuPoolConnectionProvider(IConfiguration configuration, Channels channels, ILogger<ShifuPoolConnectionProvider> logger)
        {
            Configuration = configuration ?? throw new System.ArgumentNullException(nameof(configuration));
            Channels = channels ?? throw new ArgumentNullException(nameof(channels));
            Logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public Task InitializeAsync()
        {
            Logger.LogDebug("Initialize ShifuPoolConnectionProvider");
            new Thread(async () => await HandleConnection(ThreadSource.Token))
                .UnsafeStart();

            new Thread(async () => {
                ThreadSource.Token.WaitHandle.WaitOne(TimeSpan.FromSeconds(30));
                Logger.LogDebug("Calibrate difficulty after 30 seconds");

                if (LastShares.Count == 0 && CurrentJob != null) {
                    var difficulty = CalculateTargetDifficulty();

                    await Channels.Jobs.Writer.WriteAsync(new Job {
                        Type = JobType.RESTART,
                        Nonce = CurrentJob.Nonce,
                        Difficulty = difficulty
                    });
                }
            }).UnsafeStart();

            /*new Thread(async () => {
                var token = ThreadSource.Token;
                token.WaitHandle.WaitOne(TimeSpan.FromSeconds(10));

                while (!token.IsCancellationRequested) {
                    Console.ForegroundColor = ConsoleColor.DarkBlue;
                    Console.WriteLine("{0:T}: Starting dev fee", DateTime.Now);
                    Console.ResetColor();

                    User = "0000";
                    Client.Disconnect();
                    token.WaitHandle.WaitOne(TimeSpan.FromMinutes(1));

                    Console.ForegroundColor = ConsoleColor.DarkBlue;
                    Console.WriteLine("{0:T}: Stopping dev fee", DateTime.Now);
                    Console.ResetColor();

                    User = Configuration.GetValue<string>("user");
                    Client.Disconnect();
                    token.WaitHandle.WaitOne(TimeSpan.FromMinutes(100));
                }
            }).UnsafeStart();*/

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

                Logger.LogDebug("Is diffuclty adjustment required? {}", selfAdjust);

                if (selfAdjust) {
                    Logger.LogDebug("Too many shares submitted, readjust difficulty...");
                    var newDiff = CalculateTargetDifficulty();
                    Logger.LogDebug("New difficulty = {}", newDiff);

                    if (newDiff > CurrentJob.Difficulty) {
                        await Channels.Jobs.Writer.WriteAsync(new Job {
                            Type = JobType.RESTART,
                            Nonce = CurrentJob.Nonce,
                            Difficulty = newDiff
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
            User = Configuration.GetValue<string>("user");

            if (String.IsNullOrEmpty(User)) {
                throw new Exception("Invalid user");
            }

            Console.WriteLine("User {0}", User);

            if (User.Length != 50) {
                Console.ForegroundColor = ConsoleColor.DarkRed;
                Console.WriteLine("Invalid user!");
                Console.ResetColor();
                return;
            }

            var url = Configuration.GetValue<string>("url");
            var parts = url.Replace("/", String.Empty).Split(':');

            if (parts.Length != 3) {
                throw new Exception("Invalid hostname: " + url);
            }

            var hostname = parts[1];
            var port = int.Parse(parts[2]);

            Client = new AsyncTcpClient 
            {
                HostName = hostname,
                Port = port,
                AutoReconnect = true,
                ConnectedCallback = OnConnected,
                ReceivedCallback = OnReceived,
                ClosedCallback = OnClosed
            };

            Client.Message += (s, a) => Console.WriteLine(a.Message);

            await Client.RunAsync();
        }

        private async Task OnConnected(AsyncTcpClient client, bool isReconnected) {
            Logger.LogDebug("ShifuPool:OnConnected");
            var json = JsonSerializer.Serialize(new Initialize {
                address = User
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
            var jsonRaw = Encoding.UTF8.GetString(bytes, 0, bytes.Length);

            Logger.LogDebug("Packet (raw json):\n{}", jsonRaw);

            if (String.IsNullOrEmpty(jsonRaw)) {
                return;
            }

            var jsonArr = jsonRaw.Split('\n');

            foreach (var json in jsonArr) {
                if (String.IsNullOrEmpty(json)) {
                    return;
                }

                if (json.Contains("Work")) {
                    Logger.LogDebug("PacketType = Work");
                    var difficulty = CalculateTargetDifficulty();
                    var work = JsonSerializer.Deserialize(json, typeof(Work)) as Work;

                    var blockhash = work.blockhash.ToByteArray();

                    if (blockhash.Length != 32) {
                        Console.WriteLine("Invalid job received");
                        return;
                    }

                    CurrentJob = new Job {
                        Type = JobType.NEW,
                        Nonce = blockhash,
                        Difficulty = difficulty
                    };

                    await Channels.Jobs.Writer.WriteAsync(CurrentJob);
                    return;
                }

                if (json.Contains("Notification")) {
                    Logger.LogDebug("PacketType = Notification");
                    var notification = JsonSerializer.Deserialize(json, typeof(Notification)) as Notification;
                    Console.ForegroundColor = ConsoleColor.DarkMagenta;
                    Console.WriteLine(notification.msg);
                    Console.ResetColor();
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

                Console.WriteLine(json);
            }
        }

        private int CalculateTargetDifficulty()
        {
            Logger.LogDebug("Calculate target difficulty");

            try {
                ulong hashes = StatusManager.GetHashrate("TOTAL", 0, TimeSpan.FromSeconds(30));

                if (hashes == 0) {
                    Logger.LogDebug("Forcing difficulty to 35 (no hashes done)");
                    return 35;
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
                Logger.LogDebug("Forcing difficulty to 35 (unknown error)");
                return 35;
            }
        }

        private class Initialize 
        {
            public string address { get; set; }
            public string type { get; set; } = "Initialize";
            public string useragent { get; set; }
        }

        private class Work 
        {
            public string type { get; set; }
            public string blockhash { get; set; }
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
    }
}