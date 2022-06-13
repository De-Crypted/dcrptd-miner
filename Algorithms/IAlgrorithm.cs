using System;
using System.Threading;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace dcrpt_miner 
{
    public interface IAlgorithm : IDisposable
    {
        static bool GPU { get; }
        static bool CPU { get; }
        static double DevFee { get; }
        static string DevWallet { get; }

        string Name { get; }

        void Initialize(ILogger logger, Channels channels, ManualResetEvent PauseEvent);
        void ExecuteJob(Job job);
    }
}
