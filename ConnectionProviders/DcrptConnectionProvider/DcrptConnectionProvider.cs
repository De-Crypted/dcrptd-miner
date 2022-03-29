using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Configuration;

namespace dcrpt_miner 
{
    public class DcrptConnectionProvider : IConnectionProvider
    {
        public string SolutionName { get; } = "Share";
        
        private IConfiguration Configuration { get; }
        private Channels Channels { get; }
        private HubConnection Connection { get; set; }

        public DcrptConnectionProvider(IConfiguration configuration, Channels channels)
        {
            Configuration = configuration ?? throw new System.ArgumentNullException(nameof(configuration));
            Channels = channels ?? throw new ArgumentNullException(nameof(channels));
        }

        public async Task InitializeAsync() 
        {
            var url = Configuration.GetValue<string>("url").Replace("dcrpt", "http");
            Connection = new HubConnectionBuilder()
                .WithUrl(url + "/job")
                .Build();

            Connection.Closed += async (error) =>
            {
                await Task.Delay(1000);
                await Connection.StartAsync();
            };

            Connection.On<byte[], int>("NewJob", (nonce, difficulty) =>
            {
                Channels.Jobs.Writer.TryWrite(new Job {
                    Nonce = nonce,
                    Difficulty = difficulty
                });
            });

            try
            {
                await Connection.StartAsync();
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.ToString());
            }
        }

        public async Task<SubmitResult> SubmitAsync(byte[] solution)
        {
            var user = Configuration.GetValue<string>("user");
            
            if(await Connection.InvokeAsync<bool>("Submit", user, solution)) {
                return SubmitResult.ACCEPTED;
            }

            return SubmitResult.REJECTED;
        }
    }
}
