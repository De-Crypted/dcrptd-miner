using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Linq;

namespace dcrpt_miner
{
    public class StatusManager : IHostedService
    {
        private static Stopwatch Watch { get; set; }
        private static SpinLock SpinLock = new SpinLock();
        public static ulong[] CpuHashCount = new ulong[0];
        public static ulong[] GpuHashCount = new ulong[0];
        private static List<Snapshot> HashrateSnapshots = new List<Snapshot>();

        public IConfiguration Configuration { get; }
        private CancellationTokenSource ThreadSource = new CancellationTokenSource();

        public StatusManager(IConfiguration configuration)
        {
            Configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            Watch = new Stopwatch();

            new Thread(() => ReportProgress(ThreadSource.Token))
                .UnsafeStart();
            new Thread(() => CollectHashrate(ThreadSource.Token))
                .UnsafeStart();
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            ThreadSource.Cancel();
            return Task.CompletedTask;
        }

        public static ulong GetHashrate(String type, int id, TimeSpan from)
        {
            bool lockTaken = false;
            try {
                SpinLock.Enter(ref lockTaken);

                var timestampFrom = DateTime.Now - from;

                var snapshot = HashrateSnapshots.Where(p => p.type == type && p.id == id && p.timestamp >= timestampFrom)
                    .MinBy(p => p.timestamp);

                var latest = HashrateSnapshots.Where(p => p.type == type && p.id == id)
                    .MaxBy(p => p.timestamp);

                if (snapshot == null || latest == null) {
                    return 0;
                }
                
                if (snapshot.hashrate == latest.hashrate) {
                    // hashrate has not changed, find previous record to compare against
                    snapshot = HashrateSnapshots.Where(p => p.type == type && p.id == id && p.hashrate < latest.hashrate)
                        .MaxBy(p => p.timestamp);
                }

                if (snapshot == null) {
                    return 0;
                }

                var timeBetween = latest.timestamp - snapshot.timestamp;
                var hashesBetween = latest.hashrate - snapshot.hashrate;

                return (ulong)(hashesBetween / timeBetween.TotalSeconds);
            }
            catch (Exception ex) {
                Console.WriteLine(ex.ToString());
                return 0;
            }
            finally {
                if (lockTaken) {
                    SpinLock.Exit(false);
                }
            }
        }

        private void CollectHashrate(CancellationToken token)
        {
            var gpuEnabled = Configuration.GetValue<bool>("gpu:enabled");
            var cpuEnabled = Configuration.GetValue<bool>("cpu:enabled");

            while(!token.IsCancellationRequested) {
                bool lockTaken = false;
                try {
                    SpinLock.Enter(ref lockTaken);
                    ulong hashes = 0;

                    // keep only snapshots from past 10 minutes
                    var expiredAt = DateTime.Now.AddMinutes(-10);
                    HashrateSnapshots.RemoveAll(p => p.timestamp <= expiredAt);

                    if (cpuEnabled) {
                        for (int i = 0; i < CpuHashCount.Length; i++) {
                            hashes += CpuHashCount[i];
                        }

                        HashrateSnapshots.Add(new Snapshot {
                            timestamp = DateTime.Now,
                            type = "CPU",
                            id = 0,
                            hashrate = hashes
                        });
                    }

                    if (gpuEnabled) {
                        for (int i = 0; i < GpuHashCount.Length; i++) {
                            HashrateSnapshots.Add(new Snapshot {
                                timestamp = DateTime.Now,
                                type = "GPU",
                                id = i,
                                hashrate = GpuHashCount[i]
                            });

                            hashes += GpuHashCount[i];
                        }
                    }

                    HashrateSnapshots.Add(new Snapshot {
                        timestamp = DateTime.Now,
                        type = "TOTAL",
                        id = 0,
                        hashrate = hashes
                    });
                } catch (Exception ex) {
                    Console.WriteLine(ex.ToString());
                }
                finally {
                    if (lockTaken) {
                        SpinLock.Exit(false);
                    }
                }

                token.WaitHandle.WaitOne(TimeSpan.FromSeconds(10));
            }
        }

        private async void ReportProgress(CancellationToken token)
        {
            var gpuEnabled = Configuration.GetValue<bool>("gpu:enabled");
            var cpuEnabled = Configuration.GetValue<bool>("cpu:enabled");

            Watch.Start();
            token.WaitHandle.WaitOne(TimeSpan.FromSeconds(30));

            while(!token.IsCancellationRequested) {
                Console.ForegroundColor = ConsoleColor.White;
                Console.WriteLine("|---------------------------------------|");
                Console.WriteLine("| Periodic Report\t\t\t|");
                Console.WriteLine("|---------------------------------------|");
                Console.WriteLine("| Accepted \t\t{0}\t\t|", Program.AcceptedShares);
                Console.WriteLine("| Rejected \t\t{0}\t\t|", Program.RejectedShares);
                
                if (cpuEnabled) {
                    var cpuHashes = GetHashrate("CPU", 0, TimeSpan.FromMinutes(1));
                    CalculateUnit(cpuHashes, out double cpu_hashrate, out string cpu_unit);
                    Console.WriteLine("| Hashrate (CPU) \t{0:N2} {1}\t|", cpu_hashrate, cpu_unit);
                }

                if (gpuEnabled) {
                    for (int i = 0; i < GpuHashCount.Length; i++) {
                        var gpuHashes = GetHashrate("GPU", i, TimeSpan.FromMinutes(1));
                        CalculateUnit(gpuHashes, out double gpu_hashrate, out string gpu_unit);
                        Console.WriteLine("| Hashrate (GPU #{0}) \t{1:N2} {2}\t|",
                            i,
                            gpu_hashrate, 
                            gpu_unit);
                    }
                }

                if ((CpuHashCount.Length + GpuHashCount.Length) > 1) {
                    var totalHashes = GetHashrate("TOTAL", 0, TimeSpan.FromMinutes(1));
                    CalculateUnit(totalHashes, out double hashrate, out string unit);
                    Console.WriteLine("| Hashrate (Total) \t{0:N2} {1}\t|", hashrate, unit);
                }

                Console.WriteLine("|---------------------------------------|");
                Console.WriteLine("Uptime {0} days, {1} hours, {2} minutes", Watch.Elapsed.Days, Watch.Elapsed.Hours, Watch.Elapsed.Minutes);
                Console.ResetColor();

                token.WaitHandle.WaitOne(TimeSpan.FromMinutes(3));
            }
        }

        private void CalculateUnit(double hashrate, out double adjusted_hashrate, out string unit) {
            if (hashrate > 1000000000000) {
                adjusted_hashrate = hashrate / 1000000000000;
                unit = "TH/s";
                return;
            }

            if (hashrate > 1000000000) {
                adjusted_hashrate = hashrate / 1000000000;
                unit = "GH/s";
                return;
            }

            if (hashrate > 1000000) {
                adjusted_hashrate = hashrate / 1000000;
                unit = "MH/s";
                return;
            }

            if (hashrate > 1000) {
                adjusted_hashrate = hashrate / 1000;
                unit = "KH/s";
                return;
            }

            adjusted_hashrate = hashrate;
            unit = "H/s";
        }

        private class Snapshot {
            public DateTime timestamp { get; set; }
            public String type { get; set; }
            public int id { get; set; }
            public ulong hashrate { get; set; }
        }
    }
}
