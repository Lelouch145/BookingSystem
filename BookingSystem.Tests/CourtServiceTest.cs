using BookingSystem.Api.Models.SystemModels;
using BookingSystem.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Internal;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json;

namespace BookingSystem.Tests;

public class CourtServiceTests : IClassFixture<DatabaseFixture>, IAsyncLifetime
{
    private DatabaseFixture _databaseFixture;
    private const string CourtsAll = "courts:all";
    private const string CourtsActive = "courts:active";

    public CourtServiceTests(DatabaseFixture databaseFixture)
    {
        _databaseFixture = databaseFixture;
    }

    public async Task InitializeAsync()
    {
        await _databaseFixture.ResetDatabaseAsync();
    }

    public Task DisposeAsync()
    {
        return Task.CompletedTask;
    }
    [Fact]
    public async Task CreateCourtTest()
    {
        var helper = new HelperUnit();
        var dbContext = helper.DbContextHellper();

        var cache = helper.DistributedCache();

        var logger = NullLogger<CourtService>.Instance;

        var courtService = new CourtService(dbContext, cache, logger);
        var courtName = $"Court-{Guid.NewGuid()}";

        var normalizedCourtName = CultureInfo.InvariantCulture.TextInfo
            .ToTitleCase(courtName.Trim().ToLowerInvariant());

        var result = await courtService.CreateCourt(courtName, "", CancellationToken.None);

        Assert.NotNull(result.Courts);
        Assert.Equal(Error.none, result.ErrorMessage);
        Assert.Equal(normalizedCourtName, result.Courts.CourtName);


    }
    [Fact]
    public async Task CreateCourtTestInvalidcourtName()
    {
        var helper = new HelperUnit();
        var dbContext = helper.DbContextHellper();
        var cache = helper.DistributedCache();

        var logger = NullLogger<CourtService>.Instance;

        var courtService = new CourtService(dbContext, cache, logger);

        var result = await courtService.CreateCourt("", "", CancellationToken.None);

        Assert.Equal(Error.InvalidCourtName, result.ErrorMessage);


    }
    [Fact]
    public async Task CreateCourtDuplicateTest()
    {
        var helper = new HelperUnit();
        var dbContext = helper.DbContextHellper();
        var cache = helper.DistributedCache();

        var logger = NullLogger<CourtService>.Instance;

        var courtService = new CourtService(dbContext, cache, logger);

        var courtName = $"Court-{Guid.NewGuid()}";

        var courtServiceHelper = new HelperUnit();

        var newCourt = courtServiceHelper.CreateNewCourt(courtName);

        dbContext.Courts.Add(newCourt);
        await dbContext.SaveChangesAsync();
        var result = await courtService.CreateCourt(courtName, "", CancellationToken.None);

        Assert.Equal(Error.CourtAlreadyExists, result.ErrorMessage);

    }
    [Fact]
    public async Task DisableCourtTest()
    {
        var helper = new HelperUnit();
        var dbContext = helper.DbContextHellper();
        var cache = helper.DistributedCache();

        var courtName = $"Court-{Guid.NewGuid()}";
        var normalizedCourtName = CultureInfo.InvariantCulture.TextInfo
            .ToTitleCase(courtName.Trim().ToLowerInvariant());

        var courtServiceHelper = new HelperUnit();

        var newCourt = courtServiceHelper.CreateNewCourt(courtName);
        dbContext.Courts.Add(newCourt);
        await dbContext.SaveChangesAsync();

        var logger = NullLogger<CourtService>.Instance;

        var courtService = new CourtService(dbContext, cache, logger);

        var result = await courtService.DisableCourt(normalizedCourtName, CancellationToken.None);
        var updateCourt = await dbContext.Courts.FirstAsync(x => x.Id == newCourt.Id);
        Assert.NotNull(result.Courts);
        Assert.Equal(Error.none, result.ErrorMessage);
        Assert.False(updateCourt.IsActive);
    }
    [Fact]
    public async Task DisableCourtSearchNullTest()
    {
        var helper = new HelperUnit();
        var dbContext = helper.DbContextHellper();
        var cache = helper.DistributedCache();

        var courtName = $"Court-{Guid.NewGuid()}";


        var courtServiceHelper = new HelperUnit();

        var newCourt = courtServiceHelper.CreateNewCourt(courtName);
        dbContext.Courts.Add(newCourt);
        await dbContext.SaveChangesAsync();

        var logger = NullLogger<CourtService>.Instance;

        var courtService = new CourtService(dbContext, cache, logger);
        var result = await courtService.DisableCourt("Random", CancellationToken.None);

        Assert.Equal(Error.CouldNotFindTheCourtInDataBase, result.ErrorMessage);
    }
    [Fact]
    public async Task UpdateCourtTest()
    {
        var helper = new HelperUnit();
        var dbContext = helper.DbContextHellper();
        var cache = helper.DistributedCache();

        var courtName = $"Court-{Guid.NewGuid()}";
        var normalizedCourtName = CultureInfo.InvariantCulture.TextInfo
            .ToTitleCase(courtName.Trim().ToLowerInvariant());

        var newCourtName = $"Court-{Guid.NewGuid()}";
        var newNormalizedCourtName = CultureInfo.InvariantCulture.TextInfo
            .ToTitleCase(newCourtName.Trim().ToLowerInvariant());

        var logger = NullLogger<CourtService>.Instance;

        var courtServiceHelper = new HelperUnit();
        var newCourt = courtServiceHelper.CreateNewCourt(courtName);
        dbContext.Courts.Add(newCourt);
        await dbContext.SaveChangesAsync();

        var courtService = new CourtService(dbContext, cache, logger);
        var result = await courtService.UpdateCourt(newCourt.CourtName, newCourtName, "", true, CancellationToken.None);

        var databaseCourtName = await dbContext.Courts.FirstAsync(x => x.Id == newCourt.Id);
        Assert.NotNull(result.Courts);
        Assert.Equal(Error.none, result.ErrorMessage);
        Assert.Equal(newNormalizedCourtName, result.Courts.CourtName);

    }
    [Fact]
    public async Task ShowCourts_CacheMiss()
    {
        var helper = new HelperUnit();
        var dbContext = helper.DbContextHellper();
        var cache = helper.DistributedCache();
        var courtName = $"Court-{Guid.NewGuid()}";

        var newCourt = helper.CreateNewCourt(courtName);
        dbContext.Courts.Add(newCourt);
        await dbContext.SaveChangesAsync();

        var logger = NullLogger<CourtService>.Instance;

        var cached = await cache.GetStringAsync(CourtsAll);
        Assert.Null(cached);

        var service = new CourtService(dbContext, cache, logger);
        var result = await service.ShowCourts(CancellationToken.None);
        var cachedAfterResult = await cache.GetStringAsync(CourtsAll);
        var findCourt = result.First(x => x.CourtName == courtName);
        Assert.Equal(courtName, findCourt.CourtName);

        Assert.NotNull(cachedAfterResult);
    }

