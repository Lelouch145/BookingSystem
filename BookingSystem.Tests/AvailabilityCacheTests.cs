using System.Text.Json;
using BookingSystem.Api.Database;
using BookingSystem.Api.Models.SystemModels;
using BookingSystem.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace BookingSystem.Tests;

public class AvailabilityServiceCacheTests : IClassFixture<DatabaseFixture>, IAsyncLifetime
{
    private readonly DatabaseFixture _databaseFixture;

    public AvailabilityServiceCacheTests(DatabaseFixture databaseFixture)
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
    public async Task AvailabilityCacheTest_CacheHit()
    {
        var (helper, dbContext, cache, logger, bookingTimeService) = CreateSetup();
        
        var courtName = $"Court-{Guid.NewGuid()}";
        var newCourt = helper.CreateNewCourt(courtName);
        dbContext.Courts.Add(newCourt);
        await dbContext.SaveChangesAsync();

        List<DateTime> times = new List<DateTime>();
        var cacheDate = DateTime.Today.AddDays(1);
        var dateOnlyTime = DateOnly.FromDateTime(cacheDate);
        times.Add(cacheDate);
        var schedule = new Schedule
        {
            Times = times
        };
        var serialize = JsonSerializer.Serialize(schedule);
        await cache.SetStringAsync(cacheKey.AvailabilityKey(newCourt.Id, dateOnlyTime, 60), serialize);

        var service = new AvailabilityService(bookingTimeService, dbContext, cache, logger);
        var result = await service.AvailabilitySchedule(dateOnlyTime, 60, newCourt.Id, CancellationToken.None);
        var getCache = await cache.GetStringAsync(cacheKey.AvailabilityKey(newCourt.Id, dateOnlyTime, 60));
        Assert.NotNull(getCache);
        Assert.Contains(cacheDate, result.Times);
        Assert.Single(result.Times);
        Assert.Equal(cacheDate, result.Times.First());
    }

    [Fact]
    public async Task AvailabilityCacheTest_CacheMiss()
    {
        var (helper, dbContext, cache, logger, bookingTimeService) = CreateSetup();

        var date = DateOnly.FromDateTime(DateTime.Today).AddDays(1);
        var courtName = $"Court-{Guid.NewGuid()}";
        var newCourt = helper.CreateNewCourt(courtName);
        dbContext.Courts.Add(newCourt);
        await dbContext.SaveChangesAsync();

        var service = new AvailabilityService(bookingTimeService, dbContext, cache, logger);
        var result = await service.AvailabilitySchedule(date, 60, newCourt.Id, CancellationToken.None);
        Assert.NotNull(result);
        var cacheAfterResult = await cache.GetStringAsync(cacheKey.AvailabilityKey(newCourt.Id, date, 60));
        Assert.NotNull(cacheAfterResult);
    }
    [Fact]
    public async Task AvailabilityCacheTest_RedisDown()
    {
        var (helper, dbContext, _, logger, bookingTimeService) = CreateSetup();

        var cacheFail = new FakeDistributedCache();
        var date = DateOnly.FromDateTime(DateTime.Today).AddDays(1);
        var courtName = $"Court-{Guid.NewGuid()}";
        var newCourt = helper.CreateNewCourt(courtName);
        dbContext.Courts.Add(newCourt);
        await dbContext.SaveChangesAsync();
        
        var service = new AvailabilityService(bookingTimeService, dbContext, cacheFail, logger);
        var result = await service.AvailabilitySchedule(date, 60, newCourt.Id, CancellationToken.None);

        Assert.Equal(Error.none, result.ErrorMessage);
        Assert.NotNull(result.Times);
    }

    [Fact]
    public async Task AvailabilityCacheTest_CorruptJson()
    {
        var (helper, dbContext, cache, logger, bookingTimeService) = CreateSetup();

        var date = DateOnly.FromDateTime(DateTime.Today).AddDays(1);
        var courtName = $"Court-{Guid.NewGuid()}";
        var newCourt = helper.CreateNewCourt(courtName);
        dbContext.Courts.Add(newCourt);
        await dbContext.SaveChangesAsync();
        await cache.SetStringAsync(cacheKey.AvailabilityKey(newCourt.Id, date, 60), "Broken json");

        var service = new AvailabilityService(bookingTimeService, dbContext, cache, logger);

        var result = await service.AvailabilitySchedule(date, 60, newCourt.Id, CancellationToken.None);

        Assert.Equal(Error.none, result.ErrorMessage);
        Assert.NotNull(result.Times);
        Assert.NotEmpty(result.Times);
    }


    private (HelperUnit helper,AppDbContext dbContext, IDistributedCache cache,ILogger<AvailabilityService> logger, BookingTimeService bookingTimeService) CreateSetup()
    {
        var helper = new HelperUnit();
        var dbContext = helper.DbContextHellper();
        var cache = helper.DistributedCache();
        var logger = NullLogger<AvailabilityService>.Instance;
        var bookingTimeService = new BookingTimeService(dbContext);
        return (helper ,dbContext, cache, logger, bookingTimeService);

    }
}
