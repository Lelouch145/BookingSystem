using BookingSystem.Api.Models.SystemModels;
using BookingSystem.Api.Services.Caching;

namespace BookingSystem.Tests;

public class BookingFinalizationTests : IClassFixture<DatabaseFixture>, IAsyncLifetime
{
    private readonly DatabaseFixture _databaseFixture;

    public BookingFinalizationTests(DatabaseFixture databaseFixture)
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
    public async Task BeginFinalization_Success()
    {
        var helperClass = new RedisHelper();
        var (helper, dbContext, bookingTimeService, logger, connection , database, courtName, newCourt, newUser, duration, date, bookingTime, service, createHold ) = await helperClass.CreateSetupValidate();
        var finalizing = await service.BeginFinalizationAsync(newUser.Id);
        Assert.True(finalizing.Success);
        Assert.Equal(Error.none, finalizing.ErrorMessage);
        Assert.NotNull(finalizing.LockToken);
    }
    [Fact]
    public async Task BeginFinalization_UserWithoutHold()
    {
        var helperClass = new RedisHelper();
        var (_, dbContext, bookingTimeService, logger, connection, _, _, _, newUser, _, _, _) = await helperClass.CreateSetup();

        var service = new BookingHoldService(bookingTimeService, connection, dbContext, logger);
        var finalizing = await service.BeginFinalizationAsync(newUser.Id);
        Assert.Equal(Error.UserDosentHaveAnActiveHold, finalizing.ErrorMessage);
        Assert.True(!finalizing.Success);
        Assert.Null(finalizing.LockToken);
    }
    [Fact]
    public async Task BeginFinalization_AlreadyFinalizing()
    {
        var helperClass = new RedisHelper();
        var (helper, dbContext, bookingTimeService, logger, connection , database, courtName, newCourt, newUser, duration, date, bookingTime, service, createHold ) = await helperClass.CreateSetupValidate(); 

        var finalizing = await service.BeginFinalizationAsync(newUser.Id);
        Assert.True(finalizing.Success);
        Assert.NotNull(finalizing.LockToken);
        var secondFinalizing = await service.BeginFinalizationAsync(newUser.Id);
        Assert.Equal(Error.HoldFinalizationInProgress, secondFinalizing.ErrorMessage);
    }
    [Fact]
    public async Task BeginFinalization_ConcurrentRequests()
    {
        var helperClass = new RedisHelper();
        var (helper, dbContext, bookingTimeService, logger, connection , database, courtName, newCourt, newUser, duration, date, bookingTime, service, createHold ) = await helperClass.CreateSetupValidate(); 

        var finalizing = service.BeginFinalizationAsync(newUser.Id);
        var secondFinalizing = service.BeginFinalizationAsync(newUser.Id);

        var run = await Task.WhenAll(finalizing, secondFinalizing);
        Assert.Single(run, x => Error.none == x.ErrorMessage);
        Assert.Single(run, x => Error.HoldFinalizationInProgress == x.ErrorMessage);
    }
    [Fact]
    public async Task BeginFinalization_SlotMissing()
    {
        var helperClass = new RedisHelper();
        var (helper, dbContext, bookingTimeService, logger, connection , database, courtName, newCourt, newUser, duration, date, bookingTime, service, createHold ) = await helperClass.CreateSetupValidate();  

        await database.KeyDeleteAsync(GetSlotHoldIdKey(newCourt.Id, date, bookingTime));
        var finalizing = await service.BeginFinalizationAsync(newUser.Id);
        Assert.Equal(Error.OldSlotsNoLongerBelongToHold, finalizing.ErrorMessage);
    }
    [Fact]
    public async Task BeginFinalization_SwitchBlocked()
    {
        var helperClass = new RedisHelper();
        var (helper, dbContext, bookingTimeService, logger, connection , database, courtName, newCourt, newUser, duration, date, bookingTime, service, createHold ) = await helperClass.CreateSetupValidate(); 

        var finalizing = await service.BeginFinalizationAsync(newUser.Id);
        Assert.Equal(Error.none, finalizing.ErrorMessage);
        Assert.True(finalizing.Success);
        var switchHold = await service.SwitchHoldAsync(date, bookingTime, 90, newCourt.Id, newUser.Id, CancellationToken.None);
        Assert.Equal(Error.HoldFinalizationInProgress, switchHold.ErrorMessage);

    }
    [Fact]
    public async Task BeginFinalization_ReleaseBlocked()
    {
        var helperClass = new RedisHelper();
        var (helper, dbContext, bookingTimeService, logger, connection , database, courtName, newCourt, newUser, duration, date, bookingTime, service, createHold ) = await helperClass.CreateSetupValidate(); 

        var finalizing = await service.BeginFinalizationAsync(newUser.Id);
        Assert.Equal(Error.none, finalizing.ErrorMessage);
        Assert.True(finalizing.Success);
        var releaseHold = await service.ReleaseHoldAsync(newUser.Id);
        Assert.Equal(Error.HoldFinalizationInProgress, releaseHold.ErrorMessage);
    }
    [Fact]
    public async Task BeginFinalization_MinimumTTl()
    {
        var helperClass = new RedisHelper();
        var (helper, dbContext, bookingTimeService, logger, connection , database, courtName, newCourt, newUser, duration, date, bookingTime, service, createHold ) = await helperClass.CreateSetupValidate();

        await database.KeyExpireAsync(GetHoldDataKey(createHold.HoldId), TimeSpan.FromSeconds(30));
        var finalizing = await service.BeginFinalizationAsync(newUser.Id);
        var ttl = await database.KeyTimeToLiveAsync(GetFinalizingKey(createHold.HoldId));

        Assert.NotNull(ttl);
        Assert.InRange(ttl.Value.TotalSeconds, 58, 60);
    }
    [Fact]
    public async Task BeginFinalization_PreserveLongerTTL()
    {
        var helperClass = new RedisHelper();
        var (helper, dbContext, bookingTimeService, logger, connection , database, courtName, newCourt, newUser, duration, date, bookingTime, service, createHold ) = await helperClass.CreateSetupValidate();

        var finalizing = await service.BeginFinalizationAsync(newUser.Id);
        var ttl = await database.KeyTimeToLiveAsync(GetFinalizingKey(createHold.HoldId));
        Assert.NotNull(ttl);
        Assert.InRange(ttl.Value.TotalMinutes, 4, 5);
    }
    private string GetSlotHoldIdKey(int courtId, DateOnly date, TimeOnly slotStart)
    {
        return $"hold:slot:{courtId}:{date:yyyy-MM-dd}:{slotStart:HH:mm}";

    }
    private string GetHoldDataKey(Guid holdId)
    {
        return $"hold:{holdId}";
    }
    public string GetFinalizingKey(Guid holdId)
    {
        return $"hold:finalizing:{holdId}";
    }
}