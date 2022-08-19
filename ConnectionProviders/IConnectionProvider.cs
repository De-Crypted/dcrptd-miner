using System;
using System.Threading;
using System.Threading.Tasks;

namespace dcrpt_miner 
{
    public interface IConnectionProvider : IDisposable
    {
        string JobName { get; }
        string SolutionName { get; }
        string Server { get; }
        string Protocol { get; }
        Task RunAsync(string url);
        Task RunDevFeeAsync();
        Task<SubmitResult> SubmitAsync(JobSolution solution);
        long Ping();
    }
}