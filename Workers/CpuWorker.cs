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

                if (algo == null || algo.GetType() != job.Algorithm) {
                    algo = (IAlgorithm)Activator.CreateInstance(job.Algorithm);
                }

               algo.DoCPUWork(id, job, channels, pauseEvent, token);
            }
        }
    }
}