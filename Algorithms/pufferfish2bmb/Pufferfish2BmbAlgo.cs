using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using LibreHardwareMonitor.Hardware;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using static dcrpt_miner.StatusManager;

namespace dcrpt_miner 
{
    /*static class Native32
    {
        [DllImport("kernel32.dll")]
        public static extern IntPtr GetCurrentThread();

		[DllImport("kernel32")]
		public static extern int GetCurrentThreadId();

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        public static extern int GetCurrentProcessorNumber();

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        public static extern UIntPtr SetThreadAffinityMask(IntPtr handle, UIntPtr mask);
    }*/

    public class Pufferfish2BmbAlgo : IAlgorithm
    {
        public static bool GPU => false;
        public static bool CPU => true;
        public static double DevFee => 0.015d;
        public static string DevWallet => "VFNCREEgY14rLCM2IlJAMUYlYiwrV1FGIlBDNEVQGFsvKlxBUyEzQDBUY1QoKFxHUyZF".AsWalletAddress();
        public string Name => "pufferfish2bmb";

        private List<BlockingCollection<Job>> Workers = new List<BlockingCollection<Job>>();
        private IConfiguration Configuration;
        private RandomNumberGenerator _global = RandomNumberGenerator.Create();
        private bool disposedValue;
        private CancellationTokenSource ThreadSource = new CancellationTokenSource();


        public Pufferfish2BmbAlgo()
        {

        }

        public void Initialize(ILogger logger, Channels channels, ManualResetEvent PauseEvent)
        {
            var configurationBuilder = new ConfigurationBuilder();
            configurationBuilder.AddJsonFile("config.pufferfish2bmb.json");
            configurationBuilder.AddCommandLine(Environment.GetCommandLineArgs());
            Configuration = configurationBuilder.Build().GetSection("pufferfish2bmb");

            var threads = Configuration.GetValue<int>("threads");

            if (threads <= 0) {
                threads = Environment.ProcessorCount;
            }

            StatusManager.CpuHashCount = new ulong[threads];

            for (uint i = 0; i < threads; i++) {
                var queue = new BlockingCollection<Job>();

                var tid = i;
                logger.LogDebug("Starting CpuWorker[{}] thread", tid);
                new Thread(() => {
                    Thread.BeginThreadAffinity();
                    var token = ThreadSource.Token;
                    while (!token.IsCancellationRequested) {
                        var job = queue.Take(token);
                        DoCPUWork(tid, job, channels, PauseEvent);
                    }
                }).UnsafeStart();

                Workers.Add(queue); 
            }
        }

        public void ExecuteJob(Job job)
        {
            Parallel.ForEach(Workers, worker => {
                worker.Add(job);
            });
        }

        private unsafe void DoCPUWork(uint id, Job job, Channels channels, ManualResetEvent pauseEvent)
        {
            byte[] buffer = new byte[4];
            _global.GetBytes(buffer);
            var rand = new Random(BitConverter.ToInt32(buffer, 0));

            Span<byte> concat = new byte[64];
            Span<byte> hash = new byte[119]; // TODO: verify this matches PF_HASHSPACE in all cases
            Span<byte> solution = new byte[32];

            int challengeBytes = job.Difficulty / 8;
            int remainingBits = job.Difficulty - (8 * challengeBytes);

            for (int i = 0; i < 32; i++) concat[i] = job.Nonce[i];
            for (int i = 33; i < 64; i++) concat[i] = (byte)rand.Next(0, 256);
            concat[32] = (byte)job.Difficulty;

            using (SHA256 sha256 = SHA256.Create())
            fixed (byte* ptr = concat, hashPtr = hash)
            {
                ulong* locPtr = (ulong*)(ptr + 33);

                uint count = 10;
                while (!job.CancellationToken.IsCancellationRequested)
                {
                    ++*locPtr;

                    Unmanaged.pf_newhash(ptr, 64, 0, 8, hashPtr);
                    var sha256Hash = sha256.ComputeHash(hash.ToArray());

                    if (checkLeadingZeroBits(sha256Hash, challengeBytes, remainingBits))
                    {
                        channels.Solutions.Writer.TryWrite(concat.Slice(32).ToArray());
                    }

                    if (count == 0) {
                        StatusManager.CpuHashCount[id] += 10;

                        count = 10;
                        if (id < 2) {
                            // Be nice to other threads and processes
                            Thread.Sleep(1);
                        }

                        pauseEvent.WaitOne();
                    }

                    --count;
                }
            }
        }

        // TODO: Move to util class or something??
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private unsafe bool checkLeadingZeroBits(byte[] hash, int challengeBytes, int remainingBits) {
            for (int i = 0; i < challengeBytes; i++) {
                if (hash[i] != 0) return false;
            }

            if (remainingBits > 0) return hash[challengeBytes]>>(8-remainingBits) == 0;
            else return true;
        }

        unsafe class Unmanaged
        {
            [DllImport("Algorithms/pufferfish2bmb/pufferfish2", ExactSpelling = true)]
            [SuppressGCTransition]
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public static extern int pf_newhash(byte* pass, int pass_sz, int cost_t, int cost_m, byte* hash);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (disposing)
                {
                    // TODO: dispose managed state (managed objects)
                }

                // TODO: free unmanaged resources (unmanaged objects) and override finalizer
                // TODO: set large fields to null
                disposedValue = true;
            }
        }

