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

        public static unsafe void DoWork(uint id, BlockingCollection<Job> queue, Channels channels, CancellationToken token)
        {
            byte[] buffer = new byte[4];
            _global.GetBytes(buffer);
            var rand = new Random(BitConverter.ToInt32(buffer, 0));

            Span<byte> concat = stackalloc byte[64];
            Span<byte> hash = stackalloc byte[32];
            Span<byte> solution = stackalloc byte[32];

            while(!token.IsCancellationRequested) {
                var job = queue.Take(token);

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
                            ++StatusManager.HashCount[id];
                            count = 100000;
                            if (id < 2) {
                                // Be nice to other threads and processes
                                Thread.Sleep(1);
                            }
                        }

                        --count;
                    }
                }
            }
        }

        // TODO: Move to util class or something??
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