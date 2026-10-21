using BookingSystem.Api.Models.SystemModels;
using BookingSystem.Api.Services.Caching;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using StackExchange.Redis;

namespace BookingSystem.Tests;

public class ReleaseHoldIntegrationTests : IClassFixture<DatabaseFixture>, IAsyncLifetime
{
    private readonly DatabaseFixture _databaseFixture;

    public ReleaseHoldIntegrationTests(DatabaseFixture databaseFixture)
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
    public async Task ReleaseHold_Success()
    {
        var helperClass = new RedisHelper();
        var (_, dbContext, bookingTimeService, logger, connection, database, _, newCourt, newUser, duration, date, bookingTime ) = await helperClass.CreateSetup();

        var service = new BookingHoldService(bookingTimeService, connection, dbContext, logger);

        var createHold = await service.CreateHoldAsync(date, bookingTime, duration, newCourt.Id, newUser.Id, CancellationToken.None);
        Assert.Equal(Error.none, createHold.ErrorMessage);


        List<string> redisKeys = new List<string>
        {
            GetUserHoldIdKey(newUser.Id),
            GetHoldDataKey(createHold.HoldId)
        };
        var slotCount = duration / 30;
        for(int i = 0; i < slotCount; i++)
        {
            var startTime = bookingTime.AddMinutes(i * 30);
            var newSlotKey = GetSlotHoldIdKey(newCourt.Id, date, startTime);
            redisKeys.Add(newSlotKey);
        }

        RedisKey[] keys = redisKeys.Select(x => (RedisKey)x).ToArray();

        var releaseHold = await service.ReleaseHoldAsync(newUser.Id);
        foreach(var key in keys)
        {
            Assert.False(await database.KeyExistsAsync(key));
        }
        Assert.Equal(Error.none, releaseHold.ErrorMessage);
        Assert.True(releaseHold.Success);

    }
    [Fact]
    public async Task ReleaseHold_UserWithoutHold()
    {
        var helperClass = new RedisHelper();
        var (_, dbContext, bookingTimeService, logger, connection, _, _, _, newUser, _, _, _ ) = await helperClass.CreateSetup(); 

        var service = new BookingHoldService(bookingTimeService, connection, dbContext, logger);

        var releaseHold = await service.ReleaseHoldAsync(newUser.Id);

        Assert.Equal(Error.UserDosentHaveAnActiveHold, releaseHold.ErrorMessage);
    }
    [Fact]
    public async Task ReleaseHold_OtherUserUnaffected()
    {
        var helperClass = new RedisHelper();
        var (helper, dbContext, bookingTimeService, logger, connection, database, _, newCourt, newUser, duration, date, bookingTime ) = await helperClass.CreateSetup();

        var anotherUser = helper.CreateNewUser();
        dbContext.Users.Add(anotherUser);
        var anotherDate = DateOnly.FromDateTime(DateTime.Today).AddYears(2).AddMonths(11).AddDays(20);
        await dbContext.SaveChangesAsync();
        var service = new BookingHoldService(bookingTimeService, connection, dbContext, logger);

        var userHold = await service.CreateHoldAsync(date, bookingTime, duration, newCourt.Id, newUser.Id, CancellationToken.None);
        var anotherHold = await service.CreateHoldAsync(anotherDate, bookingTime, duration, newCourt.Id, anotherUser.Id, CancellationToken.None);
        Assert.Equal(Error.none, userHold.ErrorMessage);
        Assert.Equal(Error.none, anotherHold.ErrorMessage);

        var releaseHold = await service.ReleaseHoldAsync(newUser.Id);
        Assert.Equal(Error.none, releaseHold.ErrorMessage);
        Assert.True(releaseHold.Success);
        var anotherHoldInRedis = await database.StringGetAsync(GetHoldDataKey(anotherHold.HoldId));

        Assert.False(anotherHoldInRedis.IsNull);
    }
    [Fact]
    public async Task ReleaseHold_AlreadyReleased()
    {
        var helperClass = new RedisHelper();
        var (_, dbContext, bookingTimeService, logger, connection, database, _, newCourt, newUser, duration, date, bookingTime ) = await helperClass.CreateSetup();

        var service = new BookingHoldService(bookingTimeService, connection, dbContext, logger);

        var createHold = await service.CreateHoldAsync(date, bookingTime, duration, newCourt.Id, newUser.Id, CancellationToken.None);
        Assert.Equal(Error.none, createHold.ErrorMessage);
        var releaseHold = await service.ReleaseHoldAsync(newUser.Id);
        Assert.Equal(Error.none, releaseHold.ErrorMessage);
        var secondRelease = await service.ReleaseHoldAsync(newUser.Id);
        Assert.Equal(Error.UserDosentHaveAnActiveHold, secondRelease.ErrorMessage);
    }
    [Fact]
    public async Task ReleaseHold_ConcurrentRequests()
    {
        var helperClass = new RedisHelper();
        var (_, dbContext, bookingTimeService, logger, connection, database, _, newCourt, newUser, duration, date, bookingTime ) = await helperClass.CreateSetup();

        var service = new BookingHoldService(bookingTimeService, connection, dbContext, logger);

        var createHold = await service.CreateHoldAsync(date, bookingTime, duration, newCourt.Id, newUser.Id, CancellationToken.None);
        Assert.Equal(Error.none, createHold.ErrorMessage);

        var firstRelease = service.ReleaseHoldAsync(newUser.Id);
        var secondRelease = service.ReleaseHoldAsync(newUser.Id);

        var results = await Task.WhenAll(firstRelease, secondRelease);
        Assert.Single(results, x => x.ErrorMessage == Error.none);
        Assert.Single(results, x => x.ErrorMessage == Error.UserDosentHaveAnActiveHold);
    }

    private string GetSlotHoldIdKey(int courtId, DateOnly date, TimeOnly slotStart)
    {
        return $"hold:slot:{courtId}:{date:yyyy-MM-dd}:{slotStart:HH:mm}";

    }
    private string GetUserHoldIdKey(string userId)
    {
        return $"hold:user:{userId}";
    }
    private string GetHoldDataKey(Guid holdId)
    {
        return $"hold:{holdId}";
    }

}