using System;
using System.Threading;

namespace dcrpt_miner
{
    public class Job
    {
        public JobType Type { get; set; }
        public string Id { get; set; }
        public string Name { get; set; }
        public byte[] Nonce { get; set; }
        public int Difficulty { get; set; }
        public CancellationToken CancellationToken { get; set; }
        public Type Algorithm { get; set; }
    }

    public enum JobType {
        NEW,
        RESTART,
        STOP
    }
}