        // // TODO: override finalizer only if 'Dispose(bool disposing)' has code to free unmanaged resources
        // ~Pufferfish2BmbAlgo()
        // {
        //     // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
        //     Dispose(disposing: false);
        // }

        public void Dispose()
        {
            // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }

        public void RunBenchmark()
        {
            Computer computer = new Computer
            {
                IsCpuEnabled = true,
                IsGpuEnabled = false,
                IsMemoryEnabled = false,
                IsMotherboardEnabled = false,
                IsControllerEnabled = false,
                IsNetworkEnabled = false,
                IsStorageEnabled = false
            };

            computer.Open();
            computer.Accept(new UpdateVisitor());

            ISensor powerSensor = null;

            foreach (IHardware hardware in computer.Hardware)
            {
                foreach (ISensor sensor in hardware.Sensors)
                {
                    if (sensor.SensorType == SensorType.Power) {
                        Console.WriteLine("Found power sensor {0}", sensor.Name);
                    }

                    if (sensor.SensorType == SensorType.Power && sensor.Name == "Package") {
                        powerSensor = sensor;
                    }
                }
            }

            var threads = Environment.ProcessorCount;

            Console.ForegroundColor = ConsoleColor.DarkYellow;
            Console.WriteLine("Running Benchmark");
            Console.WriteLine("If power usage is not recorded, run as administrator!");
            Console.WriteLine("Threads\t\tHashrate\tper thread\tper watt\tPower Usage\tper thread");

            var tsw = new Stopwatch();
            tsw.Start();

            for (int i = 0; i < threads; i++) {
                Console.Write("{0}", i + 1);
                var sw = new Stopwatch();
                sw.Start();

                var threadList = new List<Thread>();

                for (int x = 0; x <= i; x++) {
                    var tid = x;
                    var thread = new Thread(() => {
                        //Thread.BeginThreadAffinity();
                        /*IntPtr osThread = Native32.GetCurrentThread();

                        var bits = 0UL;
                        
                        if (tid == 0) {
                            bits = 1UL << 0;
                        }

                        if (tid == 1) {
                            bits = 1UL << 1;
                        }

                        if (tid == 0) {
                            bits = 1UL << 2;
                        }

                        if (tid == 0) {
                            bits = 1UL << 3;
                        }

                        UIntPtr mask = new UIntPtr(bits);

                        UIntPtr lastaffinity = Native32.SetThreadAffinityMask(osThread, mask);*/
                        Thread.BeginThreadAffinity();
                        RunBenchmarkThread();
                    });

                    threadList.Add(thread);

                    thread.Start();
                }

                var powerUsage = new List<float>();
                foreach (var thread in threadList) {
                    while (thread.ThreadState == System.Threading.ThreadState.Running) {
                        if (thread.IsAlive && powerSensor != null) {
                            powerSensor.Hardware.Accept(new UpdateVisitor());
                            if (powerSensor.Value.HasValue) {
                                powerUsage.Add(powerSensor.Value.Value);
                            }
                        }

                        Thread.Sleep(200);
                    }
                }

                sw.Stop();

                var hashes = (i + 1) * 1000 / sw.Elapsed.TotalSeconds;
                StatusManager.CalculateUnit(hashes, out var hashrate, out var unit);
                StatusManager.CalculateUnit(hashes / (i + 1), out var thashrate, out var tunit);

                var avgPowerUsage = powerUsage.Count > 0 ? powerUsage.Average() : 0;
                Console.WriteLine("\t\t{0:N2} {1}\t{2:N2} {3}\t{4:N2} h/w\t{5:N2}w\t\t{6:N2}w", 
                    hashrate, unit, 
                    thashrate, tunit,
                    avgPowerUsage > 0 ? hashes / avgPowerUsage : 0,
                    avgPowerUsage, 
                    avgPowerUsage / (i + 1));
            }

            computer.Close();
            tsw.Stop();
            Console.WriteLine("Benchmark completed in {0} seconds", tsw.Elapsed.TotalSeconds);
            Console.ForegroundColor = ConsoleColor.White;
        }

        public unsafe void RunBenchmarkThread()
        {
            byte[] buffer = new byte[4];
            _global.GetBytes(buffer);
            var rand = new Random(BitConverter.ToInt32(buffer, 0));

            Span<byte> concat = new byte[64];
            Span<byte> hash = new byte[119]; // TODO: verify this matches PF_HASHSPACE in all cases
            Span<byte> solution = new byte[32];

            int challengeBytes = 15 / 8;
            int remainingBits = 15 - (8 * challengeBytes);

            for (int i = 0; i < 64; i++) concat[i] = (byte)rand.Next(0, 256);

            using (SHA256 sha256 = SHA256.Create())
            fixed (byte* ptr = concat, hashPtr = hash)
            {
                ulong* locPtr = (ulong*)(ptr + 33);

                for (int i = 0; i < 1000; i++)
                {
                    ++*locPtr;

                    Unmanaged.pf_newhash(ptr, 64, 0, 8, hashPtr);
                    var sha256Hash = sha256.ComputeHash(hash.ToArray());

                    if (checkLeadingZeroBits(sha256Hash, challengeBytes, remainingBits))
                    {
                        // dummy work
                        ++*locPtr;
                    }
                }
            }
        }
    }
}
