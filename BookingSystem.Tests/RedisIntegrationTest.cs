using BookingSystem.Api.Database;
using BookingSystem.Api.Models.SystemModels;
using BookingSystem.Api.Services;
using BookingSystem.Api.Services.Caching;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using RedisDatabse = StackExchange.Redis.IDatabase;

namespace BookingSystem.Tests;

public class RedisIntegrationTests : IClassFixture<DatabaseFixture>, IAsyncLifetime
{
    private readonly DatabaseFixture _databaseFixture;

    public RedisIntegrationTests(DatabaseFixture databaseFixture)
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

    public async Task CourtDisableIntegrationTest()
    {
        var (helper, dbContext, cache, logger, bookingTimeService, redisCacheService, iDatabase) = CreateSetup();

        var courtName = $"Court-{Guid.NewGuid}";
        var newCourt = helper.CreateNewCourt(courtName);
        var bookingDate = DateOnly.FromDateTime(DateTime.Today).AddDays(2);
        var nextDay = bookingDate.AddDays(1);
        dbContext.Courts.Add(newCourt);
        await dbContext.SaveChangesAsync();

        await iDatabase.StringSetAsync(cacheKey.AvailabilityKey(newCourt.Id, bookingDate, 60), "TestIntegrationCache");
        await iDatabase.StringSetAsync(cacheKey.AvailabilityKey(newCourt.Id, bookingDate, 90), "TestIntegrationCache2");
        await iDatabase.StringSetAsync(cacheKey.AvailabilityKey(newCourt.Id, nextDay, 60), "TestIntegrationCache3");

        var courtService = new CourtService(dbContext, cache, logger, redisCacheService);
        var courtUpdate = await courtService.DisableCourt(courtName, CancellationToken.None);

        var cacheClear = await iDatabase.StringGetAsync(cacheKey.AvailabilityKey(newCourt.Id, bookingDate, 60));

        Assert.False(cacheClear.HasValue);



    }

    private (HelperUnit helper, AppDbContext dbContext, IDistributedCache cache, ILogger<CourtService> logger, BookingTimeService bookingTimeService, RedisCacheService redisCacheService, RedisDatabse iDatabase) CreateSetup()
    {  
        var helper = new HelperUnit();
        var dbContext = helper.DbContextHellper();
        var cache = helper.DistributedCache();
        var logger = NullLogger<CourtService>.Instance;
        var bookingTimeService = new BookingTimeService(dbContext);
        var redisHelper = new RedisIntegrationHelper();
        var (_, iDatabase, redisCacheService) = redisHelper.CreateRedisSetup();

        return(helper, dbContext, cache, logger, bookingTimeService, redisCacheService, iDatabase);
    }
}