    [Fact]
    public async Task ShowCourts_CacheHit()
    {
        var helper = new HelperUnit();
        var dbContext = helper.DbContextHellper();
        var cache = helper.DistributedCache();
        var courtName = $"Courts-{Guid.NewGuid()}";
        var newCourt = helper.CreateNewCourt(courtName);

        dbContext.Courts.Add(newCourt);
        await dbContext.SaveChangesAsync();

        var logger = NullLogger<CourtService>.Instance;
    
        var courtNameCache = $"Courts-{Guid.NewGuid()}";
        var newCourtCahce = helper.CreateNewCourt(courtNameCache);
        var cacheCourts = new List<Court>
        {
            newCourtCahce
        };


        var json = JsonSerializer.Serialize(cacheCourts);
        await cache.SetStringAsync(CourtsAll, json);

        var cachedBeforeServiceCall = await cache.GetStringAsync(CourtsAll);
        Assert.NotNull(cachedBeforeServiceCall);

        var service = new CourtService(dbContext, cache, logger);
        var result = await service.ShowCourts(CancellationToken.None);
        var getCourt = result.First(x => x.CourtName == courtNameCache);

        Assert.Equal(courtNameCache, getCourt.CourtName);
    }

    [Fact]
    public async Task ShowCourts_NullCache_FallBackSqlServer()
    {
        var helper = new HelperUnit();
        var dbContext = helper.DbContextHellper();
        var cache = helper.DistributedCache();
        var logger = NullLogger<CourtService>.Instance;

        var courtName = $"Court-{Guid.NewGuid()}";
        var newCourt = helper.CreateNewCourt(courtName);
        dbContext.Courts.Add(newCourt);
        await dbContext.SaveChangesAsync();

        await cache.SetStringAsync(CourtsAll, "this-is-not-json");

        var service = new CourtService(dbContext, cache, logger);
        var result = await service.ShowCourts(CancellationToken.None);
        var foundCourt = result.First(x => x.CourtName == courtName);
        Assert.Equal(courtName, foundCourt.CourtName);
    }

