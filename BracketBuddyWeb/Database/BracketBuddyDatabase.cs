using Microsoft.Azure.Cosmos;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Timers;

public class BracketBuddyDatabase
{
    private readonly CosmosClient Client;
    private readonly Container EntrantsContainer;
    private readonly Container SetsContainer;
    private readonly Container CurrentTournamentsContainer;
    private readonly Container EventsContainer;
    private readonly Container GalintAuthenticationContainer;
    private readonly Container MetricsContainer;

    public static Dictionary<int, int> PlacementToRounds;

    private static readonly Lazy<BracketBuddyDatabase> lazy = new Lazy<BracketBuddyDatabase>(() => new BracketBuddyDatabase());

    private static readonly List<int> BannedOwners = new List<int>() { 1819468 };

    private Container GetContainer(string containerName)
    {
        var dbName = "bracket-buddy";

        return Client.GetContainer(dbName, containerName);
    }

    private BracketBuddyDatabase()
    {
        var url = Environment.GetEnvironmentVariable("COSMOS_ENDPOINT"); 
        var key = Environment.GetEnvironmentVariable("COSMOS_KEY");

        Client = new CosmosClient(url, key);

        EntrantsContainer = GetContainer("Entrants");
        SetsContainer = GetContainer("Sets");
        CurrentTournamentsContainer = GetContainer("CurrentTournaments");
        EventsContainer = GetContainer("Events");
        GalintAuthenticationContainer = GetContainer("GalintAuthenticationTokens");
        MetricsContainer = GetContainer("Metrics");

        PlacementToRounds = new Dictionary<int, int>();

        var keyPlacements = new List<int>() { 1, 2, 3, 4, 5, 7, 9, 13, 17, 25, 33, 49, 65, 97, 129, 193, 257, 385, 513, 769, 1025, 1537, 2049, 3073, 4097, 6145, 8193 };

        for (var index = 0; index < keyPlacements.Count - 1; index++)
        {
            var nextKeyPlacement = keyPlacements[index + 1];

            var currentPlacement = keyPlacements[index];

            for (var placement = currentPlacement; placement < nextKeyPlacement; placement++)
            {
                PlacementToRounds[placement] = index;
            }
        }
    }

    public Dictionary<string, ReportScoreAPIRequestBody> GetEventReportedSets(string eventId)
    {
        return CacheManager.Instance.GetEventReportedSets(eventId);
    }

    public static BracketBuddyDatabase Instance { get { return lazy.Value; } }

    public async Task<StartGGUser> GetGalintAuthenticatedUserAsync(string token)
    {
        var results = new List<StartGGUser>();

        using (var iterator = GalintAuthenticationContainer.GetItemQueryIterator<StartGGUser>($"select * from t where t.token = \"{token}\"",
                                                                              requestOptions: new QueryRequestOptions() { PartitionKey = new PartitionKey(token) }))
        {
            while (iterator.HasMoreResults)
            {
                var next = await iterator.ReadNextAsync();
                results.AddRange(next.Resource);
            }
        }
        
        return results.FirstOrDefault();
    }

    public async Task<List<MetricsModel>> GetMetricsAsync(int hoursBack)
    {
        var cached = CacheManager.Instance.GetMetrics(hoursBack);

        if (cached != null)
        {
            return cached;
        }

        var results = new List<MetricsModel>();

        using (var iterator = MetricsContainer.GetItemQueryIterator<MetricsModel>(
                $"select * from t where t._ts > {DateTimeOffset.UtcNow.AddHours(-1 * hoursBack).ToUnixTimeSeconds()}"))
        {
            while (iterator.HasMoreResults)
            {
                var next = await iterator.ReadNextAsync();
                results.AddRange(next.Resource);
            }
        }

        CacheManager.Instance.SetMetrics(results);

        return results;
    }

    public async Task UpsertMetricsAsync(MetricsModel metrics)
    {
        try
        {
            await MetricsContainer.UpsertItemAsync(metrics, new PartitionKey(metrics.Id));
        }
        catch (Exception ce)
        {
            return;
        }

        return;
    }

    public async Task UpsertGalintAuthenticationTokenAsync(StartGGUser user)
    {
        try
        {
            await GalintAuthenticationContainer.UpsertItemAsync(user, new PartitionKey(user.Token));
        }
        catch (Exception ce)
        {
            return;
        }

        return;
    }

