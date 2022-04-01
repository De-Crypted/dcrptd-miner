using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using OpenCL.NetCore;
using OpenCL.NetCore.Extensions;

namespace dcrpt_miner
{
    public static partial class Cl2
    {
        [DllImport(Cl.Library)]
        private static extern IntPtr clEnqueueMapBuffer(IntPtr commandQueue,
                                                        IntPtr buffer,
                                                        Bool blockingMap,
                                                        MapFlags mapFlags,
                                                        IntPtr offset,
                                                        IntPtr cb,
                                                        uint numEventsInWaitList,
                                                        [In] [MarshalAs(UnmanagedType.LPArray, ArraySubType = UnmanagedType.SysUInt, SizeParamIndex = 6)] Event[] eventWaitList,
                                                        [Out] [MarshalAs(UnmanagedType.Struct)] out Event e,
                                                        out ErrorCode errCodeRet);

        public static IntPtr EnqueueMapBuffer(CommandQueue commandQueue,
                                                  IMem buffer,
                                                  Bool blockingMap,
                                                  MapFlags mapFlags,
                                                  IntPtr offset,
                                                  IntPtr cb,
                                                  uint numEventsInWaitList,
                                                  Event[] eventWaitList,
                                                  out Event e,
                                                  out ErrorCode errCodeRet)
        {
            var cHandle = commandQueue.GetType().GetField("_handle", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.GetField);
            var bHandle = buffer.GetType().GetField("_handle", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.GetField);

            return clEnqueueMapBuffer((IntPtr)cHandle.GetValue(commandQueue), (IntPtr)bHandle.GetValue(buffer), 
                                                     blockingMap, mapFlags, offset, cb, numEventsInWaitList, eventWaitList, out e, out errCodeRet);
        }

        [DllImport(Cl.Library)]
        public static extern ErrorCode clReleaseMemObject(IntPtr memObj);

        [DllImport(Cl.Library)]
        private static extern ErrorCode clEnqueueUnmapMemObject(IntPtr commandQueue,
                                                                IntPtr memObj,
                                                                IntPtr mappedPtr,
                                                                uint numEventsInWaitList,
                                                                [In] [MarshalAs(UnmanagedType.LPArray, ArraySubType = UnmanagedType.SysUInt, SizeParamIndex = 3)] Event[] eventWaitList,
                                                                [Out] [MarshalAs(UnmanagedType.Struct)] out Event e);
        public static ErrorCode EnqueueUnmapObject(CommandQueue commandQueue,
                                                   IMem memObj,
                                                   IntPtr mappedObject,
                                                   uint numEventsInWaitList,
                                                   Event[] eventWaitList,
                                                   out Event e)
        {
            var cHandle = commandQueue.GetType().GetField("_handle", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.GetField);
            var bHandle = memObj.GetType().GetField("_handle", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.GetField);

            return clEnqueueUnmapMemObject((IntPtr)cHandle.GetValue(commandQueue), (IntPtr)bHandle.GetValue(memObj), 
                mappedObject, numEventsInWaitList, eventWaitList, out e);
        }

        [DllImport(Cl.Library)]
        public static extern ErrorCode clSetEventCallback(Event @event, 
                                                        Int32 command_exec_callback_type, 
                                                        ComputeEventCallback pfn_notify, 
                                                        IntPtr user_data);

        public static ErrorCode SetEventCallback(Event @event, 
                                                Int32 command_exec_callback_type, 
                                                ComputeEventCallback pfn_notify, 
                                                IntPtr user_data)
        {
            return clSetEventCallback(@event, command_exec_callback_type, pfn_notify, user_data);
        }

        public delegate void ComputeEventCallback(Event eventHandle, int cmdExecStatusOrErr, IntPtr userData);

        public static void NvidiaWait(this Event @event, int milliseconds) {
            ManualResetEvent eventSignal = new ManualResetEvent(false);

            var callback = new Cl2.ComputeEventCallback((Event eventHandle, int cmdExecStatusOrErr, IntPtr userData) => {
                eventSignal.Set();
                eventSignal.Dispose();

                var handle = GCHandle.FromIntPtr(userData);
                handle.Free();

                eventHandle.Dispose();
            });

            var handle = GCHandle.Alloc(callback);
            var handlePointer = GCHandle.ToIntPtr(handle);

            Cl2.SetEventCallback(@event, 0, callback, handlePointer);

            eventSignal.WaitOne(milliseconds);
        }
    }

