using System;
using System.Threading;
using System.Collections.Generic;
using Microsoft.AspNetCore.SignalR.Client;
using System.Numerics;
using System.Threading.Tasks;
using System.Security.Cryptography;
using System.Collections.Concurrent;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Runtime.CompilerServices;

namespace dcrpt_miner
{
    public static class CpuWorker 
    {
        private static RandomNumberGenerator _global = RandomNumberGenerator.Create();

        public static unsafe void DoWork(uint id, BlockingCollection<Job> queue, Channels channels, ManualResetEvent pauseEvent, CancellationToken token)
        {
            IAlgorithm algo = null;

            while(!token.IsCancellationRequested) {
                var job = queue.Take(token);

                System.Console.WriteLine("ALGO: " + job.Type.ToString());

                if (algo == null || algo.GetType() != job.Algorithm) {
                    algo = (IAlgorithm)Activator.CreateInstance(job.Algorithm);
                }

               if (!algo.CPU) {
                   return;
               }

               algo.DoCPUWork(id, job, channels, pauseEvent, token);
            }
        }

        // TODO: Move to util class
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static unsafe bool checkLeadingZeroBits(byte* hash, int challengeSize, int challengeBytes, int remainingBits) {
            for (int i = 0; i < challengeBytes; i++) {
                if (hash[i] != 0) return false;
            }

            if (remainingBits > 0) return hash[challengeBytes]>>(8-remainingBits) == 0;
            else return true;
        }
    }
}