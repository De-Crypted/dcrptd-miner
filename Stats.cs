using System.Collections.Generic;

namespace dcrpt_miner
{
    public class Stats
    {
        public ulong hashes { get; set; }
        public long uptime { get; set; }
        public string ver { get; set; }
        public long accepted { get; set; }
        public long rejected { get; set; }
    }
}