    class GpuWorker
    {
        private static RandomNumberGenerator _global = RandomNumberGenerator.Create();

        public static List<GpuDevice> QueryDevices(IConfiguration configuration, ILoggerFactory loggerFactory)
        {
            ErrorCode error;
            var logger = loggerFactory.CreateLogger<GpuWorker>();

            try {
                logger.LogDebug("Begin QueryDevices");
                var gpuConfig = configuration.GetValue<string>("gpu:device");
                if (string.IsNullOrEmpty(gpuConfig)) {
                    gpuConfig = "0";
                }
                var selectedGpus = gpuConfig.Split(',');

                Console.WriteLine("Detecting OpenCL devices");
                var gpuDevices = new List<GpuDevice>();

                int id = 0;

                var platforms = Cl.GetPlatformIDs(out error);
                error.ThrowIfError();

                foreach (var platform in platforms) {
                    var platformName = Cl.GetPlatformInfo(platform, PlatformInfo.Vendor, out error);
                    error.ThrowIfError();

                    var devices = Cl.GetDeviceIDs(platform, DeviceType.All, out error);
                    error.ThrowIfError();

                    foreach (var device in devices) {
                        var deviceName = Cl.GetDeviceInfo(device, DeviceInfo.Name, out error);
                        error.ThrowIfError();

                        Console.WriteLine("[{0}]: {1}{2}",  
                            id, 
                            selectedGpus.Contains(id.ToString()) || selectedGpus.Contains(deviceName.ToString()) ? "*" : "", 
                            deviceName);

                        gpuDevices.Add(new GpuDevice {
                            Platform = platform,
                            PlatformName = platformName.ToString(),
                            Device = device,
                            DeviceName = deviceName.ToString(),
                            Id = id
                        });

                        id++;
                    }
                }

                return gpuDevices;
            } catch (Exception ex) {
                throw new Exception("GPU query failed.", ex);
            }
        }

        private static void Initialize(GpuDevice device, int workSize, out Context context, out Kernel kernel) {
            ErrorCode error;

            var dir = Path.GetDirectoryName(Process.GetCurrentProcess().MainModule.FileName);
            var path = Path.Join(dir, "sha256_pow.cl");

            if (!File.Exists(path)) {
                throw new FileNotFoundException("OpenCL kernel missing: " + path);
            }

            var kernelSource = File.ReadAllText(path);

            context = Cl.CreateContext(null, 1, new[] { device.Device }, null, IntPtr.Zero, out error);
            error.ThrowIfError();

            var program = Cl.CreateProgramWithSource(context, 1, new[] { kernelSource }, null, out error);
            error.ThrowIfError();

            error = Cl.BuildProgram(program, 1, new[] { device.Device }, "-DWORK_SIZE=" + workSize, null, IntPtr.Zero);
            error.ThrowIfError();

            var buildStatus = Cl.GetProgramBuildInfo(program, device.Device, ProgramBuildInfo.Status, out error).CastTo<BuildStatus>();
            error.ThrowIfError();

            if (buildStatus != BuildStatus.Success) {
                var buildInfo = Cl.GetProgramBuildInfo(program, device.Device, ProgramBuildInfo.Log, out error);
                error.ThrowIfError();
                throw new Exception(buildInfo.ToString());
            }

            kernel = Cl.CreateKernel(program, "sha256_pow_kernel", out error);
            error.ThrowIfError();

            Cl.CreateKernelsInProgram(program, out error);
            error.ThrowIfError();
        }

