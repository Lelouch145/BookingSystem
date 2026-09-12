using System.Globalization;
using BookingSystem.Api.Models.SystemModels;
using BookingSystem.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace BookingSystem.Tests;

public class BookingServiceTests : IClassFixture<DatabaseFixture>, IAsyncLifetime
{
    private DatabaseFixture _databaseFixture;

    public BookingServiceTests(DatabaseFixture databaseFixture)
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
    public async Task CreateBookingTest()
    {
        var helper = new HelperUnit();
        var dbContext = helper.DbContextHellper();

        await dbContext.Database.MigrateAsync();

        var bookingTimeService = new BookingTimeService(dbContext);


        var courtName = $"Court{Guid.NewGuid()}";
        var newCourt = helper.CreateNewCourt(courtName);
        dbContext.Courts.Add(newCourt);
        await dbContext.SaveChangesAsync(CancellationToken.None);


        var newUser = helper.CreateNewUser();
        dbContext.Users.Add(newUser);
        var bookingService = new BookingService(dbContext, bookingTimeService);

        var startTime = DateTime.Now.Date.AddDays(1).AddHours(18).AddMinutes(30);
        var result = await bookingService.CreateBooking(newCourt.Id, startTime, 60, newUser.Id, CancellationToken.None);
        var resultSlot = dbContext.BookingSlots.Count(x => x.BookingId == result.ClientBooking.Id);

        Assert.True(result.Success);
        Assert.Equal(newCourt.Id, result.ClientBooking.CourtId);
        Assert.Equal(2, resultSlot);


    }
    [Fact]
    public async Task CancelBookingTest()
    {
        var helper = new HelperUnit();
        var dbContext = helper.DbContextHellper();
        await dbContext.Database.MigrateAsync();

        var bookingTimeService = new BookingTimeService(dbContext);

        var courtName = $"Court-{Guid.NewGuid()}";
        var newCourt = helper.CreateNewCourt(courtName);
        dbContext.Courts.Add(newCourt);
        await dbContext.SaveChangesAsync();

        var newUser = helper.CreateNewUser();
        dbContext.Users.Add(newUser);
        var startTime = DateTime.Now.Date.AddDays(2).AddHours(18).AddMinutes(30);
        var endTime = startTime.AddMinutes(60);

        Booking booking = new Booking
        {
            CourtId = newCourt.Id,
            StartTime = startTime,
            UserId = newUser.Id,
            EndTime = endTime,
            Status = BookingStatus.Confirmed,
        };
        dbContext.Bookings.Add(booking);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var service = new BookingService(dbContext, bookingTimeService);

        var result = await service.CancelBooking(newUser.Id, booking.Id, CancellationToken.None, false);
        var bookingStatus = await dbContext.Bookings.FirstAsync(x => x.Id == booking.Id);
        Assert.True(result.Success);
        Assert.Equal(BookingStatus.Cancelled, bookingStatus.Status);

    }

    [Fact]

    public async Task RescheduleBooking()
    {
        var helper = new HelperUnit();
        var dbContext = helper.DbContextHellper();
        var bookingTimeService = new BookingTimeService(dbContext);

        var newCourtName = $"Court-{Guid.NewGuid()}";
        var newCourt = helper.CreateNewCourt(newCourtName);
        dbContext.Courts.Add(newCourt);
        await dbContext.SaveChangesAsync();
        var user = helper.CreateNewUser();
        dbContext.Users.Add(user);

        var booking = helper.NewBooking(newCourt.Id, user.Id);
        dbContext.Bookings.Add(booking);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var newStartTime = DateTime.Now.Date.AddDays(3).AddHours(15).AddMinutes(30);

        var service = new BookingService(dbContext, bookingTimeService);

        var result = await service.RescheduleBooking(user.Id, booking.Id, 60, newStartTime, false, CancellationToken.None);
        var changedBooking = await dbContext.Bookings.FirstAsync(x => x.Id == booking.Id);

        var slots = await dbContext.BookingSlots.Where(x => x.BookingId == booking.Id)
            .OrderBy(x => x.SlotStart)
            .ToListAsync();
        Assert.True(result.Success);
        Assert.Equal(newStartTime, changedBooking.StartTime);
        Assert.Equal(2, slots.Count);
        Assert.Equal(newStartTime, slots[0].SlotStart);
        Assert.Equal(newStartTime.AddMinutes(30), slots[1].SlotStart);

    }

