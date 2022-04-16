using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Threading;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using dcrpt_miner.OpenCL;
using System.IO;
using System.Diagnostics;

namespace dcrpt_miner
{

    class GpuWorker
    {
        private static RandomNumberGenerator _global = RandomNumberGenerator.Create();

        public static List<GpuDevice> QueryDevices(IConfiguration configuration, ILoggerFactory loggerFactory)
        {
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

                Cl.clGetPlatformIDs(0, null, out var numPlatforms)
                    .ThrowIfError();

                var platforms = new IntPtr[numPlatforms];

                Cl.clGetPlatformIDs(numPlatforms, platforms, out numPlatforms)
                    .ThrowIfError();

                foreach (var platform in platforms) {
                     Cl.clGetPlatformInfo(platform, ClPlatformInfo.Vendor, IntPtr.Zero, IntPtr.Zero, out var platformSize)
                        .ThrowIfError();

                    var platformBuf = Marshal.AllocHGlobal(platformSize);

                    Cl.clGetPlatformInfo(platform, ClPlatformInfo.Vendor, platformSize, platformBuf, out _)
                        .ThrowIfError();

                    Cl.clGetDeviceIDs(platform, ClDeviceType.All, 0, null, out var numDevices)
                        .ThrowIfError();

                    var devices = new IntPtr[numDevices];

                    Cl.clGetDeviceIDs(platform, ClDeviceType.All, numDevices, devices, out numDevices)
                        .ThrowIfError();

                    foreach (var device in devices) {
                        Cl.clGetDeviceInfo(device, ClDeviceInfo.Name, IntPtr.Zero, IntPtr.Zero, out var deviceSize)
                            .ThrowIfError();

                        var deviceBuf = Marshal.AllocHGlobal(deviceSize);

                        Cl.clGetDeviceInfo(device, ClDeviceInfo.Name, deviceSize, deviceBuf, out _)
                            .ThrowIfError();

                        var platformName = Marshal.PtrToStringAnsi(platformBuf);
                        var deviceName = Marshal.PtrToStringAnsi(deviceBuf);

                        Console.WriteLine("[{0}]: {1}{2}",  
                            id, 
                            selectedGpus.Contains(id.ToString()) || selectedGpus.Contains(deviceName) ? "*" : "", 
                            deviceName);

                        gpuDevices.Add(new GpuDevice {
                            Platform = platform,
                            PlatformName = platformName.ToString(),
                            Device = device,
                            DeviceName = deviceName.ToString(),
                            Id = id
                        });

                        id++;
                        Marshal.FreeHGlobal(deviceBuf);
                    }

                    Marshal.FreeHGlobal(platformBuf);
                }

                return gpuDevices;
            } catch (Exception ex) {
                throw new Exception("GPU query failed.", ex);
            }
        }

        private static void Initialize(GpuDevice device, int workSize, out IntPtr context, out IntPtr kernel) {
            ClErrorCode error;

            var dir = Path.GetDirectoryName(Process.GetCurrentProcess().MainModule.FileName);
            var path = Path.Join(dir, "sha256_pow.cl");

            if (!File.Exists(path)) {
                throw new FileNotFoundException("OpenCL kernel missing: " + path);
            }

            var kernelSources = new string[] {File.ReadAllText(path) };
            var devices = new IntPtr[] { device.Device };

            context = Cl.clCreateContext(null, (uint)devices.Length, devices, null, IntPtr.Zero, out error);
            error.ThrowIfError();

            var program = Cl.clCreateProgramWithSource(context, (uint)kernelSources.Length, kernelSources, null, out error);
            error.ThrowIfError();

            Cl.clBuildProgram(program, (uint)devices.Length, devices, "-DWORK_SIZE=" + workSize, null, IntPtr.Zero);
            error.ThrowIfError();

            error = Cl.clGetProgramBuildInfo(program, device.Device, ClProgramBuildInfo.Status, IntPtr.Zero, IntPtr.Zero, out var buildStatusSize);
            error.ThrowIfError();

            var buildStatusBuf = Marshal.AllocHGlobal(buildStatusSize);

            error = Cl.clGetProgramBuildInfo(program, device.Device, ClProgramBuildInfo.Status, buildStatusSize, buildStatusBuf, out _);
            error.ThrowIfError();

            var buildStatus = (ClProgramBuildStatus)Marshal.ReadInt32(buildStatusBuf);

            Marshal.FreeHGlobal(buildStatusBuf);

            if (buildStatus != ClProgramBuildStatus.Success) {
                error = Cl.clGetProgramBuildInfo(program, device.Device, ClProgramBuildInfo.Log, IntPtr.Zero, IntPtr.Zero, out var buildLogSize);
                error.ThrowIfError();

                var buildLogBuf = Marshal.AllocHGlobal(buildLogSize);

                error = Cl.clGetProgramBuildInfo(program, device.Device, ClProgramBuildInfo.Log, buildLogSize, buildLogBuf, out _);
                error.ThrowIfError();

                throw new Exception(Marshal.PtrToStringAnsi(buildLogBuf));
            }

            kernel = Cl.clCreateKernel(program, "sha256_pow_kernel", out error);
            error.ThrowIfError();
        }