        public static void DoWork(uint id, GpuDevice device, BlockingCollection<Job> queue, Channels channels, IConfiguration configuration, ILogger logger, CancellationToken token)
        {
            ErrorCode error;

            byte[] buffer = new byte[4];
            _global.GetBytes(buffer);
            var rand = new Random(BitConverter.ToInt32(buffer, 0));

            var workSize = configuration.GetValue<int>("gpu:work_size");
            logger.LogDebug("work_size = {}", workSize);

            var workMultiplier = configuration.GetValue<int?>("gpu:work_multiplier");
            logger.LogDebug("work_multiplier = {}", workMultiplier);

            Initialize(device, workSize, out var context, out var kernel);

            var maxLocalSize = Cl.GetDeviceInfo(device.Device, DeviceInfo.MaxWorkGroupSize, out error).CastTo<int>();
            error.ThrowIfError();

            bool isNVIDIA = device.PlatformName.Contains("NVIDIA");

            if (workMultiplier == null) {
                workMultiplier = maxLocalSize;
            }

            var maxGlobalSize = maxLocalSize * workMultiplier.Value;
            var maxGlobalSize1 = 1; //maxLocalSize / 2;

            var concat = new byte[64];
            var concatLen = new IntPtr(concat.Length * sizeof(byte));
            var foundLen = new IntPtr(maxGlobalSize * sizeof(byte));
            var countLen = new IntPtr(sizeof(int));

            var globalDimension = new IntPtr[] { new IntPtr(maxGlobalSize) };
            var localDimension = new IntPtr[] { new IntPtr(maxLocalSize / 2) };

            var concatBuf = Cl.CreateBuffer(context, MemFlags.AllocHostPtr | MemFlags.ReadOnly, concatLen, null, out error);
            error.ThrowIfError();

            var foundBuf = Cl.CreateBuffer(context, MemFlags.AllocHostPtr | MemFlags.ReadWrite, foundLen, null, out error);
            error.ThrowIfError();

            var countBuf = Cl.CreateBuffer(context, MemFlags.AllocHostPtr | MemFlags.ReadWrite, countLen, null, out error);
            error.ThrowIfError();

            var cmdQueue = Cl.CreateCommandQueue(context, device.Device, (CommandQueueProperties)0, out error);
            error.ThrowIfError();

            error = Cl.SetKernelArg(kernel, 0, concatBuf);
            error.ThrowIfError();
            error = Cl.SetKernelArg(kernel, 1, foundBuf);
            error.ThrowIfError();
            error = Cl.SetKernelArg(kernel, 2, countBuf);
            error.ThrowIfError();


            while (!token.IsCancellationRequested) {
                var job = queue.Take(token);

                logger.LogDebug("Job assigned, nonce = {}, difficulty = {}", job.Nonce.AsString(), job.Difficulty);

                var extranonce = new byte[32];

                extranonce[0] = (byte)job.Difficulty;
                for (int i = 1; i < 32; i++)  {
                    extranonce[i] = (byte)rand.Next(0, 256);
                }

                for (int i = 0; i < 32; i++) {
                    concat[i] = job.Nonce[i];
                    concat[i + 32] = extranonce[i];
                }

                var buf0 = Cl2.EnqueueMapBuffer(cmdQueue, concatBuf, Bool.True, MapFlags.Write, IntPtr.Zero, concatLen, 0, null, out var ev, out var buf0Err);
                ev.Dispose();
                var buf1 = Cl2.EnqueueMapBuffer(cmdQueue, foundBuf, Bool.True, MapFlags.Read | MapFlags.Write, IntPtr.Zero, foundLen, 0, null, out ev, out var buf1Err);
                ev.Dispose();
                var buf2 = Cl2.EnqueueMapBuffer(cmdQueue, countBuf, Bool.True, MapFlags.Read | MapFlags.Write, IntPtr.Zero, countLen, 0, null, out ev, out var buf2Err);
                ev.Dispose();

                buf0Err.ThrowIfError();
                buf1Err.ThrowIfError();
                buf2Err.ThrowIfError();

                Marshal.Copy(concat, 0, buf0, concat.Length);
                Marshal.WriteInt32(buf2, 0);

                Cl2.EnqueueUnmapObject(cmdQueue, concatBuf, buf0, 0, null, out ev)
                    .ThrowIfError();
                ev.Dispose();
                Cl2.EnqueueUnmapObject(cmdQueue, foundBuf, buf1, 0, null, out ev)
                    .ThrowIfError();
                ev.Dispose();
                Cl2.EnqueueUnmapObject(cmdQueue, countBuf, buf2, 0, null, out ev)
                    .ThrowIfError();
                ev.Dispose();

                Cl.Finish(cmdQueue);

                int executionTimeMs = 200;

                while(!job.CancellationToken.IsCancellationRequested) {
                    var start = DateTime.Now;

                    error = Cl.EnqueueNDRangeKernel(cmdQueue, kernel, 1, null, globalDimension, localDimension, 0, null, out ev);
                    ev.Dispose();

                    buf0 = Cl2.EnqueueMapBuffer(cmdQueue, concatBuf, Bool.False, MapFlags.Write, IntPtr.Zero, concatLen, 0, null, out ev, out buf0Err);
                    ev.Dispose();
                    buf1 = Cl2.EnqueueMapBuffer(cmdQueue, foundBuf, Bool.False, MapFlags.Read | MapFlags.Write, IntPtr.Zero, foundLen, 0, null, out ev, out buf1Err);
                    ev.Dispose();
                    buf2 = Cl2.EnqueueMapBuffer(cmdQueue, countBuf, Bool.False, MapFlags.Read | MapFlags.Write, IntPtr.Zero, countLen, 0, null, out var clevent, out buf2Err);

                    cmdQueue.Flush();

                    if (isNVIDIA) {
                        var maxWaitTime = Math.Max(executionTimeMs - 20, 1);
                        clevent.NvidiaWait(maxWaitTime);
                    }

                    cmdQueue.Finish();

                    var end = DateTime.Now;
                    executionTimeMs = (int)(end - start).TotalMilliseconds;

                    logger.LogTrace("GPU batch execution took {} ms. {} hashes were completed.", executionTimeMs, (ulong)maxGlobalSize * (ulong)maxGlobalSize1 * 512);

                    error.ThrowIfError();
                    buf0Err.ThrowIfError();
                    buf1Err.ThrowIfError();
                    buf2Err.ThrowIfError();

                    var count = Marshal.ReadInt32(buf2, 0);

                    if (count > 0) {
                        if (job.CancellationToken.IsCancellationRequested) {
                            Cl2.EnqueueUnmapObject(cmdQueue, concatBuf, buf0, 0, null, out ev)
                                .ThrowIfError();
                            ev.Dispose();
                            Cl2.EnqueueUnmapObject(cmdQueue, foundBuf, buf1, 0, null, out ev)
                                .ThrowIfError();
                            ev.Dispose();
                            Cl2.EnqueueUnmapObject(cmdQueue, countBuf, buf2, 0, null, out ev)
                                .ThrowIfError();
                            ev.Dispose();
                            break;
                        }

                        for (int i = 0; i < count; i++) {
                            var solution = new byte[32];

                            for (int x = 0; x < 32; x++) {
                                solution[x] = Marshal.ReadByte(buf1, i * 32 + x);
                            }

                            logger.LogDebug("Found solution, nonce = {}", solution.AsString());
                            channels.Solutions.Writer.TryWrite(solution);
                        }

                        Marshal.WriteByte(buf2, 0, 0);
                    }

                    // increment nonce
                    Marshal.WriteInt64(buf0, 33, Marshal.ReadInt64(buf0, 33) + 1);

                    Cl2.EnqueueUnmapObject(cmdQueue, concatBuf, buf0, 0, null, out ev)
                        .ThrowIfError();
                    ev.Dispose();
                    Cl2.EnqueueUnmapObject(cmdQueue, foundBuf, buf1, 0, null, out ev)
                        .ThrowIfError();
                    ev.Dispose();
                    Cl2.EnqueueUnmapObject(cmdQueue, countBuf, buf2, 0, null, out ev)
                        .ThrowIfError();
                    ev.Dispose();

                    StatusManager.GpuHashCount[id] += (ulong)maxGlobalSize * (ulong)maxGlobalSize1 * (ulong)workSize;
                }

                GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced);
            }

            Cl.ReleaseMemObject(concatBuf);
            Cl.ReleaseMemObject(foundBuf);
            Cl.ReleaseMemObject(countBuf);
        }
    }

    public class GpuDevice {
        public Device Device { get; set; }
        public Platform Platform { get; set; }
        public string PlatformName { get; set; }
        public string DeviceName { get; set; }
        public int Id { get; set; }
    }
}