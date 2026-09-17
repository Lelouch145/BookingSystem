using BookingSystem.Api.Database;
using Microsoft.Extensions.Configuration;
using Microsoft.EntityFrameworkCore;

using BookingSystem.Api.Models.SystemModels;
using BookingSystem.Api.Services;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BookingSystem.Tests;

public class HelperUnit
{
    public AppDbContext DbContextHellper()
    {
        var builder = new ConfigurationBuilder().AddUserSecrets<BackGroundTest>().Build();
        var connectionString = builder.GetConnectionString("DefaultConnection");
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(connectionString).Options;
        var dbContext = new AppDbContext(options);
        return dbContext;
    }
    public Court CreateNewCourt(string courtName)
    {
        Court newCourt = new Court
        {
            CourtName = courtName,
            Description = "",
            IsActive = true,
        };
        return newCourt;
    }
    public ApplicationUser CreateNewUser()
    {
        var newUser = new ApplicationUser
        {
            UserName = $"TestUser-{Guid.NewGuid()}",
            Email = $"test-{Guid.NewGuid()}@test.com"
        };
        return newUser;
    }
    public Booking NewBooking(int newCourtId, string newUserId)
    {
        var startTime = DateTime.Now.Date.AddDays(1).AddHours(18).AddMinutes(30);
        var endTime = startTime.AddMinutes(60);

        Booking booking = new Booking
        {
            CourtId = newCourtId,
            StartTime = startTime,
            UserId = newUserId,
            EndTime = endTime,
            Status = BookingStatus.Confirmed,
        };
        return booking;
    }
    public IDistributedCache DistributedCache()
    {
        var service = new ServiceCollection();
        service.AddDistributedMemoryCache();

        var serviceProvider = service.BuildServiceProvider();
        var cache = serviceProvider.GetRequiredService<IDistributedCache>();

        return cache;
    }


}