    public async Task<List<Tournament>> GetCurrentTournamentsAllAsync()
    {
        var cached = CacheManager.Instance.GetCurrentAllTournaments();

        if (cached != null)
        {
            return cached;
        }

        var results = new List<Tournament>();

        using (var iterator = CurrentTournamentsContainer.GetItemQueryIterator<Tournament>($"SELECT * FROM c OFFSET 0 LIMIT 10"))
        {
            while (iterator.HasMoreResults)
            {
                var next = await iterator.ReadNextAsync();
                results.AddRange(next.Resource);
            }
        }

        CacheManager.Instance.SetAllCurrentTournaments(results);

        return results.ToList();
    }

    public async Task<List<Tournament>> GetCurrentTournamentsAsync()
    {
        var cached = CacheManager.Instance.GetCurrentTournaments();

        if (cached != null)
        {
            return cached;
        }

        var results = new List<Tournament>();

        using (var iterator = CurrentTournamentsContainer.GetItemQueryIterator<Tournament>($"SELECT * FROM c ORDER BY c._ts DESC OFFSET 0 LIMIT 10"))
        {
            while (iterator.HasMoreResults)
            {
                var next = await iterator.ReadNextAsync();
                results.AddRange(next.Resource);
            }
        }

        CacheManager.Instance.SetCurrentTournaments(results);

        return results.ToList();
    }

    public async Task AddCurrentTournamentAndSetAsActiveAsync(BackendTournament tournament)
    {
        using (var iterator = CurrentTournamentsContainer.GetItemQueryIterator<BackendTournament>($"SELECT * FROM c WHERE (c.isActive or c.IsActive) OFFSET 0 LIMIT 10"))
        {
            while (iterator.HasMoreResults)
            {
                var next = await iterator.ReadNextAsync();

                foreach (var resource in next.Resource)
                {
                    resource.IsActive = false;
                    await CurrentTournamentsContainer.UpsertItemAsync(resource, new PartitionKey(resource.Id));
                }
            }
        }

        tournament.IsActive = true;
        await CurrentTournamentsContainer.UpsertItemAsync(tournament, new PartitionKey(tournament.Id));
    }

    public async Task<Event> GetEventAsync(string eventId, bool useLongerCache = false)
    {
        var cached = CacheManager.Instance.GetEvent(eventId);
        if (cached != null)
        {
            return cached;
        }

        Event result;
        try
        {
            result = await EventsContainer.ReadItemAsync<Event>(eventId, new PartitionKey(eventId));
        } catch (CosmosException ce)
        {
            return null;
        }

        if (result.TournamentOwner?.Id != null && BannedOwners.Contains(result.TournamentOwner.Id))
        {
            result = null;
        }

        CacheManager.Instance.SetEvent(eventId, result);

        return result;
    }

    public async Task<Entrant> GetEntrantBySlugAndEventAsync(string slug, string eventId)
    {
        var matchingEntrants = new List<Entrant>();
        var options = new QueryRequestOptions()
        {
            PartitionKey = new PartitionKey(eventId)
        };

        using (var iterator = EntrantsContainer.GetItemQueryIterator<Entrant>($"SELECT * FROM c WHERE ARRAY_CONTAINS(c.userSlugs, \"{slug}\")", requestOptions: options))
        {
            while (iterator.HasMoreResults)
            {
                var next = await iterator.ReadNextAsync();
                matchingEntrants.AddRange(next.Resource);
            }
        }

        return matchingEntrants.FirstOrDefault();
    }

    public async Task<Entrant> GetEntrantAsync(string entrantId)
    {
        var cached = CacheManager.Instance.GetEntrant(entrantId);
        if (cached != null)
        {
            return cached;
        }

        var entrantsList = new List<Entrant>();
        using (var iterator = EntrantsContainer.GetItemQueryIterator<Entrant>($"SELECT * FROM c WHERE c.id = \"{entrantId}\""))
        {
            while (iterator.HasMoreResults)
            {
                var next = await iterator.ReadNextAsync();
                entrantsList.AddRange(next.Resource);
            }
        }

        var retEntrant = entrantsList.FirstOrDefault();

        CacheManager.Instance.SetEntrant(entrantId, retEntrant);

        return retEntrant;
    }

