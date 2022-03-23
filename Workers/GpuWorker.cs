using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using OpenCL.NetCore;
using OpenCL.NetCore.Extensions;

namespace dcrpt_miner
{
    public static partial class Cl2
    {
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

        public static void NvidiaWait(this Event @event) {
            ManualResetEvent eventSignal = new ManualResetEvent(false);
            
            var callback = new Cl2.ComputeEventCallback((Event eventHandle, int cmdExecStatusOrErr, IntPtr userData) => {
                eventSignal.Set();
            });

            var handle = GCHandle.Alloc(callback);
            
            Cl2.SetEventCallback(@event, 0, callback, IntPtr.Zero);

            eventSignal.WaitOne(1000);

            handle.Free();
            @event.Dispose();
        }
    }

    class GpuWorker
    {
        private Channels Channels { get; }
        private IConfiguration Configuration { get; }
        private ILogger<GpuWorker> Logger { get; }
        private Platform CLPlatform { get; set; }
        private Device CLDevice { get; set; }
        private Context CLContext { get; set; }
        private OpenCL.NetCore.Program CLProgram { get; set;}
        private Kernel CLKernel { get; set; }
        private bool isAMD { get; set; }
        private IntPtr Dimension { get; set; }
        private RandomNumberGenerator _global = RandomNumberGenerator.Create();

        public GpuWorker(Channels channels, IConfiguration configuration, ILoggerFactory loggerFactory)
        {
            Channels = channels ?? throw new ArgumentNullException(nameof(channels));
            Configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
            Logger = loggerFactory.CreateLogger<GpuWorker>();
            Configuration = configuration;
        }

        public void BuildOpenCL()
        {
            ErrorCode error;

            try {
                Logger.LogDebug("Begin OpenCL initialization");
                var dir = Path.GetDirectoryName(Process.GetCurrentProcess().MainModule.FileName);
                var path = Path.Join(dir, "sha256_pow.cl");

                if (!File.Exists(path)) {
                    throw new FileNotFoundException("OpenCL kernel missing: " + path);
                }

                var cl_Source = File.ReadAllText(path);

                var gpu = Configuration.GetValue<string>("gpu:device");

                if(!int.TryParse(gpu, out int gpuNum)) {
                    gpuNum = 0;
                }

                Console.WriteLine("Detecting OpenCL devices: ");

                int id = 0;

                foreach (var platform in Cl.GetPlatformIDs(out error)) {
                    foreach (var device in Cl.GetDeviceIDs(platform, DeviceType.All, out error)) {
                        var name = Cl.GetDeviceInfo(device, DeviceInfo.Name, out error);
                        Console.WriteLine("[{0}]: {1}{2}", 
                            id, 
                            id == gpuNum ? "*" : "",
                            name);

                        if (id == gpuNum || name.ToString() == gpu) {
                            CLPlatform = platform;
                            CLDevice = device;
                        }

                        id++;
                    }
                }

                var platformName = Cl.GetPlatformInfo(CLPlatform, PlatformInfo.Vendor, out error);
                isAMD = platformName.ToString().Contains("Advanced Micro Devices");

                var workSize = Configuration.GetValue<string>("gpu:work_size");
                Logger.LogDebug("work_size = {}", workSize);

                var multiplier = Configuration.GetValue<long>("gpu:work_multiplier");
                Logger.LogDebug("work_multiplier = {}", multiplier);

                var workItemSizes = Cl.GetDeviceInfo(CLDevice, DeviceInfo.MaxWorkItemSizes, out error).CastTo<int>();

                Dimension = new IntPtr(workItemSizes * multiplier);
                Logger.LogDebug("batch_size = {} ({})", Dimension);

                CLContext = Cl.CreateContext(null, 1, new[] { CLDevice }, null, IntPtr.Zero, out error);
                CLProgram = Cl.CreateProgramWithSource(CLContext, 1, new[] { cl_Source }, null, out error);

                error = Cl.BuildProgram(CLProgram, 1, new[] { CLDevice }, "-DWORK_SIZE=" + workSize, null, IntPtr.Zero);

                CLKernel = Cl.CreateKernel(CLProgram, "sha256_pow_kernel", out error);

                Cl.CreateKernelsInProgram(CLProgram, out error);
                Logger.LogDebug("OpenCL kernel created");
            } catch (Exception ex) {
                /*if (cl_Program != null) {
                    string buildLog = cl_Program.GetBuildLog(cl_Device);
                    Console.WriteLine($"Build log:\n{buildLog}");
                }*/

                throw new Exception("Failed to initialize GPU", ex);
            }
        }

