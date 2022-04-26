using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace dcrpt_miner
{
    public class Startup
    {
        public Startup(IConfiguration configuration)
        {
            Configuration = configuration;
        }

        public IConfiguration Configuration { get; }

        public void ConfigureServices(IServiceCollection services)
        {
            services.AddHttpClient();
            services.AddControllers();

            services.AddSingleton<DcrptConnectionProvider>();
            services.AddSingleton<ShifuPoolConnectionProvider>();
            services.AddSingleton<BambooNodeConnectionProvider>();
            services.AddSingleton<Channels>();

            services.AddHostedService<WorkerManager>();
            services.AddHostedService<ConnectionManager>();
            services.AddHostedService<StatusManager>();
        }

        public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
        {
            var api = Configuration.GetSection("api");

            if (api.GetValue<bool>("enabled")) {
                app.UseRouting();
                app.UseEndpoints(endpoints =>
                {
                    endpoints.MapControllers();
                });
            }
        }
    }
}