    [Fact]
    public async Task CreateCourt_RemoveCacheAfterCreation()
    {
        var helper = new HelperUnit();
        var dbContext = helper.DbContextHellper();
        var cache = helper.DistributedCache();
        var logger = NullLogger<CourtService>.Instance;

        var courtName = $"Court-{Guid.NewGuid()}";
        var newCourt = helper.CreateNewCourt(courtName);
        dbContext.Courts.Add(newCourt);
        await dbContext.SaveChangesAsync();

        var json = JsonSerializer.Serialize(newCourt);
        await cache.SetStringAsync(CourtsAll, json);
        await cache.SetStringAsync(CourtsActive, json);
        var cacheCourtsAll = await cache.GetStringAsync(CourtsAll);
        var cacheActiveCourts = await cache.GetStringAsync(CourtsActive);
        Assert.NotNull(cacheCourtsAll);
        Assert.NotNull(cacheActiveCourts);
        var service = new CourtService(dbContext, cache, logger);
        var result = await service.CreateCourt($"Court-{Guid.NewGuid()}", "", CancellationToken.None);
        var cacheAll = await cache.GetStringAsync(CourtsAll);
        var cacheActive = await cache.GetStringAsync(CourtsActive);
        Assert.Null(cacheAll);
        Assert.Null(cacheActive);

    }

    [Fact]
    public async Task ShowActiveCourts_CacheHit()
    {
        var helper = new HelperUnit();
        var dbContext = helper.DbContextHellper();
        var cache = helper.DistributedCache();
        var courtName = $"Courts-{Guid.NewGuid()}";
        var newCourt = helper.CreateNewCourt(courtName);

        dbContext.Courts.Add(newCourt);
        await dbContext.SaveChangesAsync();

        var logger = NullLogger<CourtService>.Instance;
    
        var courtNameCache = $"Courts-{Guid.NewGuid()}";
        var newCourtCahce = helper.CreateNewCourt(courtNameCache);
        var cacheCourts = new List<Court>
        {
            newCourtCahce
        };


        var json = JsonSerializer.Serialize(cacheCourts);
        await cache.SetStringAsync(CourtsActive, json);

        var cachedBeforeServiceCall = await cache.GetStringAsync(CourtsActive);
        Assert.NotNull(cachedBeforeServiceCall);
        var cacheForAllCourts = await cache.GetStringAsync(CourtsAll);
        Assert.Null(cacheForAllCourts);

        var service = new CourtService(dbContext, cache, logger);
        var result = await service.ShowActiveCourts(CancellationToken.None);
        var getCourt = result.First(x => x.CourtName == courtNameCache);

        Assert.Equal(courtNameCache, getCourt.CourtName);
    }


}