        public static void DoWork(uint id, GpuDevice device, BlockingCollection<Job> queue, Channels channels, ManualResetEvent pauseEvent, IConfiguration configuration, ILogger logger, CancellationToken token)
        {
           ClErrorCode error;

            byte[] buffer = new byte[4];
            _global.GetBytes(buffer);
            var rand = new Random(BitConverter.ToInt32(buffer, 0));

            var workSize = configuration.GetValue<int>("gpu:work_size");
            logger.LogDebug("work_size = {}", workSize);

            var workMultiplier = configuration.GetValue<long?>("gpu:work_multiplier");
            logger.LogDebug("work_multiplier = {}", workMultiplier);

            Initialize(device, workSize, out var context, out var kernel);

            Cl.clGetDeviceInfo(device.Device, ClDeviceInfo.MaxWorkGroupSize, IntPtr.Zero, IntPtr.Zero, out var workGroupSize)
                .ThrowIfError();

            var workGroupSizeBuf = Marshal.AllocHGlobal(workGroupSize);

            Cl.clGetDeviceInfo(device.Device, ClDeviceInfo.MaxWorkGroupSize, workGroupSize, workGroupSizeBuf, out _)
                .ThrowIfError();

            var maxLocalSize = (long)Marshal.PtrToStructure(workGroupSizeBuf, typeof(long));

            Marshal.FreeHGlobal(workGroupSizeBuf);

            bool isNVIDIA = device.PlatformName.Contains("NVIDIA");

            if (workMultiplier == null) {
                workMultiplier = maxLocalSize;
            }

            logger.LogDebug("platform, {}, devicename, {}, maxLocalSize = {}, multiplier = {}",
                device.PlatformName,
                device.DeviceName,
                maxLocalSize,
                workMultiplier);

            var maxGlobalSize = maxLocalSize * workMultiplier.Value;
            var maxGlobalSize1 = 1; //maxLocalSize / 2;

            var concat = new byte[64];

            var concatLen = new IntPtr(concat.Length * sizeof(byte));
            var foundLen = new IntPtr(maxGlobalSize * sizeof(byte));
            var countLen = new IntPtr(sizeof(int));

            var globalDimension = new IntPtr[] { new IntPtr(maxGlobalSize) };
            var localDimension = new IntPtr[] { new IntPtr(maxLocalSize / 2) };

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

            concatBufHandle.Free();
            foundBufHandle.Free();
            countBufHandle.Free();

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

                var buf0 = Cl.clEnqueueMapBuffer(cmdQueue, concatBuf, true, ClMapFlags.Write, IntPtr.Zero, concatLen, 0, null, out var ev, out var buf0Err);
                Cl.clReleaseEvent(ev);
                var buf1 = Cl.clEnqueueMapBuffer(cmdQueue, foundBuf, true, ClMapFlags.Read | ClMapFlags.Write, IntPtr.Zero, foundLen, 0, null, out ev, out var buf1Err);
                Cl.clReleaseEvent(ev);
                var buf2 = Cl.clEnqueueMapBuffer(cmdQueue, countBuf, true, ClMapFlags.Read | ClMapFlags.Write, IntPtr.Zero, countLen, 0, null, out ev, out var buf2Err);
                Cl.clReleaseEvent(ev);

                buf0Err.ThrowIfError();
                buf1Err.ThrowIfError();
                buf2Err.ThrowIfError();

                Marshal.Copy(concat, 0, buf0, concat.Length);
                Marshal.WriteInt32(buf2, 0);

                Cl.clEnqueueUnmapMemObject(cmdQueue, concatBuf, buf0, 0, null, out ev)
                    .ThrowIfError();
                Cl.clReleaseEvent(ev);
                Cl.clEnqueueUnmapMemObject(cmdQueue, foundBuf, buf1, 0, null, out ev)
                    .ThrowIfError();
                Cl.clReleaseEvent(ev);
                Cl.clEnqueueUnmapMemObject(cmdQueue, countBuf, buf2, 0, null, out ev)
                    .ThrowIfError();
                Cl.clReleaseEvent(ev);

                Cl.clFinish(cmdQueue)
                    .ThrowIfError();

                int executionTimeMs = 200;

                while(!job.CancellationToken.IsCancellationRequested) {
                    var start = DateTime.Now;

                    error = Cl.clEnqueueNDRangeKernel(cmdQueue, kernel, 1, null, globalDimension, localDimension, 0, null, out ev);
                    Cl.clReleaseEvent(ev);

                    buf0 = Cl.clEnqueueMapBuffer(cmdQueue, concatBuf, false, ClMapFlags.Write, IntPtr.Zero, concatLen, 0, null, out ev, out buf0Err);
                    Cl.clReleaseEvent(ev);
                    buf1 = Cl.clEnqueueMapBuffer(cmdQueue, foundBuf, false, ClMapFlags.Read | ClMapFlags.Write, IntPtr.Zero, foundLen, 0, null, out ev, out buf1Err);
                    Cl.clReleaseEvent(ev);
                    buf2 = Cl.clEnqueueMapBuffer(cmdQueue, countBuf, false, ClMapFlags.Read | ClMapFlags.Write, IntPtr.Zero, countLen, 0, null, out var clevent, out buf2Err);

                    Cl.clFlush(cmdQueue)
                        .ThrowIfError();

                    if (isNVIDIA) {
                        var maxWaitTime = Math.Max(executionTimeMs - 50, 1);
                        Cl.NvidiaWait(clevent, maxWaitTime);
                    }

                    Cl.clFinish(cmdQueue)
                        .ThrowIfError();

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
                            Cl.clEnqueueUnmapMemObject(cmdQueue, concatBuf, buf0, 0, null, out ev)
                                .ThrowIfError();
                            Cl.clReleaseEvent(ev);
                            Cl.clEnqueueUnmapMemObject(cmdQueue, foundBuf, buf1, 0, null, out ev)
                                .ThrowIfError();
                            Cl.clReleaseEvent(ev);
                            Cl.clEnqueueUnmapMemObject(cmdQueue, countBuf, buf2, 0, null, out ev)
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

                            logger.LogDebug("Found solution, nonce = {}", solution.AsString());
                            channels.Solutions.Writer.TryWrite(solution);
                        }

                        Marshal.WriteByte(buf2, 0, 0);
                    }

