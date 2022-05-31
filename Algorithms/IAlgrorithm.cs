using System.Threading;

namespace dcrpt_miner 
{
    public interface IAlgorithm
    {
        bool GPU { get; }
        bool CPU { get; }

        void DoCPUWork(uint id, Job job, Channels channels, ManualResetEvent pauseEvent, CancellationToken token);
    }
}
