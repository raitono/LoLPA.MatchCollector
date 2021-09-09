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
            _timer = new Timer(ProcessQueueItems, null, TimeSpan.Zero, TimeSpan.FromSeconds(5));

            return Task.CompletedTask;
        }

        private async void ProcessQueueItems(object state)
        {
            ProcessorQueueItem queueItem = null;
            try
            {
                queueItem = await _matchProcessorContext.ProcessorQueue.OrderByDescending(i => i.IsPriority).FirstOrDefaultAsync(i => i.ProcessError == null);
                if (queueItem != null)
                {
                    _logger.LogDebug("Processing match {matchId}", queueItem.MatchId);
                    _matchProcessorContext.Remove(queueItem);
                    await _matchProcessorContext.SaveChangesAsync();
                    await ProcessMatch(queueItem.MatchId);
                }
                else
                {
                    _logger.LogDebug("Nothing in the queue...");
                }
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Error processing next queue item");
                if (queueItem != null)
                {
                    var isQueueItemDeleted = _matchProcessorContext.Entry(queueItem).State == EntityState.Detached;
                    if (isQueueItemDeleted)
                    {
                        queueItem.Id = default;
                        _matchProcessorContext.Entry(queueItem).State = EntityState.Added;
                    }
                    queueItem.ProcessError = ProcessError.FromException(e);
                    await _matchProcessorContext.SaveChangesAsync();
                }
            }
        }

        private async Task ProcessMatch(string matchId)
        {
            var match = await _riotApi.Match.GetMatchAsync(RiotSharp.Misc.Region.Americas, matchId);

            foreach (var puuid in match.Metadata.Participants)
            {
                await UpsertSummoner(puuid);
            }

            await _riotContext.SaveChangesAsync();
        }

        private async Task UpsertSummoner(string puuid)
        {
            var summoner = await _riotContext.Summoners.FindAsync(new object[] { puuid });
            var apiSummoner = await _riotApi.Summoner.GetSummonerByPuuidAsync(RiotSharp.Misc.Region.Na, puuid);
            var mappedSummoner = Summoner.FromApi(apiSummoner, summoner);

            if (summoner == null)
            {
                await _riotContext.Summoners.AddAsync(mappedSummoner);
            }
        }
    }
}