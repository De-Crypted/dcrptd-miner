using System;

namespace dcrpt_miner.OpenCL
{
    [Flags]
    public enum ClMemFlags : ulong
    {
        None = 0,
        ReadWrite = (1 << 0),
        WriteOnly = (1 << 1),
        ReadOnly = (1 << 2),
        UseHostPtr = (1 << 3),
        AllocHostPtr = (1 << 4),
        CopyHostPtr = (1 << 5),
    }
}
