using System;
using System.Threading.Tasks;
using System.Runtime.InteropServices;
using System.Diagnostics;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using System.Runtime.CompilerServices;
using Microsoft.AspNetCore.Hosting;
using System.Net;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Http.Features;
using System.Threading;
using System.Reflection;

namespace dcrpt_miner
{
    class Program
    {
        static async Task Main(string[] args)
        {
            var configuration = new ConfigurationBuilder()
                .AddCommandLine(args)
                .AddJsonFile($"config.json", optional: true, reloadOnChange: true)
                .Build();

            var benchmark = configuration.GetValue<string>("benchmark");

            if (!string.IsNullOrEmpty(benchmark)) {
                Type tAlgo;

                switch (benchmark) {
                    case "sha256bmb":
                        tAlgo = typeof(SHA256BmbAlgo);
                    break;
                    case "pufferfish2bmb":
                        tAlgo = typeof(Pufferfish2BmbAlgo);
                    break;
                    default:
                        // print possible algorithms
                    return;
                }

                var algo = (IAlgorithm)Activator.CreateInstance(tAlgo);
                algo.RunBenchmark();
                return;
            }

            var apiEnabled = configuration.GetValue<bool>("api:enabled");

            var host = Host.CreateDefaultBuilder(args)
                .ConfigureAppConfiguration((hostingContext, configuration) => {
                    configuration.Sources.Clear();

                    configuration.AddJsonFile("config.json");
                    configuration.AddCommandLine(args);
                })
                .ConfigureWebHost(webHost => {
                    webHost.UseStartup<Startup>();

                    if (!apiEnabled) {
                        webHost.UseServer(new NoopServer());
                        return;
                    }

                    webHost.UseKestrel((webHostBuilder, kestrel) => {
                        var port = webHostBuilder.Configuration.GetValue<int>("api:port");
                        var localhostOnly = webHostBuilder.Configuration.GetValue<bool>("api:localhost_only");

                        if (localhostOnly) {
                            kestrel.ListenLocalhost(port);
                        } else {
                            kestrel.ListenAnyIP(port);
                        }
                    });
                });

            var version = Assembly.GetEntryAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>().InformationalVersion;
            Console.WriteLine("dcrptd miner v" + version.ToString());

            Console.Title = "dcrptd miner " + version.ToString();
            await host.StartAsync();

            Console.TreatControlCAsInput = true;
            while (true) {
                var key = Console.ReadKey(true);

                switch (key.Key) {
                    case ConsoleKey.C:
                        if (key.Modifiers == ConsoleModifiers.Control)  {
                            Process.GetCurrentProcess().Kill();
                        }
                    break;
                    case ConsoleKey.H:
                        StatusManager.PrintHelp();
                    break;
                    case ConsoleKey.S:
                        StatusManager.DoPeriodicReport();
                    break;
                    case ConsoleKey.P:
                        WorkerManager.PauseWorkers();
                    break;
                    case ConsoleKey.R:
                        WorkerManager.ResumeWorkers();
                    break;
                }
            }
        }
    }

    class NoopServer : IServer
    {
        public IFeatureCollection Features => new FeatureCollection();

        public void Dispose() { }

        public Task StartAsync<TContext>(IHttpApplication<TContext> application, CancellationToken cancellationToken) where TContext : notnull
            => Task.CompletedTask;

        public Task StopAsync(CancellationToken cancellationToken) 
            => Task.CompletedTask;

        
    }
}
