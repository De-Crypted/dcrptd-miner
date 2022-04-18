using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace dcrpt_miner
{
    public interface IBambooNodeApi
    {
        Task<(bool success, uint block)> GetBlock();
        Task<(bool success, MiningProblem data)> GetMiningProblem();
        Task<(bool success, List<Transaction> data)> GetTransactions();
        Task<bool> Submit(Stream stream);
    }
}