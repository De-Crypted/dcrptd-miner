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
        public IConfiguration Configuration { get; }
        public ILogger<WorkerManager> Logger { get; }
        private GpuWorker GpuWorker { get; }
        private CancellationTokenSource ThreadSource = new CancellationTokenSource();

        public WorkerManager(Channels channels, IConfiguration configuration, ILogger<WorkerManager> logger, ILoggerFactory loggerFactory)
        {
            Channels = channels ?? throw new ArgumentNullException(nameof(channels));
            Configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
            Logger = logger ?? throw new ArgumentNullException(nameof(logger));

            GpuWorker = new GpuWorker(channels, configuration, loggerFactory);
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
            }

            if (gpuEnabled) {
                GpuWorker.BuildOpenCL();
            }

            for (uint i = 0; i < threads; i++)
            {
                var id = i;
                var queue = new BlockingCollection<Job>(1);

                Thread thread = null;

                if (id == 0 && gpuEnabled) {
                    Logger.LogDebug("Starting GpuWorker thread");
                    thread = new Thread(() => GpuWorker.DoWork(id, queue, ThreadSource.Token));
                } else if (cpuEnabled) {
                    Logger.LogDebug("Starting CpuWorker[{}] thread", i);
                    thread = new Thread(() => CpuWorker.DoWork(id, queue, Channels, ThreadSource.Token));
                }

                if (thread != null) {
                    thread.IsBackground = true;
                    thread.UnsafeStart();
                }

                Workers.Add(queue);
            }

            Logger.LogDebug("Waiting for job");
            await foreach(var job in Channels.Jobs.Reader.ReadAllAsync(ThreadSource.Token)) {
                TokenSource.Cancel();
                TokenSource = new CancellationTokenSource();

                if (job.Type == JobType.STOP) {
                    Logger.LogDebug("Stop workers");
                    continue;
                }

                if (job.Type == JobType.NEW) {
                    Console.ForegroundColor = ConsoleColor.DarkYellow;
                    Console.WriteLine("{0:T}: New Job (diff {1})", DateTime.Now, job.Difficulty);
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