        public unsafe async void DoWork(uint id, BlockingCollection<Job> queue, CancellationToken token)
        {
            ErrorCode error;

            byte[] buffer = new byte[4];
            _global.GetBytes(buffer);
            var rand = new Random(BitConverter.ToInt32(buffer, 0));

            var workSize = Configuration.GetValue<ulong>("gpu:work_size");

            while (!token.IsCancellationRequested) {
                var job = queue.Take(token);

                Logger.LogDebug("Job assigned, nonce = {}, difficulty = {}", job.Nonce.AsString(), job.Difficulty);

                var nonce = new byte[32];

                nonce[0] = (byte)job.Difficulty;
                for (int i = 1; i < 32; i++) nonce[i] = (byte)rand.Next(0, 256);
                
                var concat = new byte[64];
                var difficulty = new uint[1] { (uint)job.Difficulty };
                var found = new byte[1024 * 4];
                var count = new uint[1] {0};

                for (int i = 0; i < 32; i++) {
                    concat[i] = job.Nonce[i];
                    concat[i + 32] = nonce[i];
                }

                var concatBuf = Cl.CreateBuffer(CLContext, MemFlags.UseHostPtr | MemFlags.ReadOnly, concat, out error);
                var difficultyBuf = Cl.CreateBuffer(CLContext, MemFlags.CopyHostPtr | MemFlags.ReadOnly, difficulty, out error);
                var foundBuf = Cl.CreateBuffer(CLContext, MemFlags.UseHostPtr | MemFlags.WriteOnly, new IntPtr(found.Length * sizeof(byte)), found, out error);
                var countBuf = Cl.CreateBuffer(CLContext, MemFlags.UseHostPtr | MemFlags.ReadWrite, new IntPtr(count.Length * sizeof(uint)), count, out error);

                var cmdQueue = Cl.CreateCommandQueue(CLContext, CLDevice, (CommandQueueProperties)0, out error);

                var intPtrSize = Marshal.SizeOf(typeof(IntPtr));
                Cl.SetKernelArg(CLKernel, 0, new IntPtr(intPtrSize), concatBuf);
                Cl.SetKernelArg(CLKernel, 1, new IntPtr(intPtrSize), difficultyBuf);
                Cl.SetKernelArg(CLKernel, 2, new IntPtr(intPtrSize), foundBuf);
                Cl.SetKernelArg(CLKernel, 3, new IntPtr(intPtrSize), countBuf);

                var dimension = Dimension.ToInt32();

                while(!job.CancellationToken.IsCancellationRequested) {
                    //var buf1 = Cl.EnqueueMapBuffer(cmdQueue, countBuf, Bool.True, MapFlags.Read | MapFlags.Write, IntPtr.Zero, new IntPtr(count.Length * sizeof(uint)), 0, null, out _, out error);
                    //var buf2 = Cl.EnqueueMapBuffer(cmdQueue, concatBuf, Bool.True, MapFlags.Read, IntPtr.Zero, new IntPtr(concat.Length * sizeof(byte)), 0, null, out _, out error);
                    //var buf3 = Cl.EnqueueMapBuffer(cmdQueue, foundBuf, Bool.True, MapFlags.Write, IntPtr.Zero, new IntPtr(found.Length * sizeof(byte)), 0, null, out _, out error);

                    error = Cl.EnqueueNDRangeKernel(cmdQueue, CLKernel, 1, null, new IntPtr[] { new IntPtr(dimension) }, null, 0, null, out var clevent);
                    Cl.Flush(cmdQueue);

                    if (isAMD) {
                        clevent.Wait();
                    } else {
                        // seems to work nicely on Intel too so let them fall to this wait
                        clevent.NvidiaWait();
                    }

                    error = Cl.EnqueueReadBuffer(cmdQueue, countBuf, Bool.True, 0, count.Length, count, 0, null, out _);
                    error = Cl.EnqueueReadBuffer(cmdQueue, foundBuf, Bool.True, 0, found.Length, found, 0, null, out _);
                    //Cl.EnqueueUnmapObject(cmdQueue, countBuf, buf1, 0, null, out _);
                    //Cl.EnqueueUnmapObject(cmdQueue, concatBuf, buf2, 0, null, out _);
                    //Cl.EnqueueUnmapObject(cmdQueue, foundBuf, buf3, 0, null, out _);10424391
                    Cl.Finish(cmdQueue);

                    for (int i = 0; i < count[0]; i++) {
                        if (job.CancellationToken.IsCancellationRequested) {
                            break;
                        }

                        var solution = new byte[32];
                        Buffer.BlockCopy(found, i * 32, solution, 0, 32);

                        Logger.LogDebug("Found solution, nonce = {}", solution.AsString());
                        Channels.Solutions.Writer.TryWrite(solution);
                    }

                    fixed(byte* ptr = concat) {
                        ulong* locPtr = (ulong*)(ptr + 33);
                        (*locPtr)++;
                    }

                    count[0] = 0;
                    error = Cl.EnqueueWriteBuffer(cmdQueue, countBuf, Bool.True, IntPtr.Zero, new IntPtr(count.Length * sizeof(uint)), count, 0, null, out _);
                    error = Cl.EnqueueWriteBuffer(cmdQueue, concatBuf, Bool.True, IntPtr.Zero, new IntPtr(concat.Length * sizeof(byte)), concat, 0, null, out _);

                    StatusManager.HashCount[id] += ((ulong)((ulong)dimension * workSize / 100000));
                }

                Cl.ReleaseMemObject(concatBuf);
                Cl.ReleaseMemObject(difficultyBuf);
                Cl.ReleaseMemObject(foundBuf);
                Cl.ReleaseMemObject(countBuf);
            }
        }
    }
}