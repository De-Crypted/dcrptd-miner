using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;

namespace dcrpt_miner 
{
    public class BambooNodeConnectionProvider : IConnectionProvider
    {
        public Task InitializeAsync()
        {
            throw new System.NotImplementedException();
        }

        public Task<SubmitResult> SubmitAsync(byte[] solution)
        {
            throw new System.NotImplementedException();
        }
    }
}
