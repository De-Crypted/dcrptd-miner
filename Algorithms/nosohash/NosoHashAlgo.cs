using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Threading;
using dcrpt_miner.OpenCL;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace dcrpt_miner.NosoHash;

public class NosoHashAlgo : IAlgorithm
{
    public string Name => "NosoHash";
    public static bool GPU => true;
    public static bool CPU => false;

    private IConfiguration Configuration;
    private ILogger Logger;
    private RandomNumberGenerator _global = RandomNumberGenerator.Create();

    public void ExecuteJob(Job job)
    {
        throw new System.NotImplementedException();
    }

    public void Initialize(ILogger logger, Channels channels, ManualResetEvent PauseEvent)
    {
        throw new System.NotImplementedException();
    }

    public void RunBenchmark()
    {
        var configurationBuilder = new ConfigurationBuilder();
        configurationBuilder.AddJsonFile("config.sha256bmb.json");
        configurationBuilder.AddCommandLine(Environment.GetCommandLineArgs());
        Configuration = configurationBuilder.Build().GetSection("sha256bmb");

        Logger = LoggerFactory.Create(configure => configure.AddConsole()).CreateLogger<NosoHashAlgo>();

        var gpuDevices = GpuWorker.QueryDevices(Configuration, Logger);

        var gpuConfig = Configuration.GetValue<string>("gpu:device");
        if (string.IsNullOrEmpty(gpuConfig)) {
            gpuConfig = "0";
        }
        var selectedGpus = gpuConfig.Split(',');

        Console.WriteLine("Running Nosohash Benchmark");
        Console.WriteLine("Device\t\tHashrate");

        for (uint i = 0; i < selectedGpus.Length; i++) {
            var byId = int.TryParse(selectedGpus[i], out var deviceId);
            var gpu = byId ? gpuDevices.Find(g => g.Id == deviceId) : gpuDevices.Find(g => g.DeviceName == selectedGpus[i]);

            var context = InitializeGPUThread(gpu);

            var sw = new Stopwatch();
            sw.Start();

            for (int x = 0; x < 1; x++)
            {
                DoGPUWork(i, gpu, context);
            }

            sw.Stop();

            var hashes = 1048576L * 1 * 1024 / sw.Elapsed.TotalSeconds;
            StatusManager.CalculateUnit(hashes, out var hashrate, out var unit);

                Console.WriteLine("\t\t{0:N2} {1}", 
                    hashrate, unit);
        }
    }

    public void Dispose()
    {
        throw new System.NotImplementedException();
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

    //private void DoGPUWork(uint id, GpuDevice device, GpuContext context, Job job, Channels channels, ManualResetEvent pauseEvent) 
    private void DoGPUWork(uint id, GpuDevice device, GpuContext context)
    {
        ClErrorCode error;

        byte[] buffer = new byte[4];
        _global.GetBytes(buffer);
        var rand = new Random(BitConverter.ToInt32(buffer, 0));

        var concat = new byte[64];
        var extranonce = new byte[32];

        // extranonce[0] = (byte)job.Difficulty;
        for (int i = 1; i < 32; i++)  {
            extranonce[i] = (byte)rand.Next(0, 256);
        }

        for (int i = 0; i < 32; i++) {
            //concat[i] = job.Nonce[i];
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

        //while(!job.CancellationToken.IsCancellationRequested) {
            var start = DateTime.Now;

            error = Cl.clEnqueueNDRangeKernel(context.CmdQueue, context.Kernel, (uint)1, new IntPtr[] {new IntPtr(256)}, context.GlobalDimension, null, 0, null, out ev);
            Cl.clReleaseEvent(ev);

            buf0 = Cl.clEnqueueMapBuffer(context.CmdQueue, context.ConcatBuf, false, ClMapFlags.Write, IntPtr.Zero, context.ConcatLen, 0, null, out ev, out buf0Err);
            Cl.clReleaseEvent(ev);
            buf1 = Cl.clEnqueueMapBuffer(context.CmdQueue, context.FoundBuf, false, ClMapFlags.Read | ClMapFlags.Write, IntPtr.Zero, context.FoundLen, 0, null, out ev, out buf1Err);
            Cl.clReleaseEvent(ev);
            buf2 = Cl.clEnqueueMapBuffer(context.CmdQueue, context.CountBuf, false, ClMapFlags.Read | ClMapFlags.Write, IntPtr.Zero, context.CountLen, 0, null, out var clevent, out buf2Err);

            Cl.clFlush(context.CmdQueue)
                .ThrowIfError();

            /*if (isNVIDIA) {
                var maxWaitTime = Math.Max(executionTimeMs - 50, 1);
                Cl.NvidiaWait(clevent, maxWaitTime);
            }*/

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
                /*if (job.CancellationToken.IsCancellationRequested) {
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
                }*/

                /*for (int i = 0; i < count; i++) {
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
                    channels.Solutions.Writer.TryWrite(new JobSolution
                    {
                        Nonce = job.Nonce,
                        Solution = solution
                    });
                }*/

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

            // StatusManager.GpuHashCount[id] += (ulong)(context.GlobalDimension[0].ToInt64() * (long)context.WorkSize);

            //pauseEvent.WaitOne();
        //}
    }
}