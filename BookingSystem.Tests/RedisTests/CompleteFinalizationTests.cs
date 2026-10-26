using BookingSystem.Api.Models.SystemModels;
using BookingSystem.Api.Services.Caching;
using BookingSystem.Tests;

public class CompleteFinalizationTests : IClassFixture<DatabaseFixture>, IAsyncLifetime
{
    private readonly DatabaseFixture _databaseFixture;
    public CompleteFinalizationTests(DatabaseFixture databaseFixture)
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
    public async Task CompleteFinalization_Success()
    {
        var helperClass = new RedisHelper();
        var (helper, dbContext, bookingTimeService, logger, connection , database, courtName, newCourt, newUser, duration, date, bookingTime, service, createHold ) = await helperClass.CreateSetupValidate();

        var finalizing = await service.BeginFinalizationAsync(newUser.Id);
        Assert.True(finalizing.Success);
        var complete = await service.CompleteFinalizationaAync(newUser.Id, finalizing.LockToken);
        Assert.Equal(Error.none, complete.ErrorMessage);
        Assert.True(complete.Success);
        var getKey = await database.StringGetAsync(GetUserHoldIdKey(newUser.Id));
       Assert.True(getKey.IsNull);

    }
    [Fact]
    public async Task CompleteFinalization_WrongLockToken()
    {
        var helperClass = new RedisHelper();
        var (helper, dbContext, bookingTimeService, logger, connection , database, courtName, newCourt, newUser, duration, date, bookingTime, service, createHold ) = await helperClass.CreateSetupValidate();

        var finalizing = await service.BeginFinalizationAsync(newUser.Id);
        var randomLock = Guid.NewGuid();
        var complete = await service.CompleteFinalizationaAync(newUser.Id, randomLock);
        Assert.Equal(Error.FinalizationLockMismatch, complete.ErrorMessage);
        var getKey = await database.StringGetAsync(GetUserHoldIdKey(newUser.Id));
        Assert.False(getKey.IsNull);
    }
    [Fact]
    public async Task CompleteFinalization_NoActiveHold()
    {
        var helperClass = new RedisHelper();
        var (helper, dbContext, bookingTimeService, logger, connection, database, courtName, newCourt, newUser, duration, date, bookingTime) = await helperClass.CreateSetup();
        var service = new BookingHoldService(bookingTimeService, connection, dbContext, logger);
        var complete = await service.CompleteFinalizationaAync(newUser.Id, null);
        Assert.Equal(Error.UserDosentHaveAnActiveHold, complete.ErrorMessage);
    }
    [Fact]
    public async Task CompleteFinalization_SlotMissing()
    {
        var helperClass = new RedisHelper();
        var (helper, dbContext, bookingTimeService, logger, connection , database, courtName, newCourt, newUser, duration, date, bookingTime, service, createHold ) = await helperClass.CreateSetupValidate();

        var finalizing = await service.BeginFinalizationAsync(newUser.Id);
        await database.KeyDeleteAsync(helperClass.GetSlotHoldIdKey(newCourt.Id, date, bookingTime));
        var complete = await service.CompleteFinalizationaAync(newUser.Id, finalizing.LockToken);
        Assert.Equal(Error.OldSlotsNoLongerBelongToHold, complete.ErrorMessage);
    }
    [Fact]
    public async Task CompleteFinalization_AlreadyCompleted()
    {
        
        var helperClass = new RedisHelper();
        var (helper, dbContext, bookingTimeService, logger, connection , database, courtName, newCourt, newUser, duration, date, bookingTime, service, createHold ) = await helperClass.CreateSetupValidate();

        var finalizing = await service.BeginFinalizationAsync(newUser.Id);
        var complete = await service.CompleteFinalizationaAync(newUser.Id, finalizing.LockToken);
        Assert.Equal(Error.none, complete.ErrorMessage);
        Assert.True(complete.Success);

        var secondComplete = await service.CompleteFinalizationaAync(newUser.Id, finalizing.LockToken);
        Assert.Equal(Error.UserDosentHaveAnActiveHold, secondComplete.ErrorMessage);
    }
    [Fact]
    public async Task CompleteFinalization_ConcurrentRequests()
    {
        var helperClass = new RedisHelper();
        var (helper, dbContext, bookingTimeService, logger, connection , database, courtName, newCourt, newUser, duration, date, bookingTime, service, createHold ) = await helperClass.CreateSetupValidate();

        var finalizing = await service.BeginFinalizationAsync(newUser.Id);

        var complete = service.CompleteFinalizationaAync(newUser.Id, finalizing.LockToken);
        var secondComplete = service.CompleteFinalizationaAync(newUser.Id, finalizing.LockToken);

        var result = await Task.WhenAll(complete, secondComplete);
        Assert.Single(result, x => x.Success);

        Assert.Single(result, x =>
            x.ErrorMessage == Error.UserDosentHaveAnActiveHold ||
            x.ErrorMessage == Error.FinalizationLockMismatch);
    }
    private string GetUserHoldIdKey(string userId)
    {
        return $"hold:user:{userId}";
    }
}