    public async Task<List<Entrant>> GetEntrantsAsync(string eventId, bool useLongerCache = false)
    {
        var cached = CacheManager.Instance.GetEventEntrants(eventId);
        if (cached != null)
        {
            return cached;
        }

        List<Entrant> results = new List<Entrant>();

        using (var iterator = EntrantsContainer.GetItemQueryIterator<Entrant>($"select * from t where t.eventId = \"{eventId}\"",
                                                                              requestOptions: new QueryRequestOptions() { PartitionKey = new PartitionKey(eventId) }))
        {
            while (iterator.HasMoreResults)
            {
                var next = await iterator.ReadNextAsync();
                results.AddRange(next.Resource);
            }
        }

        List<Entrant> resultsList = results.OrderBy(x => x.Seeding == null).ThenBy(x => x.Seeding).ToList();

        long? ttl = (eventId == "864717" || useLongerCache) ? (long?)86400 : null;
        CacheManager.Instance.SetEventEntrants(eventId, results, ttl);

        return resultsList;
    }

    public async Task<Set> GetSetAsync(string setId)
    {
        var results = new List<Set>();

        using (var iterator = SetsContainer.GetItemQueryIterator<Set>($"select * from t where t.id = \"{setId}\""))
        {
            while (iterator.HasMoreResults)
            {
                var next = await iterator.ReadNextAsync();
                results.AddRange(next.Resource);
            }
        }

        var retSet = results.FirstOrDefault();
        retSet = ProcessSet(retSet);

        return retSet;
    }

    public async Task<List<Set>> GetSetsAsync(string eventId, bool useLongerCache = false)
    {
        var cached = CacheManager.Instance.GetEventSets(eventId);
        if (cached != null)
        {
            return cached;
        }

        var results = new List<Set>();

        using (var iterator = SetsContainer.GetItemQueryIterator<Set>($"select * from t where t.eventId = \"{eventId}\"",
                                                                      requestOptions: new QueryRequestOptions() { PartitionKey = new PartitionKey(eventId) }))
        {
            while (iterator.HasMoreResults)
            {
                var next = await iterator.ReadNextAsync();
                results.AddRange(next.Resource);
            }
        }

        long? ttl = (eventId == "864717" || useLongerCache) ? (long?) 86400 : null;

        results = results.Where(x => x.IsFakeSet != true).Select(x => ProcessSet(x)).ToList();

        CacheManager.Instance.SetEventSets(eventId, results, ttl);

        return results;
    }

    private Set ProcessSet(Set set)
    {
        if (set?.DisplayScore == "DQ" && set?.DetailedScore == null)
        {
            var winnerId = set.WinnerId;
            var loserId = set.EntrantIds.FirstOrDefault(x => x != winnerId);

            set.DetailedScore = new Dictionary<string, string>()
            {
                { winnerId, "0" },
                { loserId, "-1" }
            };
        }

        return set;
    }

    public async Task<IEnumerable<Upset>> GetUpsetsAndNotableAsync(string eventId)
    {
        var cached = CacheManager.Instance.GetEventUpsets(eventId);
        if (cached != null)
        {
            return cached;
        }

        var results = await GetSetsAsync(eventId);

        var ret = results.Where(x => x.IsUpsetOrNotable).Select(set => MapToUpset(set));

        long? ttl = eventId == "864717" ? (long?)86400 : null;
        CacheManager.Instance.SetEventUpsets(eventId, ret.ToList(), ttl);

        return ret;
    }

    public static Upset MapToUpset(Set set)
    {
        Entrant winner = set.Entrants.Where(x => x.Id == set.WinnerId).Single();
        Entrant loser = set.Entrants.Where(x => x.Id != set.WinnerId).Single();

        int winnerRoundSeedingPlacement;
        PlacementToRounds.TryGetValue(winner.InitialSeedNum ?? -1, out winnerRoundSeedingPlacement);

        int loserRoundSeedingPlacement;
        PlacementToRounds.TryGetValue(loser.InitialSeedNum ?? -1, out loserRoundSeedingPlacement);

        int upsetFactor = Math.Abs(winnerRoundSeedingPlacement - loserRoundSeedingPlacement);

        if (set.DisplayScore == "DQ")
        {
            set.DetailedScore = new Dictionary<string, string>()
            {
                { winner.Id, "W" },
                { loser.Id, "DQ" }
            };
        }

        var display = set.DetailedScore == null ? ">" : $"{set.DetailedScore[winner.Id]}-{set.DetailedScore[loser.Id]}";

        var newDisplayScore = $"{winner.Name} ({winner.InitialSeedNum}) {display} " +
                              $"{loser.Name} ({loser.InitialSeedNum})";

        return new Upset()
        {
            Set = set,
            CompletedUpset = winnerRoundSeedingPlacement > loserRoundSeedingPlacement,
            UpsetFactor = upsetFactor,
            NewDisplayScore = newDisplayScore
        };
    }
}