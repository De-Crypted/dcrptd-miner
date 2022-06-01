using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Threading;

namespace dcrpt_miner 
{
    public class Pufferfish2BmbAlgo : IAlgorithm
    {
        public static bool GPU => false;
        public static bool CPU => true;
        public static string Name => "pufferfish2bmb";

        private RandomNumberGenerator _global = RandomNumberGenerator.Create();

        public unsafe void DoCPUWork(uint id, Job job, Channels channels, ManualResetEvent pauseEvent, CancellationToken token)
        {
            byte[] buffer = new byte[4];
            _global.GetBytes(buffer);
            var rand = new Random(BitConverter.ToInt32(buffer, 0));

            Span<byte> concat = new byte[64];
            Span<byte> hash = new byte[119]; // TODO: verify this matches PF_HASHSPACE in all cases
            Span<byte> solution = new byte[32];

            int challengeBytes = job.Difficulty / 8;
            int remainingBits = job.Difficulty - (8 * challengeBytes);

            for (int i = 0; i < 32; i++) concat[i] = job.Nonce[i];
            for (int i = 33; i < 64; i++) concat[i] = (byte)rand.Next(0, 256);
            concat[32] = (byte)job.Difficulty;

            Thread.BeginThreadAffinity();

            using (SHA256 sha256 = SHA256.Create())
            fixed (byte* ptr = concat, hashPtr = hash)
            {
                ulong* locPtr = (ulong*)(ptr + 33);
                uint* hPtr = (uint*)hashPtr;

                uint count = 10;
                while (!job.CancellationToken.IsCancellationRequested)
                {
                    ++*locPtr;

                    Unmanaged.pf_newhash(ptr, 64, 1, 8, hashPtr);
                    var sha256Hash = sha256.ComputeHash(hash.ToArray());

                    if (checkLeadingZeroBits(sha256Hash, job.Difficulty, challengeBytes, remainingBits))
                    {
                        channels.Solutions.Writer.TryWrite(concat.Slice(32).ToArray());
                    }

                    if (count == 0) {
                        StatusManager.CpuHashCount[id] += 10;

                        count = 10;
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

        // TODO: Move to util class or something??
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private unsafe bool checkLeadingZeroBits(byte[] hash, int challengeSize, int challengeBytes, int remainingBits) {
            for (int i = 0; i < challengeBytes; i++) {
                if (hash[i] != 0) return false;
            }

            if (remainingBits > 0) return hash[challengeBytes]>>(8-remainingBits) == 0;
            else return true;
        }

        unsafe class Unmanaged
        {
            [DllImport("Algorithms/pufferfish2bmb/pufferfish2", ExactSpelling = true)]
            [SuppressGCTransition]
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public static extern int pf_newhash(byte* pass, int pass_sz, int cost_t, int cost_m, byte* hash);
        }
    }
}
