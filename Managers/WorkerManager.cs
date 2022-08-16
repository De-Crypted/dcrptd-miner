using System;
using System.Threading;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Collections.Concurrent;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace dcrpt_miner
{
    public class WorkerManager : IHostedService
    {
        private CancellationTokenSource TokenSource = new CancellationTokenSource();
        private List<BlockingCollection<Job>> Workers = new List<BlockingCollection<Job>>();
        public Channels Channels { get; }
        public IConfiguration Configuration { get; }
        public ILogger<WorkerManager> Logger { get; }
        public ILoggerFactory LoggerFactory { get; }

        private static ManualResetEvent PauseEvent = new ManualResetEvent(true);
        private CancellationTokenSource ThreadSource = new CancellationTokenSource();

        public WorkerManager(Channels channels, IConfiguration configuration, ILogger<WorkerManager> logger, ILoggerFactory loggerFactory)
        {
            Channels = channels ?? throw new ArgumentNullException(nameof(channels));
            Configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
            Logger = logger ?? throw new ArgumentNullException(nameof(logger));
            LoggerFactory = loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory));
        }

        public static void PauseWorkers()
        {
            PauseEvent.Reset();
            SafeConsole.WriteLine(ConsoleColor.DarkGray, "{0:T}: Paused, press 'r' to resume mining", DateTime.Now);
        }

        public static void ResumeWorkers()
        {
            PauseEvent.Set();
            SafeConsole.WriteLine(ConsoleColor.DarkGray, "{0:T}: Mining resumed", DateTime.Now);
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
            PauseEvent.Set();
            return Task.CompletedTask;
        }

        private async Task JobHandler(CancellationToken token)
        {
            IAlgorithm algo = null;

            Logger.LogDebug("Waiting for job");
            await foreach(var job in Channels.Jobs.Reader.ReadAllAsync(token)) {
                TokenSource.Cancel();
                TokenSource.Dispose();
                TokenSource = new CancellationTokenSource();

                if (job.Type == JobType.STOP) {
                    Logger.LogDebug("Stop workers");
                    continue;
                }

                if (algo == null || algo.GetType() != job.Algorithm) {
                    if (algo != null) {
                        algo.Dispose();
                    }

                    algo = (IAlgorithm)Activator.CreateInstance(job.Algorithm);
                    algo.Initialize(LoggerFactory.CreateLogger(algo.GetType()), Channels, PauseEvent);

                    SafeConsole.WriteLine(ConsoleColor.DarkGray, "Algorithm: {0}", algo.Name);
                    StatusManager.RegisterAlgorith(algo);
                }

                if (job.Type == JobType.NEW) {
                    Console.ForegroundColor = ConsoleColor.DarkYellow;
                    Console.WriteLine("{0:T}: New {1} {2} (diff {3})", DateTime.Now, job.Name, job.Id.ToLowerInvariant(), job.Difficulty.ToString("0.##"));
                    Console.ResetColor();
                }

                job.CancellationToken = TokenSource.Token;
                Logger.LogDebug("Assigning job to workers");

                algo.ExecuteJob(job);
            }

            if (algo != null) {
                algo.Dispose();
            }
        }
    }
}
