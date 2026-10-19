using System.Runtime.CompilerServices;
using BookingSystem.Api.Database;
using BookingSystem.Api.Models.SystemModels;
using BookingSystem.Api.Services;
using BookingSystem.Api.Services.Caching;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using StackExchange.Redis;

namespace BookingSystem.Tests;

public class BookingHoldServiceIntegrationTests : IClassFixture<DatabaseFixture>, IAsyncLifetime
{
    private readonly DatabaseFixture _databaseFixture;

    public BookingHoldServiceIntegrationTests(DatabaseFixture databaseFixture)
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
    public async Task CreateHold_Success()
    {
        var (helper, dbContext, _, logger, bookingTimeService, _,connection, database) = CreateSetup();
        var courtName = $"Court-{Guid.NewGuid()}";
        var newCourt = helper.CreateNewCourt(courtName);
        dbContext.Courts.Add(newCourt);
        var newUser = helper.CreateNewUser();
        dbContext.Users.Add(newUser);
        await dbContext.SaveChangesAsync();

        var service = new BookingHoldService(bookingTimeService, connection, dbContext, logger);

        var date = new DateOnly(2026, 11, 20);
        var timeOnly = new TimeOnly(15, 00);
        var duration = 60;

        var result = await service.CreateHoldAsync(date, timeOnly, duration, newCourt.Id, newUser.Id, CancellationToken.None);
        var checkRedis = await database.StringGetAsync(GetUserHoldKey(newUser.Id));
        Assert.Equal(Error.none, result.ErrorMessage);
        Assert.NotEqual(Guid.Empty, result.HoldId);
        Assert.Equal(result.HoldId.ToString(), checkRedis.ToString());

    }

