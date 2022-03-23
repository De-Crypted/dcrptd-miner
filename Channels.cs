using System.Threading.Channels;

namespace dcrpt_miner 
{
    public class Channels
    {
        public Channel<byte[]> Solutions { get; }
        public Channel<Job> Jobs { get; }

        public Channels() 
        {
            Solutions = Channel.CreateUnbounded<byte[]>();
            Jobs = Channel.CreateUnbounded<Job>();
        }
    }
}
