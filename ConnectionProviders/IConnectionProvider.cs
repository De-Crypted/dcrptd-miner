using System;
using System.Threading;
using System.Threading.Tasks;

namespace dcrpt_miner 
{
    public interface IConnectionProvider : IDisposable
    {
        string JobName { get; }
        string SolutionName { get; }
        public Task RunAsync(string url);
        public Task RunDevFeeAsync(CancellationToken cancellationToken);
        public Task<SubmitResult> SubmitAsync(byte[] solution);
    }
}