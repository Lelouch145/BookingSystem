using System.Formats.Asn1;
using BookingSystem.Api.Database;
using BookingSystem.Api.Models.SystemModels;
using BookingSystem.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.VisualBasic;
using Xunit.Sdk;

namespace BookingSystem.Tests;

public class BookingConcurrencyTests
{



    [Fact]
    public async Task ResetDatabase()
    {
        var dbContextService = new DbContextHelper();
        var dbContext = dbContextService.DbContextHellper(); 

        await dbContext.Database.EnsureDeletedAsync();
        await dbContext.Database.MigrateAsync();
    }

    [Fact]
    public async Task TransactionTest()
    {


        var dbContextService = new DbContextHelper();
        var dbContext = dbContextService.DbContextHellper(); 
        var secondDbContext = dbContextService.DbContextHellper();

        await dbContext.Database.MigrateAsync();

        BookingTimeService bookingTimeService = new BookingTimeService(dbContext);
        BookingTimeService bookingTimeServiceSecond = new BookingTimeService(secondDbContext);

        Court newCourt = new Court
        {
            CourtName = $"Court-{Guid.NewGuid()}",
            IsActive = true,
        };
        dbContext.Courts.Add(newCourt);
        await dbContext.SaveChangesAsync();
        var testUser = new ApplicationUser
        {
            UserName = "TestUser",
            Email = "test@test.com"
        };
        var testUser2 = new ApplicationUser
        {
            UserName = "TestUser2",
            Email = "test@test2.com"
        };
        dbContext.Users.AddRange(testUser, testUser2);
        await dbContext.SaveChangesAsync();
        BookingService bookingService = new BookingService(dbContext, bookingTimeService);
        BookingService secondBookingService = new BookingService(secondDbContext, bookingTimeServiceSecond);
        var startTime = DateTime.Now.Date.AddDays(1).AddHours(18).AddMinutes(30);
        var CreateBooking = bookingService.CreateBooking(newCourt.Id, startTime, 60, testUser.Id, CancellationToken.None);
        var secondCreateBooking = secondBookingService.CreateBooking(newCourt.Id, startTime, 60, testUser2.Id, CancellationToken.None);

        var results = await Task.WhenAll(CreateBooking, secondCreateBooking);

        var test = results.FirstOrDefault(x => x.ErrorMessage == Error.BookingTimeIsOverlappingWithAnotherBooking);
        var success = results.FirstOrDefault(x => x.Success);
        var successCount = results.Count(x => x.Success);
        var overlapCount = results.Count(x => x.ErrorMessage == Error.BookingTimeIsOverlappingWithAnotherBooking);
        Assert.NotNull(success);
        Assert.True(success.Success);
        Assert.NotNull(test);
        Assert.Equal(1, successCount);
        Assert.Equal(1, overlapCount);
    }

