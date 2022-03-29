using System.Threading.Tasks;

namespace dcrpt_miner 
{
    public interface IConnectionProvider
    {
        string SolutionName { get; }
        public Task InitializeAsync();
        public Task<SubmitResult> SubmitAsync(byte[] solution);
    }
}