using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using dcrpt_miner.OpenCL;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace dcrpt_miner 
{
    public class SHA256BmbAlgo : IAlgorithm
    {
        public static bool GPU => true;
        public static bool CPU => true;
        public static double DevFee => 0.01d;
        public static string DevWallet => "VFNCREEgY14rLCM2IlJAMUYlYiwrV1FGIlBDNEVQGFsvKlxBUyEzQDBUY1QoKFxHUyZF".AsWalletAddress();
        public string Name => "sha256bmb";

        private ILogger Logger { get; set; }
        private Channels Channels { get; set; }
        private List<BlockingCollection<Job>> Workers = new List<BlockingCollection<Job>>();
        private IConfiguration Configuration;
        private RandomNumberGenerator _global = RandomNumberGenerator.Create();
        private bool disposedValue;
        private CancellationTokenSource ThreadSource = new CancellationTokenSource();

        public SHA256BmbAlgo()
        {

        }

        public void Initialize(ILogger logger, Channels channels, ManualResetEvent PauseEvent)
        {
            var configurationBuilder = new ConfigurationBuilder();
            configurationBuilder.AddJsonFile("config.sha256bmb.json");
            configurationBuilder.AddCommandLine(Environment.GetCommandLineArgs());
            Configuration = configurationBuilder.Build().GetSection("sha256bmb");

            Logger = logger ?? throw new ArgumentNullException(nameof(logger));
            Channels = channels;

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
                    var queue = new BlockingCollection<Job>();

                    var tid = i;
                    Logger.LogDebug("Starting CpuWorker[{}] thread", tid);
                    new Thread(() => {
                        var token = ThreadSource.Token;
                        while (!token.IsCancellationRequested) {
                            var job = queue.Take(token);
                            DoCPUWork(tid, job, Channels, PauseEvent);
                        }
                    }).UnsafeStart();

                    Workers.Add(queue); 
                }
            }

            if (gpuEnabled) {
                var gpuDevices = GpuWorker.QueryDevices(Configuration, Logger);

                var gpuConfig = Configuration.GetValue<string>("gpu:device");
                if (string.IsNullOrEmpty(gpuConfig)) {
                    gpuConfig = "0";
                }
                var selectedGpus = gpuConfig.Split(',');

                StatusManager.GpuHashCount = new ulong[selectedGpus.Length];

                for (uint i = 0; i < selectedGpus.Length; i++) {
                    var queue = new BlockingCollection<Job>();
                    
                    var byId = int.TryParse(selectedGpus[i], out var deviceId);
                    var gpu = byId ? gpuDevices.Find(g => g.Id == deviceId) : gpuDevices.Find(g => g.DeviceName == selectedGpus[i]);

                    if (gpu == null) {
                        continue;
                    }

                    var tid = i;
                    Logger.LogDebug("Starting GpuWorker[{}] thread for gpu id: {}, name: {}", tid, gpu.Id, gpu.DeviceName);
                    new Thread(() => {
                        var token = ThreadSource.Token;
                        var context = InitializeGPUThread(gpu);

                        while (!token.IsCancellationRequested) {
                            var job = queue.Take(token);
                            DoGPUWork(tid, gpu, context, job, Channels, PauseEvent);
                        }

                        Cl.clReleaseMemObject(context.ConcatBuf)
                            .ThrowIfError();
                        Cl.clReleaseMemObject(context.FoundBuf)
                            .ThrowIfError();
                        Cl.clReleaseMemObject(context.CountBuf)
                            .ThrowIfError();
                    }).UnsafeStart();

                    Workers.Add(queue);
                }
            }
        }

        public void ExecuteJob(Job job)
        {
            Logger.LogDebug("Assigning job to workers");
            Parallel.ForEach(Workers, worker => {
                worker.Add(job);
            });
        }

        private unsafe void DoCPUWork(uint id, Job job, Channels channels, ManualResetEvent pauseEvent)
        {
            byte[] buffer = new byte[4];
            _global.GetBytes(buffer);
            var rand = new Random(BitConverter.ToInt32(buffer, 0));

            Span<byte> concat = stackalloc byte[64];
            Span<byte> hash = stackalloc byte[32];
            Span<byte> solution = stackalloc byte[32];

            int challengeBytes = job.Difficulty / 8;
            int remainingBits = job.Difficulty - (8 * challengeBytes);

            for (int i = 0; i < 32; i++) concat[i] = job.Nonce[i];
            for (int i = 33; i < 64; i++) concat[i] = (byte)rand.Next(0, 256);
            concat[32] = (byte)job.Difficulty;

            fixed (byte* ptr = concat, hashPtr = hash)
            {
                ulong* locPtr = (ulong*)(ptr + 33);
                uint* hPtr = (uint*)hashPtr;

                uint count = 100000;
                while (!job.CancellationToken.IsCancellationRequested)
                {
                    ++*locPtr;

                    Unmanaged.SHA256Ex(ptr, hashPtr);

                    if (checkLeadingZeroBits(hashPtr, job.Difficulty, challengeBytes, remainingBits))
                    {
                        channels.Solutions.Writer.TryWrite(concat.Slice(32).ToArray());
                    }

                    if (count == 0) {
                        StatusManager.CpuHashCount[id] += 100000;
                        count = 100000;
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

        private GpuContext InitializeGPUThread(GpuDevice device) {
            ClErrorCode error;

            var workSize = Configuration.GetValue<int>("gpu:work_size");
            Logger.LogDebug("work_size = {}", workSize);

            var workMultiplier = Configuration.GetValue<long?>("gpu:work_multiplier");
            Logger.LogDebug("work_multiplier = {}", workMultiplier);

            var intensity = Configuration.GetValue<int>("gpu:intensity", 1);
            Logger.LogDebug("intensity = {}", intensity);

            GpuWorker.Initialize(device, workSize, out var context, out var kernel);

            Cl.clGetDeviceInfo(device.Device, ClDeviceInfo.MaxWorkGroupSize, IntPtr.Zero, IntPtr.Zero, out var workGroupSize)
                .ThrowIfError();

            var workGroupSizeBuf = Marshal.AllocHGlobal(workGroupSize);

            Cl.clGetDeviceInfo(device.Device, ClDeviceInfo.MaxWorkGroupSize, workGroupSize, workGroupSizeBuf, out _)
                .ThrowIfError();

            var maxLocalSize = (long)Marshal.PtrToStructure(workGroupSizeBuf, typeof(long));

            Marshal.FreeHGlobal(workGroupSizeBuf);

            Cl.clGetKernelWorkGroupInfo(kernel, device.Device, ClKernelWorkGroupInfo.WorkGroupSize, IntPtr.Zero, IntPtr.Zero, out var kernelWorkGroupSize)
                .ThrowIfError();

            var kernelWorkGroupSizeBuf = Marshal.AllocHGlobal(kernelWorkGroupSize);

            Cl.clGetKernelWorkGroupInfo(kernel, device.Device, ClKernelWorkGroupInfo.WorkGroupSize, kernelWorkGroupSize, kernelWorkGroupSizeBuf, out _)
                .ThrowIfError();

            var kWorkGroupSize = (long)Marshal.PtrToStructure(kernelWorkGroupSizeBuf, typeof(long));

            Marshal.FreeHGlobal(kernelWorkGroupSizeBuf);

            Cl.clGetKernelWorkGroupInfo(kernel, device.Device, ClKernelWorkGroupInfo.WorkGroupSize, IntPtr.Zero, IntPtr.Zero, out var preferredWorkGroupSizeMultipleSize)
                .ThrowIfError();

            var preferredWorkGroupSizeMultipleSizeBuf = Marshal.AllocHGlobal(preferredWorkGroupSizeMultipleSize);

            Cl.clGetKernelWorkGroupInfo(kernel, device.Device, ClKernelWorkGroupInfo.PreferredWorkGroupSizeMultiple, preferredWorkGroupSizeMultipleSize, preferredWorkGroupSizeMultipleSizeBuf, out _)
                .ThrowIfError();

            var kPreferredWorkGroupSizeMultiple = (long)Marshal.PtrToStructure(preferredWorkGroupSizeMultipleSizeBuf, typeof(long));

            Marshal.FreeHGlobal(preferredWorkGroupSizeMultipleSizeBuf);

            if (workMultiplier == null) {
                workMultiplier = maxLocalSize;
            }

            Logger.LogDebug("platform, {}, devicename, {}, maxLocalSize = {}, multiplier = {}, kernelWorkGroupSize = {}, kernelPreferredWorkGroupSizeMultiple = {}",
                device.PlatformName,
                device.DeviceName,
                maxLocalSize,
                workMultiplier,
                kWorkGroupSize,
                kPreferredWorkGroupSizeMultiple);

            var concat = new byte[64];

            var concatLen = new IntPtr(concat.Length * sizeof(byte));
            var foundLen = new IntPtr(1024 * sizeof(byte));
            var countLen = new IntPtr(sizeof(int));

            var localWorkGroupSize = Math.Min(kWorkGroupSize, maxLocalSize);
            var finalLocalSize1 = localWorkGroupSize - (localWorkGroupSize % kPreferredWorkGroupSizeMultiple);

            Logger.LogDebug("dimension {} x {}", finalLocalSize1, intensity );

            var localDimension = new IntPtr[] { new IntPtr(finalLocalSize1 * intensity) };
            var globalDimension = new IntPtr[] { new IntPtr(maxLocalSize * workMultiplier.Value) };

            //var localDimension = new IntPtr[] { new IntPtr(32), new IntPtr(32) };
            //var globalDimension = new IntPtr[] { new IntPtr(32 * 32), new IntPtr(32 * 32) };

            var concatHandle = GCHandle.Alloc(null, GCHandleType.Pinned);
            var foundHandle = GCHandle.Alloc(null, GCHandleType.Pinned);
            var countHandle = GCHandle.Alloc(null, GCHandleType.Pinned);
            
            var concatBuf = Cl.clCreateBuffer(context, ClMemFlags.AllocHostPtr | ClMemFlags.ReadOnly, concatLen, concatHandle.AddrOfPinnedObject(), out error);
            error.ThrowIfError();

            var foundBuf = Cl.clCreateBuffer(context, ClMemFlags.AllocHostPtr | ClMemFlags.ReadWrite, foundLen, foundHandle.AddrOfPinnedObject(), out error);
            error.ThrowIfError();

            var countBuf = Cl.clCreateBuffer(context, ClMemFlags.AllocHostPtr | ClMemFlags.ReadWrite, countLen, countHandle.AddrOfPinnedObject(), out error);
            error.ThrowIfError();

            var cmdQueue = Cl.clCreateCommandQueue(context, device.Device, 0, out error);
            error.ThrowIfError();

            concatHandle.Free();
            foundHandle.Free();
            countHandle.Free();

            var concatBufHandle = GCHandle.Alloc(concatBuf, GCHandleType.Pinned);
            var foundBufHandle = GCHandle.Alloc(foundBuf, GCHandleType.Pinned);
            var countBufHandle = GCHandle.Alloc(countBuf, GCHandleType.Pinned);

            Cl.clSetKernelArg(kernel, 0, (IntPtr)IntPtr.Size, concatBufHandle.AddrOfPinnedObject())
                .ThrowIfError();
            Cl.clSetKernelArg(kernel, 1, (IntPtr)IntPtr.Size, foundBufHandle.AddrOfPinnedObject())
                .ThrowIfError();
            Cl.clSetKernelArg(kernel, 2, (IntPtr)IntPtr.Size, countBufHandle.AddrOfPinnedObject())
                .ThrowIfError();
            //Cl.clSetKernelArg(kernel, 3, new IntPtr(1024 * 32 * sizeof(byte)), IntPtr.Zero)
            //    .ThrowIfError();

            concatBufHandle.Free();
            foundBufHandle.Free();
            countBufHandle.Free();

            return new GpuContext {
                ConcatBuf = concatBuf,
                ConcatLen = concatLen,
                CountBuf = countBuf,
                CountLen = countLen,
                FoundBuf = foundBuf,
                FoundLen = foundLen,
                CmdQueue = cmdQueue,
                Kernel = kernel,
                LocalDimension = localDimension,
                GlobalDimension = globalDimension,
                WorkSize = ((uint)workSize),
                WorkMultiplier = ((uint?)workMultiplier).Value
            };
        }

        private void DoGPUWork(uint id, GpuDevice device, GpuContext context, Job job, Channels channels, ManualResetEvent pauseEvent) 
        {
           ClErrorCode error;

            byte[] buffer = new byte[4];
            _global.GetBytes(buffer);
            var rand = new Random(BitConverter.ToInt32(buffer, 0));

            var concat = new byte[64];
            var extranonce = new byte[32];

            extranonce[0] = (byte)job.Difficulty;
            for (int i = 1; i < 32; i++)  {
                extranonce[i] = (byte)rand.Next(0, 256);
            }

            for (int i = 0; i < 32; i++) {
                concat[i] = job.Nonce[i];
                concat[i + 32] = extranonce[i];
            }

            bool isNVIDIA = device.PlatformName.Contains("NVIDIA");

            var buf0 = Cl.clEnqueueMapBuffer(context.CmdQueue, context.ConcatBuf, true, ClMapFlags.Write, IntPtr.Zero, context.ConcatLen, 0, null, out var ev, out var buf0Err);
            Cl.clReleaseEvent(ev);
            var buf1 = Cl.clEnqueueMapBuffer(context.CmdQueue, context.FoundBuf, true, ClMapFlags.Read | ClMapFlags.Write, IntPtr.Zero, context.FoundLen, 0, null, out ev, out var buf1Err);
            Cl.clReleaseEvent(ev);
            var buf2 = Cl.clEnqueueMapBuffer(context.CmdQueue, context.CountBuf, true, ClMapFlags.Read | ClMapFlags.Write, IntPtr.Zero, context.CountLen, 0, null, out ev, out var buf2Err);
            Cl.clReleaseEvent(ev);

            buf0Err.ThrowIfError();
            buf1Err.ThrowIfError();
            buf2Err.ThrowIfError();

            Marshal.Copy(concat, 0, buf0, concat.Length);
            Marshal.WriteInt32(buf2, 0);

            Cl.clEnqueueUnmapMemObject(context.CmdQueue, context.ConcatBuf, buf0, 0, null, out ev)
                .ThrowIfError();
            Cl.clReleaseEvent(ev);
            Cl.clEnqueueUnmapMemObject(context.CmdQueue, context.FoundBuf, buf1, 0, null, out ev)
                .ThrowIfError();
            Cl.clReleaseEvent(ev);
            Cl.clEnqueueUnmapMemObject(context.CmdQueue, context.CountBuf, buf2, 0, null, out ev)
                .ThrowIfError();
            Cl.clReleaseEvent(ev);

            Cl.clFinish(context.CmdQueue)
                .ThrowIfError();

            int executionTimeMs = 200;

            while(!job.CancellationToken.IsCancellationRequested) {
                var start = DateTime.Now;

                error = Cl.clEnqueueNDRangeKernel(context.CmdQueue, context.Kernel, (uint)context.LocalDimension.Length, null, context.GlobalDimension, null, 0, null, out ev);
                Cl.clReleaseEvent(ev);

                buf0 = Cl.clEnqueueMapBuffer(context.CmdQueue, context.ConcatBuf, false, ClMapFlags.Write, IntPtr.Zero, context.ConcatLen, 0, null, out ev, out buf0Err);
                Cl.clReleaseEvent(ev);
                buf1 = Cl.clEnqueueMapBuffer(context.CmdQueue, context.FoundBuf, false, ClMapFlags.Read | ClMapFlags.Write, IntPtr.Zero, context.FoundLen, 0, null, out ev, out buf1Err);
                Cl.clReleaseEvent(ev);
                buf2 = Cl.clEnqueueMapBuffer(context.CmdQueue, context.CountBuf, false, ClMapFlags.Read | ClMapFlags.Write, IntPtr.Zero, context.CountLen, 0, null, out var clevent, out buf2Err);

                Cl.clFlush(context.CmdQueue)
                    .ThrowIfError();

                if (isNVIDIA) {
                    var maxWaitTime = Math.Max(executionTimeMs - 50, 1);
                    Cl.NvidiaWait(clevent, maxWaitTime);
                }

                Cl.clFinish(context.CmdQueue)
                    .ThrowIfError();

                var end = DateTime.Now;
                executionTimeMs = (int)(end - start).TotalMilliseconds;

                error.ThrowIfError();
                buf0Err.ThrowIfError();
                buf1Err.ThrowIfError();
                buf2Err.ThrowIfError();

                var count = Marshal.ReadInt32(buf2, 0);

                if (count > 0) {
                    if (job.CancellationToken.IsCancellationRequested) {
                        Cl.clEnqueueUnmapMemObject(context.CmdQueue, context.ConcatBuf, buf0, 0, null, out ev)
                            .ThrowIfError();
                        Cl.clReleaseEvent(ev);
                        Cl.clEnqueueUnmapMemObject(context.CmdQueue, context.FoundBuf, buf1, 0, null, out ev)
                            .ThrowIfError();
                        Cl.clReleaseEvent(ev);
                        Cl.clEnqueueUnmapMemObject(context.CmdQueue, context.CountBuf, buf2, 0, null, out ev)
                            .ThrowIfError();
                        Cl.clReleaseEvent(ev);
                        break;
                    }

                    for (int i = 0; i < count; i++) {
                        if (job.CancellationToken.IsCancellationRequested) {
                            var shares = Interlocked.Increment(ref StatusManager.Shares);
                            Interlocked.Increment(ref StatusManager.DroppedShares);

                            Console.ForegroundColor = ConsoleColor.DarkRed;
                            Console.WriteLine("{0:T}: Share #{2} dropped", DateTime.Now, shares);
                            Console.ResetColor();

                            continue;
                        }

                        var solution = new byte[32];

                        for (int x = 0; x < 32; x++) {
                            solution[x] = Marshal.ReadByte(buf1, i * 32 + x);
                        }

                        Logger.LogDebug("Found solution, nonce = {}", solution.AsString());
                        channels.Solutions.Writer.TryWrite(solution);
                    }

                    Marshal.WriteByte(buf2, 0, 0);
                }

                // increment nonce
                Marshal.WriteInt64(buf0, 33, Marshal.ReadInt64(buf0, 33) + 1);

                Cl.clEnqueueUnmapMemObject(context.CmdQueue, context.ConcatBuf, buf0, 0, null, out ev)
                    .ThrowIfError();
                Cl.clReleaseEvent(ev);
                Cl.clEnqueueUnmapMemObject(context.CmdQueue, context.FoundBuf, buf1, 0, null, out ev)
                    .ThrowIfError();
                Cl.clReleaseEvent(ev);
                Cl.clEnqueueUnmapMemObject(context.CmdQueue, context.CountBuf, buf2, 0, null, out ev)
                    .ThrowIfError();
                Cl.clReleaseEvent(ev);

                StatusManager.GpuHashCount[id] += (ulong)(context.GlobalDimension[0].ToInt64() * (long)context.WorkSize);

                pauseEvent.WaitOne();
            }
        }
                
        // TODO: Move to util class or something??
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private unsafe bool checkLeadingZeroBits(byte* hash, int challengeSize, int challengeBytes, int remainingBits) {
            for (int i = 0; i < challengeBytes; i++) {
                if (hash[i] != 0) return false;
            }

            if (remainingBits > 0) return hash[challengeBytes]>>(8-remainingBits) == 0;
            else return true;
        }

        unsafe class Unmanaged
        {
            [DllImport("Algorithms/sha256bmb/sha256_lib", ExactSpelling = true)]
            [SuppressGCTransition]
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public static extern void SHA256Ex(byte* buffer, byte* output);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (disposing)
                {
                    // TODO: dispose managed state (managed objects)
                    ThreadSource.Cancel();
                }

                // TODO: free unmanaged resources (unmanaged objects) and override finalizer
                // TODO: set large fields to null
                disposedValue = true;
            }
        }

        // // TODO: override finalizer only if 'Dispose(bool disposing)' has code to free unmanaged resources
        // ~SHA256BmbAlgo()
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
    }

    class GpuContext
    {
        public IntPtr ConcatBuf { get; set; }
        public IntPtr ConcatLen { get; set; }
        public IntPtr CountBuf { get; set; }
        public IntPtr CountLen { get; set; }
        public IntPtr FoundBuf { get; set; }
        public IntPtr FoundLen { get; set; }
        public IntPtr CmdQueue { get; set; }
        public IntPtr Kernel { get; set; }
        public IntPtr[] LocalDimension { get; set; }
        public IntPtr[] GlobalDimension { get; set; }
        public uint WorkSize { get; set; }
        public uint WorkMultiplier { get; set; }
    }
}