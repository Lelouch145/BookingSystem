using System.Diagnostics;
using BookingSystem.Api.Models.ResponseModel;
using BookingSystem.Api.Models.SystemModels;
using BookingSystem.Api.Services.Caching;
using Microsoft.Identity.Client;

namespace BookingSystem.Tests;

public class AbortFinalizationtests : IClassFixture<DatabaseFixture>, IAsyncLifetime
{
    private readonly DatabaseFixture _databaseFixture;
    public AbortFinalizationtests(DatabaseFixture databaseFixture)
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
    public async Task AbortFinalization_Success()
    {     
        var helperClass = new RedisHelper();
        var (helper, dbContext, bookingTimeService, logger, connection , database, courtName, newCourt, newUser, duration, date, bookingTime, service, createHold ) = await helperClass.CreateSetupValidate();

        var finalizing = await service.BeginFinalizationAsync(newUser.Id);

        var abort = await service.AbortFinalizationAsync(newUser.Id, finalizing.LockToken);

        Assert.True(abort.Success);
        Assert.True(finalizing.LockToken.HasValue);
    }
    [Fact]
    public async Task AbortFinalization_WrongLockToken()
    {
        var helperClass = new RedisHelper();
        var (helper, dbContext, bookingTimeService, logger, connection , database, courtName, newCourt, newUser, duration, date, bookingTime, service, createHold ) = await helperClass.CreateSetupValidate();

        var finalizing = await service.BeginFinalizationAsync(newUser.Id);
        Assert.True(finalizing.LockToken.HasValue);

        var abort = await service.AbortFinalizationAsync(newUser.Id, Guid.NewGuid());
        Assert.Equal(Error.FinalizationLockMismatch, abort.ErrorMessage);
        Assert.True(finalizing.LockToken.HasValue);
    }
    [Fact]
    public async Task AbortFinalization_NoActiveHold()
    {
        var helperClass = new RedisHelper();
        var (helper, dbContext, bookingTimeService, logger, connection, database, courtName, newCourt, newUser, duration, date, bookingTime) = await helperClass.CreateSetup();

        var service = new BookingHoldService(bookingTimeService, connection, dbContext, logger);

        var abort = await service.AbortFinalizationAsync(newUser.Id, Guid.NewGuid());
        Assert.Equal(Error.UserDosentHaveAnActiveHold, abort.ErrorMessage);
    }
    [Fact]
    public async Task AbortFinalization_RestoreOriginalTTL()
    {
        var helperClass = new RedisHelper();
        var (helper, dbContext, bookingTimeService, logger, connection , database, courtName, newCourt, newUser, duration, date, bookingTime, service, createHold ) = await helperClass.CreateSetupValidate();

        await database.KeyExpireAsync(GetHoldDataKey(createHold.HoldId), TimeSpan.FromSeconds(40));

        var finalizing = await service.BeginFinalizationAsync(newUser.Id);
        var ttl = await database.KeyTimeToLiveAsync(GetHoldDataKey(createHold.HoldId));
        Assert.NotNull(ttl);
        Assert.InRange(ttl.Value.TotalSeconds, 50, 60);
        var abort = await service.AbortFinalizationAsync(newUser.Id, finalizing.LockToken);
        var ttlAfterAbort = await database.KeyTimeToLiveAsync(GetHoldDataKey(createHold.HoldId));
        Assert.NotNull(ttlAfterAbort);
        Assert.InRange(ttlAfterAbort.Value.Seconds, 30, 40);
    }
    [Fact]
    public async Task AbortFinalization_OriginalTTLExpired()
    {
        var helperClass = new RedisHelper();
        var (helper, dbContext, bookingTimeService, logger, connection , database, courtName, newCourt, newUser, duration, date, bookingTime, service, createHold ) = await helperClass.CreateSetupValidate(); 

        await database.KeyExpireAsync(GetHoldDataKey(createHold.HoldId), TimeSpan.FromSeconds(3));

        var finalizing = await service.BeginFinalizationAsync(newUser.Id);

        await Task.Delay(TimeSpan.FromSeconds(4));

        var abort = await service.AbortFinalizationAsync(newUser.Id, finalizing.LockToken);
        Assert.Equal(Error.none, abort.ErrorMessage);
        var removed = await database.StringGetAsync(GetHoldDataKey(createHold.HoldId));
        Assert.False(removed.HasValue);
    }
    [Fact]
    public async Task AbortFinalization_ConcurrentRequests()
    {
        var helperClass = new RedisHelper();
        var (helper, dbContext, bookingTimeService, logger, connection , database, courtName, newCourt, newUser, duration, date, bookingTime, service, createHold ) = await helperClass.CreateSetupValidate();

        var finalizing = await service.BeginFinalizationAsync(newUser.Id);

        var abort = service.AbortFinalizationAsync(newUser.Id, finalizing.LockToken);
        var secondAbort = service.AbortFinalizationAsync(newUser.Id, finalizing.LockToken);

        var result = await Task.WhenAll(abort, secondAbort);
        Assert.Single(result, x => x.Success == true);
        Assert.Single(result, x => x.Success == false);
    }

    private string GetHoldDataKey(Guid holdId)
    {
        return $"hold:{holdId}";
    }

}