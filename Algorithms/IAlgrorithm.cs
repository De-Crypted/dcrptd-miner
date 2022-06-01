using System.Threading;

namespace dcrpt_miner 
{
    public interface IAlgorithm
    {
        static string Name { get; }
        static bool GPU { get; }
        static bool CPU { get; }

        void DoCPUWork(uint id, Job job, Channels channels, ManualResetEvent pauseEvent, CancellationToken token);
    }
}