    [Fact]
    public async Task RowVersionTest()
    {
        var dbContextService = new DbContextHelper();
        var dbContext = dbContextService.DbContextHellper();
        var secondDbContext = dbContextService.DbContextHellper();
   

        await dbContext.Database.MigrateAsync();

        Court newCourt = new Court
        {
            CourtName = $"Court-{Guid.NewGuid()}",
            IsActive = true
        };
        dbContext.Courts.Add(newCourt);
        await dbContext.SaveChangesAsync();
        var newUser = new ApplicationUser
        {
            UserName = "TestUser",
            Email = "test@test.com"
        };
        dbContext.Users.Add(newUser);
        var startTime = DateTime.Now.Date.AddDays(1).AddHours(18).AddMinutes(30);
        var endTime = startTime.AddMinutes(60);
        var updateEndTime = startTime.AddMinutes(90);
        var updateStartTime = DateTime.Now.Date.AddDays(1).AddHours(20).AddMinutes(30);
        Booking newBooking = new Booking
        {
            CourtId = newCourt.Id,
            UserId = newUser.Id,
            StartTime = startTime,
            EndTime = endTime,

        };
        dbContext.Bookings.Add(newBooking);
        await dbContext.SaveChangesAsync();
        var bookingA = await dbContext.Bookings.FirstAsync(x => x.Id == newBooking.Id);
        var bookingB = await secondDbContext.Bookings.FirstAsync(x => x.Id == newBooking.Id);

        bookingA.EndTime = updateEndTime;
        await dbContext.SaveChangesAsync();
        bookingB.StartTime = updateStartTime;
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => secondDbContext.SaveChangesAsync());

    }

    [Fact]
    public async Task CancelBookingConcurrency()
    {
        var dbContextService = new DbContextHelper();
        var dbContext = dbContextService.DbContextHellper();
        var secondDbContext = dbContextService.DbContextHellper();

        await dbContext.Database.MigrateAsync();

        BookingTimeService bookingTimeService = new BookingTimeService(dbContext);
        Court newCourt = new Court
        {
            CourtName = $"Court-{Guid.NewGuid()}",
            IsActive = true
        };
        dbContext.Courts.Add(newCourt);
        await dbContext.SaveChangesAsync();

        var newUser = new ApplicationUser
        {
            UserName = "TestUser",
            Email = "test@test.com"
        };
        dbContext.Users.Add(newUser);
        var startTime = DateTime.Now.Date.AddDays(2).AddHours(18).AddMinutes(30);
        var endTime = startTime.AddMinutes(60);
        Booking newBooking = new Booking
        {
            CourtId = newCourt.Id,
            UserId = newUser.Id,
            StartTime = startTime,
            EndTime = endTime,
        };
        dbContext.Bookings.Add(newBooking);
        await dbContext.SaveChangesAsync();

        var service = new BookingService(dbContext, bookingTimeService);
        var secondService = new BookingService(secondDbContext, bookingTimeService);
        var bookingB = await secondDbContext.Bookings.FirstAsync(x => x.Id == newBooking.Id);
        var firstTask = service.CancelBooking(newUser.Id, newBooking.Id, CancellationToken.None, false);
        var secondTask = secondService.CancelBooking(newUser.Id, newBooking.Id, CancellationToken.None, false);

        var result = await Task.WhenAll(firstTask, secondTask);
        var successResult = result.FirstOrDefault(x => x.Success);
        var errorcode = result.FirstOrDefault(x => x.ErrorMessage == Error.BookingWasModifiedByAnotherRequest);
        var successResultCount = result.Count(x => x.Success);
        var errorCodeCount = result.Count(x => x.ErrorMessage == Error.BookingWasModifiedByAnotherRequest);
        Assert.NotNull(successResult);
        Assert.NotNull(errorcode);
        Assert.True(successResult.Success);
        Assert.Equal(Error.BookingWasModifiedByAnotherRequest, errorcode.ErrorMessage);
        Assert.Equal(1, successResultCount);
        Assert.Equal(1, errorCodeCount);
    }
    
    [Fact]
    public async Task RescheduleBookingConcurrency()
    {
        var dbContextService = new DbContextHelper();
        var dbContext = dbContextService.DbContextHellper();
        var secondDbContext = dbContextService.DbContextHellper();

        await dbContext.Database.MigrateAsync();

        BookingTimeService bookingTimeService = new BookingTimeService(dbContext);
        BookingTimeService bookingTimeServiceSecond = new BookingTimeService(secondDbContext);

        Court newCourt = new Court
        {
            CourtName = $"Court-{Guid.NewGuid()}",
            IsActive = true
        };
        dbContext.Courts.Add(newCourt);
        await dbContext.SaveChangesAsync();

        var newUser = new ApplicationUser
        {
            UserName = "TestUser",
            Email = "test@test.com"
        };
        dbContext.Users.Add(newUser);
        var startTime = DateTime.Now.Date.AddDays(2).AddHours(18).AddMinutes(30);
        var endTime = startTime.AddMinutes(60);
        Booking newBooking = new Booking
        {
            CourtId = newCourt.Id,
            UserId = newUser.Id,
            StartTime = startTime,
            EndTime = endTime,
        };
        dbContext.Bookings.Add(newBooking);
        await dbContext.SaveChangesAsync();

        var service = new BookingService(dbContext, bookingTimeService);
        var secondService = new BookingService(secondDbContext, bookingTimeServiceSecond);
        var newStartTime = DateTime.Now.Date.AddDays(3).AddHours(15).AddMinutes(30);

        var bookingB = await secondDbContext.Bookings.FirstAsync(x => x.Id == newBooking.Id);

        var firstTask = service.RescheduleBooking(newUser.Id, newBooking.Id, 60, newStartTime, false, CancellationToken.None);
        var secondTask = secondService.RescheduleBooking(newUser.Id, newBooking.Id, 90, newStartTime, false, CancellationToken.None);

        var result = await Task.WhenAll(firstTask, secondTask);

        var success = result.FirstOrDefault(x => x.Success);
        var successCount = result.Count(x => x.Success);
        var errorcode = result.FirstOrDefault(x => x.ErrorMessage == Error.BookingWasModifiedByAnotherRequest);
        var errorCodeCount = result.Count(x => x.ErrorMessage == Error.BookingWasModifiedByAnotherRequest);

        Assert.NotNull(success);
        Assert.True(success.Success);
        Assert.Equal(1, successCount);
        Assert.Equal(1, errorCodeCount);
        Assert.NotNull(errorcode);
        Assert.Equal(Error.BookingWasModifiedByAnotherRequest, errorcode.ErrorMessage);

    }


}