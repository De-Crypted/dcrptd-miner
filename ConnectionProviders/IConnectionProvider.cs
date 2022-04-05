using System.Threading.Tasks;

namespace dcrpt_miner 
{
    public interface IConnectionProvider
    {
        string JobName { get; }
        string SolutionName { get; }
        public Task StartAsync();
        public Task<SubmitResult> SubmitAsync(byte[] solution);
    }
}