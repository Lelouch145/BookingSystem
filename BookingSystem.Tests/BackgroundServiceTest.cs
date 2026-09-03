namespace BookingSystem.Tests;

using BookingSystem.Api.Database;
using BookingSystem.Api.Models.SystemModels;
using BookingSystem.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

public class BackGroundTest
{

    [Fact]
    public async Task TestAsync()
    {
        var builder = new ConfigurationBuilder().AddUserSecrets<BackGroundTest>().Build();
        var connectionString = builder.GetConnectionString("DefaultConnection");
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(connectionString).Options;
        var dbContext = new AppDbContext(options);


        ApplicationUser newUser = new ApplicationUser
        {
            UserName = "TestUser",
            Email = "test@test.com"
        };
        dbContext.Users.Add(newUser);

        Court newCourt = new Court
        {
            CourtName = $"Court-{Guid.NewGuid()}",
            IsActive = true
        };
        dbContext.Courts.Add(newCourt);
        await dbContext.SaveChangesAsync();

        var startTime = new DateTime(2026, 05, 07, 18, 30, 00);
        var endTime = startTime.AddMinutes(60);
        Booking newBooking = new Booking
        {
            CourtId = newCourt.Id,
            UserId = newUser.Id,
            StartTime = startTime,
            EndTime = endTime,
            Status = BookingStatus.Confirmed,
        };
        dbContext.Bookings.Add(newBooking);
        await dbContext.SaveChangesAsync();
        dbContext.ChangeTracker.Clear();


        var BackgroundService = new BookingCompletionService(dbContext);
        await BackgroundService.CompleteExpiresBookingAsync(CancellationToken.None);

        var updateBooking = await dbContext.Bookings.FirstAsync(x => x.Id == newBooking.Id);


        Assert.Equal(BookingStatus.Completed, updateBooking.Status);

    }

}