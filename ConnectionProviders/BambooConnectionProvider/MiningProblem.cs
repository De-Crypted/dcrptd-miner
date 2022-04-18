namespace dcrpt_miner
{
    public class MiningProblem
    {
       public uint challengeSize { get; set; }
        public string lastHash { get; set; }
        public string lastTimestamp { get; set; }
        public ulong miningFee { get; set; }
    }
}
