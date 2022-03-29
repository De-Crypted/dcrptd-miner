using System.Collections.Generic;

namespace dcrpt_miner
{
    public class Block
    {
        public uint Id { get; set; }
        public ulong Timestamp { get; set; }
        public uint ChallengeSize { get; set; }
        public byte[] LastHash { get; set; }
        public byte[] RootHash { get; set; }
        public List<Transaction> Transactions { get; set; }
    }
}