namespace dcrpt_miner.OpenCL
{
    public enum ClKernelWorkGroupInfo : int
    {
        WorkGroupSize = 0x11B0,
        CompileWorkGroupSize = 0x11B1,
        LocalMemSize = 0x11B2,
        PreferredWorkGroupSizeMultiple = 0x11B3,
        PrivateMemSize = 0x11B4
    }
}