using System.Net;
using BookingSystem.Api.Database;
using BookingSystem.Api.Models.SystemModels;
using BookingSystem.Api.Services;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Identity.Client;
using Microsoft.IdentityModel.Tokens;

namespace BookingSystem.Tests;

public class AvailabilityServiceTest : IClassFixture<DatabaseFixture>, IAsyncLifetime
{
    private DatabaseFixture _databaseFixture;
    public AvailabilityServiceTest(DatabaseFixture databaseFixture)
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
    public async Task AvailabilitySchedule_GoodScenario()
    {
        var (helper, dbContext, cache, logger, bookingTimeService) = CreateSetup();
        
        var courtName = $"newCourt-{Guid.NewGuid()}";
        var savedCourt = await helper.CreateCourtAndSaveDatabase(dbContext, courtName);

        var bookingDate = DateOnly.FromDateTime(DateTime.Today).AddDays(1);


        var availabilityService = new AvailabilityService(bookingTimeService, dbContext, cache, logger);

        var schedule = await availabilityService.AvailabilitySchedule(bookingDate, 60, savedCourt.Id, CancellationToken.None);

        var result = schedule.ErrorMessage;

        Assert.Equal(Error.none, result);
    }
    
    [Fact]
    public async Task AvailabilitySchedule_BookingSlotsExist_ScheduleDosentShowTheTime()
    {
        var (helper, dbContext, cache, logger, bookingTimeService) = CreateSetup();

        var courtName = $"newCourt-{Guid.NewGuid()}";
        var savedCourt = await helper.CreateCourtAndSaveDatabase(dbContext, courtName);
        var newUser = helper.CreateNewUser();
        dbContext.Users.Add(newUser);
        await dbContext.SaveChangesAsync();
        var booking = helper.NewBookingWithSlots(savedCourt.Id, newUser.Id);
        dbContext.Bookings.Add(booking);
        await dbContext.SaveChangesAsync();
        var bookingDate = DateOnly.FromDateTime(DateTime.Today).AddDays(1);


        var availabilityService = new AvailabilityService(bookingTimeService, dbContext, cache, logger);

        var schedule = await availabilityService.AvailabilitySchedule(bookingDate, 60, savedCourt.Id, CancellationToken.None);

        Assert.DoesNotContain(booking.StartTime, schedule.Times);

    }

    [Fact]
    public async Task AvailabilitySchedule_CourtDoesNotExist()
    {
        var (helper, dbContext, cache, logger, bookingTimeService) = CreateSetup();
        var availabilityService = new AvailabilityService(bookingTimeService, dbContext, cache, logger);
        var bookingDate = DateOnly.FromDateTime(DateTime.Today).AddDays(1);
        var schedule = await availabilityService.AvailabilitySchedule(bookingDate, 60, 2, CancellationToken.None);

        Assert.Equal(Error.CouldNotFindTheCourtInDataBase, schedule.ErrorMessage);
    }   

    [Fact]
    public async Task AvailabilitySchedule_BookingTimeServiceError()
    {
        var (helper, dbContext, cache, logger, bookingService) = CreateSetup();
        
        var courtName = $"newCourt-{Guid.NewGuid()}";
        var savedCourt = await helper.CreateCourtAndSaveDatabase(dbContext, courtName);

        var bookingDate = DateOnly.FromDateTime(DateTime.Today).AddDays(1);


        var availabilityService = new AvailabilityService(bookingService, dbContext, cache, logger);

        var schedule = await availabilityService.AvailabilitySchedule(bookingDate, 30, savedCourt.Id, CancellationToken.None);

        Assert.Equal(Error.InvalidDuration, schedule.ErrorMessage);
    }
    [Fact]
    public async Task AvailabilitySchedule_OverlappingBooking_StartTimeExcluded()
    {
        var (helper, dbContext, cache, logger, bookingTimeService) = CreateSetup();

        var courtName = $"newCourt-{Guid.NewGuid()}";
        var savedCourt = await helper.CreateCourtAndSaveDatabase(dbContext, courtName);
        var newUser = helper.CreateNewUser();
        dbContext.Users.Add(newUser);
        await dbContext.SaveChangesAsync();
        var booking = helper.NewBookingWithSlots(savedCourt.Id, newUser.Id);
        dbContext.Bookings.Add(booking);
        await dbContext.SaveChangesAsync();
        var bookingDate = DateOnly.FromDateTime(DateTime.Today).AddDays(1);


        var availabilityService = new AvailabilityService(bookingTimeService, dbContext, cache, logger);

        var schedule = await availabilityService.AvailabilitySchedule(bookingDate, 90, savedCourt.Id, CancellationToken.None);
        var startTimeToCheck = booking.StartTime.AddMinutes(-30);

        Assert.DoesNotContain(startTimeToCheck, schedule.Times);
    }

    private (HelperUnit helper, AppDbContext dbContext, IDistributedCache cache, ILogger<AvailabilityService> logger, BookingTimeService bookingTimeService) CreateSetup()
    {
        var helper = new HelperUnit();
        var dbContext = helper.DbContextHellper();
        var cache = helper.DistributedCache();
        var logger = NullLogger<AvailabilityService>.Instance;
        var bookingService = new BookingTimeService(dbContext);

        return (helper, dbContext, cache, logger, bookingService);
    }
    
}