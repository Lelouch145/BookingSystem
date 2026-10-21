using System.Text.Json;
using System.Threading.Tasks;
using BookingSystem.Api.Database;
using BookingSystem.Api.Models.SystemModels;
using BookingSystem.Api.Services;
using BookingSystem.Api.Services.Caching;
using Microsoft.AspNetCore.Mvc.ModelBinding.Binders;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using StackExchange.Redis;

namespace BookingSystem.Tests;

public class SwitchHoldAsyncIntegrationgTests : IClassFixture<DatabaseFixture>, IAsyncLifetime
{
    private readonly DatabaseFixture _databaseFixture;

    public SwitchHoldAsyncIntegrationgTests(DatabaseFixture databaseFixture)
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
    public async Task SwitchHold_Success()
    {
        var helperClass = new RedisHelper();
        var (_, dbContext, bookingTimeService, logger, connection, _, _, newCourt, newUser, duration, date, bookingTime ) = await helperClass.CreateSetup();
        var newDate = DateOnly.FromDateTime(DateTime.Today).AddMonths(2).AddDays(14);

        await dbContext.SaveChangesAsync();
        var service = new BookingHoldService(bookingTimeService, connection, dbContext, logger);
        var createHold = await service.CreateHoldAsync(date, bookingTime, duration, newCourt.Id, newUser.Id, CancellationToken.None);
        Assert.Equal(Error.none, createHold.ErrorMessage);

        var switchHold = await service.SwitchHoldAsync(newDate, bookingTime, duration, newCourt.Id, newUser.Id, CancellationToken.None);
        Assert.Equal(Error.none, switchHold.ErrorMessage);
    }

    [Fact]
    public async Task SwitchHold_OverlappingSlots()
    {
        var helperClass = new RedisHelper();
        var (_, dbContext, bookingTimeService, logger, connection, _, _, newCourt, newUser, duration, date, bookingTime ) = await helperClass.CreateSetup();

        var oldDuration = 90;
        var newBookingTime = new TimeOnly(16, 00);

        var service = new BookingHoldService(bookingTimeService, connection, dbContext, logger);
        var createHold = await service.CreateHoldAsync(date, bookingTime, oldDuration, newCourt.Id, newUser.Id, CancellationToken.None);
        Assert.Equal(Error.none, createHold.ErrorMessage);

        var switchHold = await service.SwitchHoldAsync(date, newBookingTime, duration, newCourt.Id, newUser.Id, CancellationToken.None);
        Assert.Equal(Error.none, switchHold.ErrorMessage);
    }

    [Fact]
    public async Task SwitchHold_NewSlotOccupied()
    {
        var helperClass = new RedisHelper();
        var (helper , dbContext, bookingTimeService, logger, connection, database, _, newCourt, newUser, duration, date, bookingTime ) = await helperClass.CreateSetup();

        var anotherUser = helper.CreateNewUser();
        dbContext.Users.Add(anotherUser);
        await dbContext.SaveChangesAsync();
        var anotherBookingTime = new TimeOnly(20, 00);
        var oldDuration = 90;

        var service = new BookingHoldService(bookingTimeService, connection, dbContext, logger);
        var createHold = await service.CreateHoldAsync(date, bookingTime, oldDuration, newCourt.Id, newUser.Id, CancellationToken.None);
        var createHoldAnother = await service.CreateHoldAsync(date, anotherBookingTime, oldDuration, newCourt.Id, anotherUser.Id, CancellationToken.None);
        Assert.Equal(Error.none, createHold.ErrorMessage);
        Assert.Equal(Error.none, createHoldAnother.ErrorMessage);

        var switchHold = await service.SwitchHoldAsync(date, anotherBookingTime, duration, newCourt.Id, newUser.Id, CancellationToken.None);
        Assert.Equal(Error.BookingSlotAlreadyOnHold, switchHold.ErrorMessage);
        var oldHold = await database.StringGetAsync(GetHoldDataKey(createHold.HoldId));
        Assert.False(oldHold.IsNull);

        for(int i = 0; i < oldDuration / 30; i++)
        {
            var slotTime = bookingTime.AddMinutes(i * 30);
            var key = GetSlotHoldIdKey(newCourt.Id, date, slotTime);

            var oldSlot = await database.StringGetAsync(key);

            Assert.Equal(createHold.HoldId.ToString(), oldSlot.ToString());
        }
    }

    [Fact]
    public async Task SwitchHold_PreserveExpiration()
    {
        var helperClass = new RedisHelper();
        var (_, dbContext, bookingTimeService, logger, connection, _, _, newCourt, newUser, duration, date, bookingTime ) = await helperClass.CreateSetup();

        var oldDuration = 90;
        var newBookingTime = new TimeOnly(16, 00);

        var service = new BookingHoldService(bookingTimeService, connection, dbContext, logger);
        var createHold = await service.CreateHoldAsync(date, bookingTime, oldDuration, newCourt.Id, newUser.Id, CancellationToken.None);
        Assert.Equal(Error.none, createHold.ErrorMessage);

        var switchHold = await service.SwitchHoldAsync(date, newBookingTime, duration, newCourt.Id, newUser.Id, CancellationToken.None);
        Assert.Equal(Error.none, switchHold.ErrorMessage);
        Assert.Equal(switchHold.ExpiresAt, createHold.ExpiresAt);
    }