    [Fact]
    public async Task CreateBooking_CouldNotFindTheCourtInDatabase()
    {
        var helper = new HelperUnit();
        var dbContext = helper.DbContextHellper();
        var bookingTimeService = new BookingTimeService(dbContext);

        var newCourtName = $"Court-{Guid.NewGuid()}";
        var newCourt = helper.CreateNewCourt(newCourtName);
        dbContext.Courts.Add(newCourt);
        var user = helper.CreateNewUser();
        dbContext.Users.Add(user);

        var service = new BookingService(dbContext, bookingTimeService);

        var startTime = DateTime.Now.Date.AddDays(1).AddHours(18).AddMinutes(30);
        var result = await service.CreateBooking(newCourt.Id, startTime, 60, user.Id, CancellationToken.None);

        Assert.Equal(Error.CouldNotFindTheCourtInDataBase, result.ErrorMessage);
    }
    [Fact]
    public async Task CancelBooking_BookingNotFound()
    {
        var helper = new HelperUnit();
        var dbContext = helper.DbContextHellper();
        await dbContext.Database.MigrateAsync();

        var bookingTimeService = new BookingTimeService(dbContext);

        var courtName = $"Court-{Guid.NewGuid()}";
        var newCourt = helper.CreateNewCourt(courtName);
        dbContext.Courts.Add(newCourt);
        await dbContext.SaveChangesAsync();

        var newUser = helper.CreateNewUser();
        dbContext.Users.Add(newUser);
        var startTime = DateTime.Now.Date.AddDays(1).AddHours(18).AddMinutes(30);
        var endTime = startTime.AddMinutes(60);

        Booking booking = new Booking
        {
            CourtId = newCourt.Id,
            StartTime = startTime,
            UserId = newUser.Id,
            EndTime = endTime,
            Status = BookingStatus.Confirmed,
        };
        dbContext.Bookings.Add(booking);

        var service = new BookingService(dbContext, bookingTimeService);

        var result = await service.CancelBooking(newUser.Id, booking.Id, CancellationToken.None, false);
        Assert.False(result.Success);
        Assert.Equal(Error.BookingNotFound, result.ErrorMessage);
    }
    [Fact]
    public async Task CancelBooking_BookingIsAlreadyCancelled()
    {
        var helper = new HelperUnit();
        var dbContext = helper.DbContextHellper();
        await dbContext.Database.MigrateAsync();

        var bookingTimeService = new BookingTimeService(dbContext);

        var courtName = $"Court-{Guid.NewGuid()}";
        var newCourt = helper.CreateNewCourt(courtName);
        dbContext.Courts.Add(newCourt);
        await dbContext.SaveChangesAsync();

        var newUser = helper.CreateNewUser();
        dbContext.Users.Add(newUser);
        var startTime = DateTime.Now.Date.AddDays(1).AddHours(18).AddMinutes(30);
        var endTime = startTime.AddMinutes(60);

        Booking booking = new Booking
        {
            CourtId = newCourt.Id,
            StartTime = startTime,
            UserId = newUser.Id,
            EndTime = endTime,
            Status = BookingStatus.Cancelled,
        };
        dbContext.Bookings.Add(booking);
        await dbContext.SaveChangesAsync();

        var service = new BookingService(dbContext, bookingTimeService);

        var result = await service.CancelBooking(newUser.Id, booking.Id, CancellationToken.None, false);
        Assert.False(result.Success);
        Assert.Equal(Error.BookingIsAlreadyCancelled, result.ErrorMessage);
    }

