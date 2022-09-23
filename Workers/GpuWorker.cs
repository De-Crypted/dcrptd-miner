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

        public static List<GpuDevice> QueryDevices(IConfiguration configuration, ILogger logger)
        {
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
                    try {
                        Cl.clGetPlatformInfo(platform, ClPlatformInfo.Vendor, IntPtr.Zero, IntPtr.Zero, out var platformSize)
                            .ThrowIfError();

                        var platformBuf = Marshal.AllocHGlobal(platformSize);

                        Cl.clGetPlatformInfo(platform, ClPlatformInfo.Vendor, platformSize, platformBuf, out _)
                            .ThrowIfError();

                        Cl.clGetDeviceIDs(platform, ClDeviceType.Gpu, 0, null, out var numDevices)
                            .ThrowIfError();

                        var devices = new IntPtr[numDevices];

                        Cl.clGetDeviceIDs(platform, ClDeviceType.Gpu, numDevices, devices, out numDevices)
                            .ThrowIfError();

                        foreach (var device in devices) {
                            try {
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

                                Marshal.FreeHGlobal(deviceBuf);
                            } catch (Exception ex) {
                                Console.WriteLine("[{0}]: Unknown device(DeviceQueryFailed)",  id);
                                logger.LogDebug(ex, "Device query failed");
                            }

                            id++;
                        }

                        Marshal.FreeHGlobal(platformBuf);
                    } catch (Exception ex) {
                        logger.LogDebug(ex, "Platform query failed");
                    }
                }

                return gpuDevices;
            } catch (Exception ex) {
                throw new Exception("GPU query failed.", ex);
            }
        }

        public static void Initialize(GpuDevice device, int workSize, out IntPtr context, out IntPtr kernel) {
            ClErrorCode error;

            var dir = Path.GetDirectoryName(Process.GetCurrentProcess().MainModule.FileName);
            var path = Path.Join(dir, "Algorithms", "nosohash", "nosohash.cl");

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

            kernel = Cl.clCreateKernel(program, "md5d", out error);
            error.ThrowIfError();
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