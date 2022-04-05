using System;
using System.Threading;
using System.Collections.Generic;
using Microsoft.AspNetCore.SignalR.Client;
using System.Numerics;
using System.Threading.Tasks;
using System.Security.Cryptography;
using System.Collections.Concurrent;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Runtime.CompilerServices;

namespace dcrpt_miner
{
    public class WorkerManager : IHostedService
    {
        private CancellationTokenSource TokenSource = new CancellationTokenSource();
        private List<BlockingCollection<Job>> Workers = new List<BlockingCollection<Job>>();
        public Channels Channels { get; }
        public IConnectionProvider ConnectionProvider { get; }
        public IConfiguration Configuration { get; }
        public ILogger<WorkerManager> Logger { get; }
        public ILoggerFactory LoggerFactory { get; }

        private CancellationTokenSource ThreadSource = new CancellationTokenSource();

        public WorkerManager(Channels channels, IConnectionProvider connectionProvider, IConfiguration configuration, ILogger<WorkerManager> logger, ILoggerFactory loggerFactory)
        {
            Channels = channels ?? throw new ArgumentNullException(nameof(channels));
            ConnectionProvider = connectionProvider ?? throw new ArgumentNullException(nameof(connectionProvider));
            Configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
            Logger = logger ?? throw new ArgumentNullException(nameof(logger));
            LoggerFactory = loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory));
        }

        public Task StartAsync(CancellationToken cancellationToken) 
        {
            Logger.LogDebug("Starting WorkerManager thread");
            new Thread(async () => await JobHandler(ThreadSource.Token))
                .UnsafeStart();
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            Logger.LogDebug("Stopping ConnectionManager thread");
            Channels.Jobs.Writer.Complete();
            Channels.Solutions.Writer.Complete();
            ThreadSource.Cancel();
            TokenSource.Cancel();
            return Task.CompletedTask;
        }

        private async Task JobHandler(CancellationToken token)
        {
            var threads = 1;
            var cpuEnabled = Configuration.GetValue<bool>("cpu:enabled");
            var gpuEnabled = Configuration.GetValue<bool>("gpu:enabled");

            if (cpuEnabled) {
                threads = Configuration.GetValue<int>("cpu:threads");

                if (threads <= 0) {
                    threads = Environment.ProcessorCount;
                }

                StatusManager.CpuHashCount = new ulong[threads];

                for (uint i = 0; i < threads; i++) {
                    var queue = new BlockingCollection<Job>(1);

                    var tid = i;
                    Logger.LogDebug("Starting CpuWorker[{}] thread", tid);
                    new Thread(() => CpuWorker.DoWork(tid, queue, Channels, ThreadSource.Token))
                        .UnsafeStart();

                    Workers.Add(queue); 
                }
            }

            if (gpuEnabled) {
                var gpuDevices = GpuWorker.QueryDevices(Configuration, LoggerFactory);

                var gpuConfig = Configuration.GetValue<string>("gpu:device");
                if (string.IsNullOrEmpty(gpuConfig)) {
                    gpuConfig = "0";
                }
                var selectedGpus = gpuConfig.Split(',');

                StatusManager.GpuHashCount = new ulong[selectedGpus.Length];

                for (uint i = 0; i < selectedGpus.Length; i++) {
                    var queue = new BlockingCollection<Job>(1);
                    
                    var byId = int.TryParse(selectedGpus[i], out var deviceId);
                    var gpu = byId ? gpuDevices.Find(g => g.Id == deviceId) : gpuDevices.Find(g => g.DeviceName == selectedGpus[i]);

                    if (gpu == null) {
                        continue;
                    }

                    var tid = i;
                    Logger.LogDebug("Starting GpuWorker[{}] thread for gpu id: {}, name: {}", tid, gpu.Id, gpu.DeviceName);
                    new Thread(() => GpuWorker.DoWork(tid, gpu, queue, Channels, Configuration, LoggerFactory.CreateLogger<GpuWorker>(), ThreadSource.Token))
                        .UnsafeStart();

                    Workers.Add(queue);
                }
            }

            Logger.LogDebug("Waiting for job");
            await foreach(var job in Channels.Jobs.Reader.ReadAllAsync(ThreadSource.Token)) {
                TokenSource.Cancel();
                TokenSource.Dispose();
                TokenSource = new CancellationTokenSource();

                if (job.Type == JobType.STOP) {
                    Logger.LogDebug("Stop workers");
                    continue;
                }

                if (job.Type == JobType.NEW) {
                    Console.ForegroundColor = ConsoleColor.DarkYellow;
                    Console.WriteLine("{0:T}: New {1} (diff {2})", DateTime.Now, ConnectionProvider.JobName, job.Difficulty);
                    Console.ResetColor();
                }

                Logger.LogDebug("Assigning job to workers");
                Parallel.ForEach(Workers, worker => {
                    var tjob = new Job {
                        Nonce = job.Nonce,
                        Difficulty = job.Difficulty,
                        CancellationToken = TokenSource.Token
                    };

                    worker.Add(tjob);
                });
            }
        }
    }
}
