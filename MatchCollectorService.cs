using LoLPA.Database;
using LoLPA.Database.Models.MatchProcessor;
using LoLPA.Database.Models.Riot;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RiotSharp;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace LoLPA.MatchCollector
{
    public class MatchCollectorService : BackgroundService
    {
        private readonly ILogger<MatchCollectorService> _logger;
        private readonly MatchProcessorContext _matchProcessorContext;
        private readonly RiotApi _riotApi;
        private readonly RiotContext _riotContext;
        private Timer _timer;

        public MatchCollectorService(
            ILogger<MatchCollectorService> logger,
            MatchProcessorContext matchProcessorContext,
            RiotContext riotContext,
            RiotApi riotApi
            )
        {
            _logger = logger;
            _matchProcessorContext = matchProcessorContext;
            _riotContext = riotContext;
            _riotApi = riotApi;
        }

        public override void Dispose()
        {
            _timer?.Dispose();
            base.Dispose();
        }

        public override Task StartAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("{service} started at {startTime}", "MatchCollectorService", DateTime.Now);

            return base.StartAsync(cancellationToken);
        }

        public override Task StopAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("{service} stopped at {stopTime}", "MatchCollectorService", DateTime.Now);

            _timer?.Change(Timeout.Infinite, 0);

            return base.StopAsync(cancellationToken);
        }

        protected override Task ExecuteAsync(CancellationToken cancellationToken)
        {
            _timer = new Timer(ProcessNextQueueItem, null, TimeSpan.Zero, TimeSpan.FromSeconds(5));

            return Task.CompletedTask;
        }

        private async void ProcessNextQueueItem(object state)
        {
            ProcessorQueueItem nextQueueItem = null;
            try
            {
                nextQueueItem = await _matchProcessorContext.ProcessorQueue.OrderByDescending(i => i.IsPriority).FirstOrDefaultAsync(i => i.ProcessError == null);
                if (nextQueueItem != null)
                {
                    _logger.LogDebug("Processing match {matchId}", nextQueueItem.MatchId);
                    _matchProcessorContext.Remove(nextQueueItem);
                    await _matchProcessorContext.SaveChangesAsync();
                    await ProcessMatch(nextQueueItem.MatchId);
                }
                else
                {
                    _logger.LogDebug("Nothing in the queue...");
                }
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Error processing next queue item");
                if (nextQueueItem != null)
                {
                    var isNextQueueItemDeleted = _matchProcessorContext.Entry(nextQueueItem).State == EntityState.Detached;
                    if (isNextQueueItemDeleted)
                    {
                        nextQueueItem.Id = default;
                        _matchProcessorContext.Entry(nextQueueItem).State = EntityState.Added;
                    }
                    nextQueueItem.ProcessError = ProcessError.FromException(e);
                    await _matchProcessorContext.SaveChangesAsync();
                }
            }
        }

        private async Task ProcessMatch(string matchId)
        {
            var match = await _riotApi.Match.GetMatchAsync(RiotSharp.Misc.Region.Americas, matchId);

            foreach (var puuid in match.Metadata.Participants)
            {
                await AddSummoner(puuid);
            }

            await _riotContext.SaveChangesAsync();
        }

        private async Task AddSummoner(string puuid)
        {
            var summoner = await _riotContext.Summoners.FindAsync(new object[] { puuid });
            var apiSummoner = await _riotApi.Summoner.GetSummonerByPuuidAsync(RiotSharp.Misc.Region.Na, puuid);
            var newSummoner = Summoner.FromApi(apiSummoner, summoner);

            if (summoner == null)
            {
                await _riotContext.Summoners.AddAsync(newSummoner);
            }
        }
    }
}