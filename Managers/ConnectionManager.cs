using System;
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
        public IConnectionProvider ConnectionProvider { get; }
        public Channels Channels { get; }
        public IConfiguration Configuration { get; }
        public ILogger<ConnectionManager> Logger { get; }

        private CancellationTokenSource ThreadSource = new CancellationTokenSource();

        public ConnectionManager(IConnectionProvider connectionProvider, Channels channels, IConfiguration configuration, ILogger<ConnectionManager> logger)
        {
            ConnectionProvider = connectionProvider ?? throw new System.ArgumentNullException(nameof(connectionProvider));
            Channels = channels ?? throw new System.ArgumentNullException(nameof(channels));
            Configuration = configuration ?? throw new System.ArgumentNullException(nameof(configuration));
            Logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            Logger.LogDebug("Starting ConnectionManager thread");
            new Thread(async () => await HandleSubmissions(ThreadSource.Token))
                .UnsafeStart();

            return ConnectionProvider.StartAsync();
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
                        ++Program.Shares;

                        sw.Start();
                        var result = await ConnectionProvider.SubmitAsync(solution);
                        sw.Stop();

                        switch(result) {
                            case SubmitResult.ACCEPTED:
                                ++Program.AcceptedShares;

                                Console.ForegroundColor = ConsoleColor.DarkGreen;
                                Console.WriteLine("{0:T}: {1} #{2} accepted ({3} ms)", DateTime.Now, ConnectionProvider.SolutionName, Program.Shares, sw.Elapsed.Milliseconds);
                                Console.ResetColor();
                                break;
                            case SubmitResult.REJECTED:
                                ++Program.RejectedShares;

                                Console.ForegroundColor = ConsoleColor.DarkRed;
                                Console.WriteLine("{0:T}: {1} #{2} rejected ({3} ms)", DateTime.Now, ConnectionProvider.SolutionName, Program.Shares, sw.Elapsed.Milliseconds);
                                Console.ResetColor();
                                break;
                            case SubmitResult.TIMEOUT:
                                Console.ForegroundColor = ConsoleColor.DarkRed;
                                Console.WriteLine("{0:T}: Failed to submit {1} (ERR_ACK_TIMEOUT)", DateTime.Now, ConnectionProvider.SolutionName);
                                Console.ResetColor();
                                break;
                        }

                        sw.Reset();
                        Logger.LogDebug("Submit done");
                    } catch (Exception) {
                        Console.ForegroundColor = ConsoleColor.DarkRed;
                        Console.WriteLine("{0:T}: Failed to submit share (ERR_CONN_FAILED)", DateTime.Now);
                        Console.ResetColor();
                    }
                }
            } catch(System.OperationCanceledException) {
                Logger.LogDebug("Solution reader cancelled. Shutting down...");
            } catch (Exception ex) {
                throw new Exception("SubmissionHandler threw error", ex);
            }

            Logger.LogDebug("Thread exit!");
        }
    }
}