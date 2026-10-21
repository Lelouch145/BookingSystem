using BookingSystem.Api.Models.SystemModels;
using BookingSystem.Api.Services;
using BookingSystem.Api.Services.Caching;
using NuGet.Frameworks;

namespace BookingSystem.Tests;

public class GetValidateTests : IClassFixture<DatabaseFixture>, IAsyncLifetime
{
    private readonly DatabaseFixture _databaseFixture;

    public GetValidateTests(DatabaseFixture databaseFixture)
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
    public async Task GetValidateHold_Success()
    {
        var helperClass = new RedisHelper();
        var (_, _, _, _, _, _, _, newCourt, newUser, _, _, _, service, createHold ) = await helperClass.CreateSetupValidate();

        Assert.Equal(Error.none, createHold.ErrorMessage);
        var validate = await service.GetValidateHoldAsync(newUser.Id);
        Assert.Equal(Error.none, validate.ErrorMessage);
        Assert.NotNull(validate.CacheHold);
        Assert.Equal(newCourt.Id, validate.CacheHold.CourtId);
    }

    [Fact]
    public async Task GetValidateHold_UserWithoutHold()
    {
        var helperClass = new RedisHelper();
        var (_, dbContext, bookingTimeService, logger, connection, _, _, _, newUser, _, _, _) = await helperClass.CreateSetup();

        var service = new BookingHoldService(bookingTimeService, connection, dbContext, logger);
        var validate = await service.GetValidateHoldAsync(newUser.Id);
        Assert.Equal(Error.UserDosentHaveAnActiveHold, validate.ErrorMessage);
    }
    [Fact]
    public async Task GetValidateHold_HoldDataMissing()
    {
                         
        var helperClass = new RedisHelper();
        var (_, _, _, _, connection , database, _, newCourt, newUser, _, _, _, service, createHold ) = await helperClass.CreateSetupValidate();

        await database.KeyDeleteAsync(GetHoldDataKey(createHold.HoldId));
        var validate = await service.GetValidateHoldAsync(newUser.Id);
        Assert.Equal(Error.DontHaveAHold, validate.ErrorMessage);
    }
    [Fact]
    public async Task GetValidateHold_InvalidJson()
    {
        var helperClass = new RedisHelper();
        var (_, _, _, _, connection , database, _, newCourt, newUser, _, _, _, service, createHold ) = await helperClass.CreateSetupValidate();

        await database.StringSetAsync(GetHoldDataKey(createHold.HoldId), "Invalid", TimeSpan.FromMinutes(5));

        var validate = await service.GetValidateHoldAsync(newUser.Id);
        Assert.Equal(Error.DontHaveAHold, validate.ErrorMessage);
        Assert.Null(validate.CacheHold);
    }
    [Fact]
    public async Task GetValidateHold_SlotMissing()
    {
        var helperClass = new RedisHelper();
        var (helper, dbContext, _, _, connection , database, _, newCourt, newUser, _, date, bookingTime, service, createHold ) = await helperClass.CreateSetupValidate();
 
        await database.KeyDeleteAsync(GetSlotHoldIdKey(newCourt.Id, date, bookingTime));

        var validate = await service.GetValidateHoldAsync(newUser.Id);
        Assert.Equal(Error.OldSlotsNoLongerBelongToHold, validate.ErrorMessage);
        Assert.Null(validate.CacheHold); 
    }
    [Fact]
    public async Task GetValidateHold_SlotBelongsToAnotherHold()
    {
         var helperClass = new RedisHelper();
        var (helper, dbContext, _, _, connection , database, _, newCourt, newUser, _, date, bookingTime, service, createHold ) = await helperClass.CreateSetupValidate();

        var newUserName = $"newUser-{Guid.NewGuid()}";
        var newUser2 = helper.CreateNewUser();
        dbContext.Users.Add(newUser2);
        await dbContext.SaveChangesAsync();
        var validate = await service.GetValidateHoldAsync(newUser2.Id);
        Assert.Equal(Error.UserDosentHaveAnActiveHold, validate.ErrorMessage);
    }
    private string GetSlotHoldIdKey(int courtId, DateOnly date, TimeOnly slotStart)
    {
        return $"hold:slot:{courtId}:{date:yyyy-MM-dd}:{slotStart:HH:mm}";

    }
    private string GetHoldDataKey(Guid holdId)
    {
        return $"hold:{holdId}";
    }
    private string GetUserHoldIdKey(string userId)
    {
        return $"hold:user:{userId}";
    }
}