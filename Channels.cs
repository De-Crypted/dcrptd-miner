using System.Threading.Channels;

namespace dcrpt_miner 
{
    public class Channels
    {
        public Channel<JobSolution> Solutions { get; }
        public Channel<Job> Jobs { get; }

        public Channels() 
        {
            Solutions = Channel.CreateUnbounded<JobSolution>();
            Jobs = Channel.CreateUnbounded<Job>();
        }
    }
}
