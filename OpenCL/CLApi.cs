using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;

namespace dcrpt_miner.OpenCL
{
    public static class Cl
    {
        public const string LIBRARY = "OpenCL";

        [DllImport(LIBRARY)]
        [SuppressGCTransition]
        public static extern ClErrorCode clGetPlatformIDs(uint numEntries,
                                                         [In] IntPtr[] platforms,
                                                         out uint numPlatforms);

        [DllImport(LIBRARY)]
        [SuppressGCTransition]
        public static extern ClErrorCode clGetPlatformInfo(IntPtr platform,
                                                          ClPlatformInfo paramName,
                                                          IntPtr paramValueSize,
                                                          IntPtr paramValue,
                                                          out IntPtr paramValueSizeRet);

        [DllImport(LIBRARY)]
        [SuppressGCTransition]
        public static extern ClErrorCode clGetDeviceIDs(IntPtr platform,
                                                       ClDeviceType deviceType,
                                                       uint numEntries,
                                                       [In] IntPtr[] devices,
                                                       out uint numDevices);

        [DllImport(LIBRARY)]
        [SuppressGCTransition]
        public static extern ClErrorCode clGetDeviceInfo(IntPtr device,
                                                ClDeviceInfo paramName,
                                                IntPtr paramValueSize,
                                                IntPtr paramValue,
                                                out IntPtr paramValueSizeRet);

        [DllImport(LIBRARY)]
        [SuppressGCTransition]
        public static extern IntPtr clCreateContext([In] IntPtr[] properties,
                                                     uint numDevices,
                                                     [In] IntPtr[] devices,
                                                     CreateContextNotify pfnNotify,
                                                     IntPtr userData,
                                                     out ClErrorCode errorCode);

        [DllImport(LIBRARY)]
        [SuppressGCTransition]
        public static extern IntPtr clCreateProgramWithSource(IntPtr context,
                                                               uint count,
                                                               [In] [MarshalAs(UnmanagedType.LPArray, ArraySubType = UnmanagedType.LPStr, SizeParamIndex = 1)] string[] strings,
                                                               IntPtr[] lengths,
                                                               out ClErrorCode errorCode);

        [DllImport(LIBRARY)]
        [SuppressGCTransition]
        public static extern ClErrorCode clBuildProgram(IntPtr program,
                                                       uint numDevices,
                                                       [In] IntPtr[] devices,
                                                       [In] [MarshalAs(UnmanagedType.LPStr)] string options,
                                                       BuildProgramNotify pfnNotify,
                                                       IntPtr userData);

        [DllImport(LIBRARY)]
        [SuppressGCTransition]
        public static extern ClErrorCode clGetProgramBuildInfo(IntPtr program,
                                                              IntPtr device,
                                                              ClProgramBuildInfo paramName,
                                                              IntPtr paramValueSize,
                                                              IntPtr paramValue,
                                                              out IntPtr paramValueSizeRet);

        [DllImport(LIBRARY)]
        [SuppressGCTransition]
        public static extern IntPtr clCreateKernel(IntPtr program,
                                                    [In] [MarshalAs(UnmanagedType.LPStr)] string kernelName,
                                                    out ClErrorCode error);

        [DllImport(LIBRARY)]
        [SuppressGCTransition]
        public static extern IntPtr clCreateBuffer(IntPtr context,
                                                    ClMemFlags flags, 
                                                    IntPtr size, 
                                                    IntPtr hostPtr,
                                                    out ClErrorCode error);
 
        [DllImport(LIBRARY)]
        [SuppressGCTransition]
        public static extern IntPtr clCreateCommandQueue(IntPtr context, 
                                                        IntPtr device,
                                                        uint properties,
                                                        out ClErrorCode error);

        [DllImport(LIBRARY)]
        [SuppressGCTransition]
        public static extern ClErrorCode clSetKernelArg(IntPtr kernel, uint argIndex, IntPtr argSize, IntPtr argValue);