                    // increment nonce
                    Marshal.WriteInt64(buf0, 33, Marshal.ReadInt64(buf0, 33) + 1);

                    Cl.clEnqueueUnmapMemObject(cmdQueue, concatBuf, buf0, 0, null, out ev)
                        .ThrowIfError();
                    Cl.clReleaseEvent(ev);
                    Cl.clEnqueueUnmapMemObject(cmdQueue, foundBuf, buf1, 0, null, out ev)
                        .ThrowIfError();
                    Cl.clReleaseEvent(ev);
                    Cl.clEnqueueUnmapMemObject(cmdQueue, countBuf, buf2, 0, null, out ev)
                        .ThrowIfError();
                    Cl.clReleaseEvent(ev);

                    StatusManager.GpuHashCount[id] += (ulong)maxGlobalSize * (ulong)maxGlobalSize1 * (ulong)workSize;

                    pauseEvent.WaitOne();
                }

                GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced);
            }

            Cl.clReleaseMemObject(concatBuf)
                .ThrowIfError();
            Cl.clReleaseMemObject(foundBuf)
                .ThrowIfError();
            Cl.clReleaseMemObject(countBuf)
                .ThrowIfError();
        }
    }

    public class GpuDevice {
        public IntPtr Device { get; set; }
        public IntPtr Platform { get; set; }
        public string PlatformName { get; set; }
        public string DeviceName { get; set; }
        public int Id { get; set; }
    }
}