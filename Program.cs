using LoLPA.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using RiotSharp;

namespace LoLPA.MatchCollector
{
    public class Program
    {
        public static void Main(string[] args) => CreateHostBuilder(args).Build().Run();

        public static IHostBuilder CreateHostBuilder(string[] args) =>
            Host.CreateDefaultBuilder(args)
                .ConfigureServices((hostingContext, services) =>
                {
                    services
                        .AddLogging()
                        .AddDbContext<MatchProcessorContext>(options => options.UseSqlServer(hostingContext.Configuration.GetConnectionString("MatchProcessor")))
                        .AddDbContext<RiotContext>(options => options.UseSqlServer(hostingContext.Configuration.GetConnectionString("Riot")))
                        .AddSingleton<RiotApi>((serviceProvider) =>
                            {
                                RiotApi api = null;
                                RiotApiOptions options = new();
                                hostingContext.Configuration.GetSection(nameof(RiotApiOptions)).Bind(options);

                                if (hostingContext.HostingEnvironment.IsDevelopment())
                                {
                                    api = RiotApi.GetDevelopmentInstance(options.ApiKey);
                                }
                                else
                                {
                                    api = RiotApi.GetInstance(options.ApiKey, options.LimitPer10s, options.LimitPer10m);
                                }
                                return api;
                            }
                        )
                        .AddHostedService<MatchCollectorService>();
                });
    }
}