using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace dcrpt_miner 
{
    public class ConnectionManager : IHostedService
    {
        public IServiceProvider ServiceProvider { get; }
        public Channels Channels { get; }
        public IConfiguration Configuration { get; }
        public ILogger<ConnectionManager> Logger { get; }

        private IConnectionProvider CurrentProvider { get; set; }
        private CancellationTokenSource ThreadSource = new CancellationTokenSource();

        public ConnectionManager(IServiceProvider serviceProvider, Channels channels, IConfiguration configuration, ILogger<ConnectionManager> logger)
        {
            ServiceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
            Channels = channels ?? throw new System.ArgumentNullException(nameof(channels));
            Configuration = configuration ?? throw new System.ArgumentNullException(nameof(configuration));
            Logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            Logger.LogDebug("Starting ConnectionManager thread");

            new Thread(async () => await HandleSubmissions(ThreadSource.Token))
                .UnsafeStart();

            new Thread(async () => {
                var token = ThreadSource.Token;
                var urls = Configuration.GetSection("url").Get<List<string>>();

                if (urls == null) {
                    urls = new List<string>();
                }

                // provides simple input from cmdline (no need to input with array index) and backwards compatibility
                var url = Configuration.GetValue<string>("url");

                if (url != null && !string.IsNullOrEmpty(url)) {
                    urls.Clear();
                    urls.Add(url);
                } 

                if (urls.Count == 0) {
                    SafeConsole.WriteLine(ConsoleColor.DarkRed, "No url set in config.json or --url argument!");
                    Process.GetCurrentProcess().Kill();
                    return;
                }

                var retryAction = Configuration.GetValue<RetryAction>("action_after_retries_done");
                var keepReconnecting = retryAction == RetryAction.RETRY;

                do {
                    foreach (var _url in urls) {
                        SafeConsole.WriteLine(ConsoleColor.DarkGray, "{0:T}: Connecting to {1}", DateTime.Now, _url);

                        CurrentProvider = GetConnectionProvider(_url);

                        try {
                            await CurrentProvider.RunAsync(_url);
                        } catch (Exception ex) {
                            SafeConsole.WriteLine(ConsoleColor.DarkRed, ex.ToString());
                        }

                        SafeConsole.WriteLine(ConsoleColor.DarkGray, "{0:T}: Disconnected from {1}", DateTime.Now, _url);
                    }

                    token.WaitHandle.WaitOne(TimeSpan.FromSeconds(5));
                } while (keepReconnecting);

                SafeConsole.WriteLine(ConsoleColor.DarkRed, "{0:T}: Miner shutting down...", DateTime.Now);
                Process.GetCurrentProcess().Kill();
            }).UnsafeStart();

            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            Logger.LogDebug("Stopping ConnectionManager thread");
            ThreadSource.Cancel();
            return Task.CompletedTask;
        }

        private async Task HandleSubmissions(CancellationToken cancellationToken)
        {
            var sw = new Stopwatch();

            Logger.LogDebug("Thread idle...");
            try {
                await foreach(var solution in Channels.Solutions.Reader.ReadAllAsync(cancellationToken)) {
                    try {
                        Logger.LogDebug("Submitting solution (nonce = {})", solution.AsString());
                        var shares = Interlocked.Increment(ref StatusManager.Shares);

                        sw.Start();
                        var result = await CurrentProvider.SubmitAsync(solution);
                        sw.Stop();

                        switch(result) {
                            case SubmitResult.ACCEPTED:
                                Interlocked.Increment(ref StatusManager.AcceptedShares);
                                SafeConsole.WriteLine(ConsoleColor.DarkGreen, "{0:T}: {1} #{2} accepted ({3} ms)", DateTime.Now, CurrentProvider.SolutionName, shares, sw.Elapsed.Milliseconds);
                                break;
                            case SubmitResult.REJECTED:
                                Interlocked.Increment(ref StatusManager.RejectedShares);
                                SafeConsole.WriteLine(ConsoleColor.DarkRed, "{0:T}: {1} #{2} rejected ({3} ms)", DateTime.Now, CurrentProvider.SolutionName, shares, sw.Elapsed.Milliseconds);
                                break;
                            case SubmitResult.TIMEOUT:
                                SafeConsole.WriteLine(ConsoleColor.DarkRed, "{0:T}: Failed to submit {1} (ERR_ACK_TIMEOUT)", DateTime.Now, CurrentProvider.SolutionName);
                                break;
                        }

                        sw.Reset();
                        Logger.LogDebug("Submit done");
                    } catch (Exception) {
                        SafeConsole.WriteLine(ConsoleColor.DarkRed, "{0:T}: Failed to submit share (ERR_CONN_FAILED)", DateTime.Now);
                    }
                }
            } catch(System.OperationCanceledException) {
                Logger.LogDebug("Solution reader cancelled. Shutting down...");
            }

            Logger.LogDebug("Thread exit!");
        }

        private IConnectionProvider GetConnectionProvider(string url) {
            switch (url.Substring(0, url.IndexOf(':'))) {
                case "dcrpt":
                    return (IConnectionProvider)ServiceProvider.GetService(typeof(DcrptConnectionProvider));
                case "shifu":
                    return (IConnectionProvider)ServiceProvider.GetService(typeof(ShifuPoolConnectionProvider));
                case "bamboo":
                    return (IConnectionProvider)ServiceProvider.GetService(typeof(BambooNodeConnectionProvider));
                default:
                    throw new Exception("Unknown protocol");
            }
        }
    }

    public enum RetryAction {
        RETRY,
        SHUTDOWN
    }
}