        [DllImport(LIBRARY)]
        [SuppressGCTransition]
        public static extern ClErrorCode clEnqueueNDRangeKernel(IntPtr commandQueue,
                                                               IntPtr kernel,
                                                               uint workDim,
                                                               [In] [MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 2)] IntPtr[] globalWorkOffset,
                                                               [In] [MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 2)] IntPtr[] globalWorkSize,
                                                               [In] [MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 2)] IntPtr[] localWorkSize,
                                                               uint numEventsInWaitList,
                                                               [In] IntPtr[] eventWaitList,
                                                               out IntPtr @event);

        [DllImport(LIBRARY)]
        [SuppressGCTransition]
        public static extern IntPtr clEnqueueMapBuffer(IntPtr commandQueue,
                                                        IntPtr buffer,
                                                        bool blockingMap,
                                                        ClMapFlags mapFlags,
                                                        IntPtr offset,
                                                        IntPtr cb,
                                                        uint numEventsInWaitList,
                                                        [In] IntPtr[] eventWaitList,
                                                        out IntPtr @event,
                                                        out ClErrorCode errCodeRet);

        [DllImport(LIBRARY)]
        [SuppressGCTransition]
        public static extern ClErrorCode clReleaseMemObject(IntPtr memObj);

        [DllImport(LIBRARY)]
        [SuppressGCTransition]   
        public static extern ClErrorCode clEnqueueUnmapMemObject(IntPtr commandQueue,
                                                                IntPtr memObj,
                                                                IntPtr mappedPtr,
                                                                uint numEventsInWaitList,
                                                                [In] IntPtr[] eventWaitList,
                                                                out IntPtr @event);

        [DllImport(LIBRARY)]
        [SuppressGCTransition]
        public static extern ClErrorCode clSetEventCallback(IntPtr @event, 
                                                        Int32 commandExecCallbackType, 
                                                        ComputeEventCallback pfnNotify, 
                                                        IntPtr userData);

        [DllImport(LIBRARY)]
        [SuppressGCTransition]
        public static extern ClErrorCode clReleaseEvent(IntPtr @event);

        [DllImport(LIBRARY)]
        [SuppressGCTransition]
        public static extern ClErrorCode clFlush(IntPtr commandQueue);

        [DllImport(LIBRARY)]
        [SuppressGCTransition]
        public static extern ClErrorCode clFinish(IntPtr commandQueue);

        [DllImport(LIBRARY)]
        [SuppressGCTransition]
        public static extern ClErrorCode clGetKernelWorkGroupInfo(
            IntPtr kernel,
            IntPtr device,
            ClKernelWorkGroupInfo paramName,
            IntPtr paramValueSize,
            IntPtr paramValue,
            out IntPtr paramValueSizeRet);

        public delegate void CreateContextNotify(string err, byte[] data, IntPtr cb, IntPtr userData);
        public delegate void BuildProgramNotify(IntPtr program, IntPtr userData);
        public delegate void ComputeEventCallback(IntPtr @event, int cmdExecStatusOrErr, IntPtr userData);

        public static void NvidiaWait(IntPtr @event, int milliseconds) {
            using(var eventSignal = new ManualResetEvent(false)) {
                var callback = new Cl.ComputeEventCallback((IntPtr eventHandle, int cmdExecStatusOrErr, IntPtr userData) => {
                    if (!eventSignal.SafeWaitHandle.IsClosed) {
                        eventSignal.Set();
                    }

                    var handle = GCHandle.FromIntPtr(userData);
                    handle.Free();

                    clReleaseEvent(eventHandle);
                });

                var handle = GCHandle.Alloc(callback);
                var handlePointer = GCHandle.ToIntPtr(handle);

                Cl.clSetEventCallback(@event, 0, callback, handlePointer);

                eventSignal.WaitOne(milliseconds);
            }
        }

        public static void ThrowIfError(this ClErrorCode errorCode) {
            if (errorCode == ClErrorCode.Success) {
                return;
            }
            
            throw new Exception(String.Format("OpenCL call returned error ({0})", errorCode));
        }
    }
}