    [Fact]
    public async Task CancelBooking_CannotCancelBooking24HoursBeforeTheBooking()
    {
        var helper = new HelperUnit();
        var dbContext = helper.DbContextHellper();
        await dbContext.Database.MigrateAsync();

        var bookingTimeService = new BookingTimeService(dbContext);

        var courtName = $"Court-{Guid.NewGuid()}";
        var newCourt = helper.CreateNewCourt(courtName);
        dbContext.Courts.Add(newCourt);
        await dbContext.SaveChangesAsync();

        var newUser = helper.CreateNewUser();
        dbContext.Users.Add(newUser);
        var startTime = DateTime.Now.Date.AddDays(0).AddHours(18).AddMinutes(30);
        var endTime = startTime.AddMinutes(60);

        Booking booking = new Booking
        {
            CourtId = newCourt.Id,
            StartTime = startTime,
            UserId = newUser.Id,
            EndTime = endTime,
            Status = BookingStatus.Confirmed,
        };
        dbContext.Bookings.Add(booking);
        await dbContext.SaveChangesAsync();

        var service = new BookingService(dbContext, bookingTimeService);

        var result = await service.CancelBooking(newUser.Id, booking.Id, CancellationToken.None, false);
        Assert.False(result.Success);
        Assert.Equal(Error.CannotCancelBooking24HoursBeforeTheBooking, result.ErrorMessage);
    }

    [Fact]
    public async Task CancelBooking_AdminCancel24HoursBeforeTheBooking()
    {
        var helper = new HelperUnit();
        var dbContext = helper.DbContextHellper();
        await dbContext.Database.MigrateAsync();

        var bookingTimeService = new BookingTimeService(dbContext);

        var courtName = $"Court-{Guid.NewGuid()}";
        var newCourt = helper.CreateNewCourt(courtName);
        dbContext.Courts.Add(newCourt);
        await dbContext.SaveChangesAsync();

        var newUser = helper.CreateNewUser();
        dbContext.Users.Add(newUser);
        var startTime = DateTime.Now.Date.AddDays(0).AddHours(18).AddMinutes(30);
        var endTime = startTime.AddMinutes(60);

        Booking booking = new Booking
        {
            CourtId = newCourt.Id,
            StartTime = startTime,
            UserId = newUser.Id,
            EndTime = endTime,
            Status = BookingStatus.Confirmed,
        };
        dbContext.Bookings.Add(booking);
        await dbContext.SaveChangesAsync();

        var service = new BookingService(dbContext, bookingTimeService);

        var result = await service.CancelBooking(newUser.Id, booking.Id, CancellationToken.None, true);
        var bookingStatus = await dbContext.Bookings.FirstAsync(x => x.Id == booking.Id);
        Assert.True(result.Success);
        Assert.Equal(BookingStatus.Cancelled, bookingStatus.Status);
    }
    [Fact]
    public async Task RescheduleBooking_BookingNotFound()
    {
        var helper = new HelperUnit();
        var dbContext = helper.DbContextHellper();
        var bookingTimeService = new BookingTimeService(dbContext);

        var newCourtName = $"Court-{Guid.NewGuid()}";
        var newCourt = helper.CreateNewCourt(newCourtName);
        dbContext.Courts.Add(newCourt);
        await dbContext.SaveChangesAsync();
        var user = helper.CreateNewUser();
        dbContext.Users.Add(user);

        var booking = helper.NewBooking(newCourt.Id, user.Id);
        dbContext.Bookings.Add(booking);

        var newStartTime = DateTime.Now.Date.AddDays(3).AddHours(15).AddMinutes(30);

        var service = new BookingService(dbContext, bookingTimeService);

        var result = await service.RescheduleBooking(user.Id, booking.Id, 60, newStartTime, false, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal(Error.BookingNotFound, result.ErrorMessage);

    }
}