    [Fact]
    public async Task SwitchHold_UserWithoutHold()
    {
        var helperClass = new RedisHelper();
        var (_, dbContext, bookingTimeService, logger, connection, _, _, newCourt, newUser, duration, date, bookingTime ) = await helperClass.CreateSetup();



        var service = new BookingHoldService(bookingTimeService, connection, dbContext, logger);

        var switchHold = await service.SwitchHoldAsync(date, bookingTime, duration, newCourt.Id, newUser.Id, CancellationToken.None);
        Assert.Equal(Error.UserDosentHaveAnActiveHold, switchHold.ErrorMessage);
    }

    [Fact]
    public async Task SwitchHold_ChangeCourt()
    {
        var helperClass = new RedisHelper();
        var (helper, dbContext, bookingTimeService, logger, connection, _, _, newCourt, newUser, duration, date, bookingTime ) = await helperClass.CreateSetup();
        var differentCourtName = $"Court-{Guid.NewGuid()}";
        var differentCourt = helper.CreateNewCourt(differentCourtName);
        dbContext.Courts.Add(differentCourt);
        await dbContext.SaveChangesAsync();
        var service = new BookingHoldService(bookingTimeService, connection, dbContext, logger);

        var createHold = await service.CreateHoldAsync(date, bookingTime, duration, newCourt.Id, newUser.Id, CancellationToken.None);
        Assert.Equal(Error.none, createHold.ErrorMessage);

        var switchHoldDifferentCourt = await service.SwitchHoldAsync(date, bookingTime, duration, differentCourt.Id, newUser.Id, CancellationToken.None);
        Assert.Equal(Error.none, switchHoldDifferentCourt.ErrorMessage);
    }

    [Fact]
    public async Task SwitchHold_ConcurrentRequests()
    {
        var helperClass = new RedisHelper();
        var (helper, dbContext, bookingTimeService, logger, connection, _, _, newCourt, newUser, duration, date, bookingTime ) = await helperClass.CreateSetup();
        var secondDbContext = helper.DbContextHellper();
        var secondBookingTimeService = new BookingTimeService(secondDbContext);
        
        var service = new BookingHoldService(bookingTimeService, connection, dbContext, logger);
        var secondService = new BookingHoldService(secondBookingTimeService, connection, secondDbContext, logger);

        var createHold = await service.CreateHoldAsync(date, bookingTime, duration, newCourt.Id, newUser.Id, CancellationToken.None);

        var switchHold = service.SwitchHoldAsync(date, new TimeOnly(17, 00), duration, newCourt.Id, newUser.Id, CancellationToken.None);
        var secondSwitchHold = secondService.SwitchHoldAsync(date, new TimeOnly(18, 00), duration, newCourt.Id, newUser.Id, CancellationToken.None);
        var completeTask = await Task.WhenAll(switchHold, secondSwitchHold);

        Assert.Single(completeTask, x => x.ErrorMessage == Error.none);
        Assert.Single(completeTask, x => x.ErrorMessage == Error.HoldDataHasChanged);
    }
    [Fact]
    public async Task SwitchHold_HoldDataChanged()
    {
        var helperClass = new RedisHelper();
        var (helper, dbContext, bookingTimeService, logger, connection, database, _, newCourt, newUser, duration, date, bookingTime ) = await helperClass.CreateSetup();

        var service = new BookingHoldService(bookingTimeService, connection, dbContext, logger);

        var createHold = await service.CreateHoldAsync(date, bookingTime, duration, newCourt.Id, newUser.Id, CancellationToken.None);

        var oldHold = await database.StringGetAsync(GetHoldDataKey(createHold.HoldId));

        var switchHold = await service.SwitchHoldAsync(date, bookingTime, 90, newCourt.Id, newUser.Id, CancellationToken.None);
        var script = File.ReadAllText
        (
            Path.Combine
            (
                AppContext.BaseDirectory,
                "Services",
                "Caching",
                "Scripts",
                "SwitchHold.lua"
            )
        );
        var newSlotCount = duration / 30;

        List<string> redisKey = new List<string>
        {
            GetUserHoldIdKey(newUser.Id),
            GetHoldDataKey(createHold.HoldId)
        };
        for(var i = 0; i < newSlotCount; i++)
        {
            var time = bookingTime.AddMinutes(i * 30);
            var newSlotKey = GetSlotHoldIdKey(newCourt.Id,date, time);
            redisKey.Add(newSlotKey);
            
        }

        var oldSlotCount = duration / 30;
        List<string> bookingSlotsOld = new List<string>();
        for(var i = 0; i < oldSlotCount; i++)
        {
            var oldTime = bookingTime.AddMinutes(i * 30);
            var oldKey = GetSlotHoldIdKey(newCourt.Id, date, oldTime);
            bookingSlotsOld.Add(oldKey);
        }
        redisKey.AddRange(bookingSlotsOld);

        RedisKey[] keys = redisKey.Select(x => (RedisKey)x).ToArray();

        RedisValue[] values =
        {
            switchHold.HoldId.ToString(),
            "",
            0,
            oldHold.ToString()
        };
        
        var luaScript = await database.ScriptEvaluateAsync(script, keys, values);

        RedisResult[]? results = (RedisResult[]?)luaScript;

        Assert.NotNull(results);
        Assert.Equal(3, (int)results[0]);
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