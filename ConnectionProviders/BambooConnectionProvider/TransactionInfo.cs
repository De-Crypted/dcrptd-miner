using System;

namespace dcrpt_miner {
    internal unsafe struct TransactionInfo {
        public fixed byte signature[64];
        public fixed byte signingKey[32];
        public ulong timestamp;
        public fixed byte to[25];
        public fixed byte from[25];
        public ulong amount;
        public  ulong fee;
        public bool isTransactionFee;
    }
}