    [Fact]
    public async Task CreateHold_SameSlotTwice()
    {
        var (helper, dbContext, _, logger, bookingTimeService, _,connection, database) = CreateSetup();
        var courtName = $"Court-{Guid.NewGuid()}";
        var newCourt = helper.CreateNewCourt(courtName);
        dbContext.Courts.Add(newCourt);
        var newUser = helper.CreateNewUser();
        var newUser2 = helper.CreateNewUser();
        dbContext.Users.AddRange(newUser, newUser2);
        await dbContext.SaveChangesAsync();

        var service = new BookingHoldService(bookingTimeService, connection, dbContext, logger);

        var date = new DateOnly(2026, 11, 20);
        var timeOnly = new TimeOnly(15, 00);
        var duration = 60;
        var result1 = await service.CreateHoldAsync(date, timeOnly, duration, newCourt.Id, newUser.Id, CancellationToken.None);
        var result2 = await service.CreateHoldAsync(date, timeOnly, duration, newCourt.Id, newUser2.Id, CancellationToken.None);
        Assert.Equal(Error.none, result1.ErrorMessage);
        Assert.Equal(Error.BookingSlotAlreadyOnHold, result2.ErrorMessage);
    }
    [Fact]
    public async Task CreateHold_OverlappingSlots()
    {
        var (helper, dbContext, _, logger, bookingTimeService, _,connection, database) = CreateSetup();;
        var courtName = $"Court-{Guid.NewGuid()}";
        var newCourt = helper.CreateNewCourt(courtName);
        dbContext.Courts.Add(newCourt);
        var newUser = helper.CreateNewUser();
        var newUser2 = helper.CreateNewUser();
        dbContext.Users.AddRange(newUser, newUser2);
        await dbContext.SaveChangesAsync();

        var service = new BookingHoldService(bookingTimeService, connection, dbContext, logger);

        var date = new DateOnly(2026, 11, 20);
        var timeOnly = new TimeOnly(15, 00);
        var duration = 60;
        var result1 = await service.CreateHoldAsync(date, timeOnly, duration, newCourt.Id, newUser.Id, CancellationToken.None);
        var result2 = await service.CreateHoldAsync(date, new TimeOnly(15, 30), 90, newCourt.Id, newUser2.Id, CancellationToken.None);
        Assert.Equal(Error.none, result1.ErrorMessage);
        Assert.Equal(Error.BookingSlotAlreadyOnHold, result2.ErrorMessage);
    }
    [Fact]
    public async Task CreateHold_UserAlreadyHasHold()
    {
        var (helper, dbContext, _, logger, bookingTimeService, _,connection, database) = CreateSetup();
        var courtName = $"Court-{Guid.NewGuid()}";
        var newCourt = helper.CreateNewCourt(courtName);
        dbContext.Courts.Add(newCourt);
        var newUser = helper.CreateNewUser();
        var newUser2 = helper.CreateNewUser();
        dbContext.Users.AddRange(newUser, newUser2);
        await dbContext.SaveChangesAsync();

        var service = new BookingHoldService(bookingTimeService, connection, dbContext, logger);

        var date = new DateOnly(2026, 11, 20);
        var timeOnly = new TimeOnly(15, 00);
        var duration = 60;
        var result1 = await service.CreateHoldAsync(date, timeOnly, duration, newCourt.Id, newUser.Id, CancellationToken.None);
        var result2 = await service.CreateHoldAsync(date, new TimeOnly(15, 30), 90, newCourt.Id, newUser2.Id, CancellationToken.None);

        Assert.Equal(Error.none, result1.ErrorMessage);
        Assert.Equal(Error.BookingSlotAlreadyOnHold, result2.ErrorMessage);
    }
    [Fact]
    public async Task CreateHold_Expiration()
    {
        var (helper, dbContext, _, logger, bookingTimeService, _,connection, database) = CreateSetup();
        var courtName = $"Court-{Guid.NewGuid()}";
        var newCourt = helper.CreateNewCourt(courtName);
        dbContext.Courts.Add(newCourt);
        var newUser = helper.CreateNewUser();
        dbContext.Users.Add(newUser);
        await dbContext.SaveChangesAsync();

        var service = new BookingHoldService(bookingTimeService, connection, dbContext, logger);

        var date = DateOnly.FromDateTime(DateTime.Today).AddMonths(1).AddDays(14);
        var timeOnly = new TimeOnly(15, 00);
        var duration = 60;
        var result = await service.CreateHoldAsync(date, timeOnly, duration, newCourt.Id, newUser.Id, CancellationToken.None);

        var ttl = await database.KeyTimeToLiveAsync(GetHoldKey(result.HoldId));
        Assert.NotNull(ttl);
        Assert.True(ttl > TimeSpan.FromSeconds(290));
        Assert.True(ttl <= TimeSpan.FromSeconds(300));
    }
    [Fact]
    public async Task CreateHold_ConcurrentRequests()
    {
        var (helper, dbContext, dbContext2, logger, bookingTimeService, bookingTimeService2, connection, database) = CreateSetup();
        var courtName = $"Court-{Guid.NewGuid()}";
        var newCourt = helper.CreateNewCourt(courtName);
        dbContext.Courts.Add(newCourt);
        var newUser = helper.CreateNewUser();
        var newUser2 = helper.CreateNewUser();
        dbContext.Users.AddRange(newUser, newUser2);
        await dbContext.SaveChangesAsync();

        var service = new BookingHoldService(bookingTimeService, connection, dbContext, logger);
        var service2 = new BookingHoldService(bookingTimeService2, connection, dbContext2, logger);

        var date = new DateOnly(2026, 11, 20);
        var timeOnly = new TimeOnly(15, 00);
        var duration = 60;
        var result1 = service.CreateHoldAsync(date, timeOnly, duration, newCourt.Id, newUser.Id, CancellationToken.None);
        var result2 = service2.CreateHoldAsync(date, timeOnly, 60, newCourt.Id, newUser2.Id, CancellationToken.None);

        var results = await Task.WhenAll(result1, result2);

        Assert.Single(results, x => x.ErrorMessage == Error.none);
        Assert.Single(results, x => x.ErrorMessage == Error.BookingSlotAlreadyOnHold);
    }



    private(HelperUnit helperUnit, AppDbContext dbContext, AppDbContext dbContext2, ILogger<BookingHoldService> logger, BookingTimeService bookingTimeService, BookingTimeService bookingTimeService2, IConnectionMultiplexer connection, IDatabase database) CreateSetup()
    {
        var helper = new HelperUnit();
        var dbContext = helper.DbContextHellper();
        var dbContext2 = helper.DbContextHellper();
        var redisHelper = new RedisIntegrationHelper();
        var (connection, database, _) = redisHelper.CreateRedisSetup();
        var logger = NullLogger<BookingHoldService>.Instance;
        var bookingTimeService = new BookingTimeService(dbContext);
        var bookingTimeService2 = new BookingTimeService(dbContext2);

        return(helper, dbContext, dbContext2, logger, bookingTimeService, bookingTimeService2, connection, database);
    }
    private string GetUserHoldKey(string userId)
    {
        return $"hold:user:{userId}";
    }
    private string GetHoldKey(Guid holdId)
    {
        return $"hold:{holdId}";
    }
}