using System.Threading.Tasks;

namespace dcrpt_miner 
{
    public interface IConnectionProvider
    {
        public Task InitializeAsync();
        public Task<SubmitResult> SubmitAsync(byte[] solution);
    }
}