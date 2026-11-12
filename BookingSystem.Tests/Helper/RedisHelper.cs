using System.IO.Compression;
using BookingSystem.Api.Database;
using BookingSystem.Api.Models.ResponseModel;
using BookingSystem.Api.Models.SystemModels;
using BookingSystem.Api.Services;
using BookingSystem.Api.Services.Caching;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Internal;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using StackExchange.Redis;

namespace BookingSystem.Tests;

public class RedisHelper
{
    public async Task<(HelperUnit helper, AppDbContext dbContext, BookingTimeService bookingTimeService, ILogger<BookingHoldService> logger, IConnectionMultiplexer connection, IDatabase database, string courtName, Court newCourt, ApplicationUser newUser, int duration, DateOnly date, TimeOnly bookingTime, IDbContextFactory<AppDbContext> dbContextFactory)> CreateSetup()
    {
        var helper = new HelperUnit();
        var dbContext = helper.DbContextHellper();
        var bookingTimeService = new BookingTimeService(dbContext);
        var logger = NullLogger<BookingHoldService>.Instance;
        var redisIntegrationHelper = new RedisIntegrationHelper();
        var courtName = $"Court-{Guid.NewGuid()}";
        var newCourt = helper.CreateNewCourt(courtName);
        dbContext.Courts.Add(newCourt);
        var newUser = helper.CreateNewUser();
        dbContext.Users.Add(newUser);
        await dbContext.SaveChangesAsync();
        var duration = 60;
        var date = DateOnly.FromDateTime(DateTime.Today).AddMonths(2).AddDays(14);
        var bookingTime = new TimeOnly(15, 30);
        var (connection, database, _) = redisIntegrationHelper.CreateRedisSetup();

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(dbContext.Database.GetConnectionString())
            .Options;
        
        var factoryMock = new Mock<IDbContextFactory<AppDbContext>>();
        
        factoryMock.Setup(x => x.CreateDbContext())
        .Returns(() => new AppDbContext(options));

        factoryMock.Setup(x => x.CreateDbContextAsync(
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new AppDbContext(options));
        
        var dbContextFactory = factoryMock.Object;


        return (helper, dbContext, bookingTimeService, logger, connection, database, courtName, newCourt, newUser, duration, date, bookingTime, dbContextFactory);
    }
    public async Task<(HelperUnit helper, AppDbContext dbContext, BookingTimeService bookingTimeService, ILogger<BookingHoldService> logger, IConnectionMultiplexer connection, IDatabase database, string courtName, Court newCourt, ApplicationUser newUser, int duration, DateOnly date, TimeOnly bookingTime,BookingHoldService service, CreateHoldResponse createHold, IDbContextFactory<AppDbContext> dbContextFactory)> CreateSetupValidate()
    {
        var helper = new HelperUnit();
        var dbContext = helper.DbContextHellper();
        var bookingTimeService = new BookingTimeService(dbContext);
        var logger = NullLogger<BookingHoldService>.Instance;
        var redisIntegrationHelper = new RedisIntegrationHelper();
        var courtName = $"Court-{Guid.NewGuid()}";
        var newCourt = helper.CreateNewCourt(courtName);
        dbContext.Courts.Add(newCourt);
        var newUser = helper.CreateNewUser();
        dbContext.Users.Add(newUser);
        await dbContext.SaveChangesAsync();
        var duration = 60;
        var date = DateOnly.FromDateTime(DateTime.Today).AddMonths(2).AddDays(14);
        var bookingTime = new TimeOnly(15, 30);
        var (connection, database, _) = redisIntegrationHelper.CreateRedisSetup();
        var service = new BookingHoldService(bookingTimeService, connection, dbContext, logger);
        var createHold = await service.CreateHoldAsync(date, bookingTime, duration, newCourt.Id, newUser.Id, CancellationToken.None);

        var services = new ServiceCollection();
        services.AddDbContextFactory<AppDbContext>(options => 
            options.UseSqlServer(dbContext.Database.GetConnectionString()));
        
        using var provider = services.BuildServiceProvider();

        var dbContextFactory = provider.GetRequiredService<IDbContextFactory<AppDbContext>>();

        return (helper, dbContext, bookingTimeService, logger, connection, database, courtName, newCourt, newUser, duration, date, bookingTime, service, createHold, dbContextFactory);
    }
    public async Task<(HelperUnit helper, AppDbContext dbContext, BookingService bookingService, ApplicationUser newUser)> SetupForConfirmBooking()
    {
        var (helper, dbContext, bookingTimeService, logger, connection , database, courtName, newCourt, newUser, duration, date, bookingTime, service, createHold, _ ) = await CreateSetupValidate();
        var bookingLogger = NullLogger<BookingService>.Instance;
        var cache = helper.DistributedCache();

        var services = new ServiceCollection();
        services.AddDbContextFactory<AppDbContext>(options => 
            options.UseSqlServer(dbContext.Database.GetConnectionString()));
        
        using var provider = services.BuildServiceProvider();

        var dbContextFactory = provider.GetRequiredService<IDbContextFactory<AppDbContext>>();
        var bookingService = new BookingService(dbContext, bookingTimeService, cache, bookingLogger, service, dbContextFactory);
        return(helper, dbContext, bookingService, newUser);
    }

    public string GetSlotHoldIdKey(int courtId, DateOnly date, TimeOnly slotStart)
    {
        return $"hold:slot:{courtId}:{date:yyyy-MM-dd}:{slotStart:HH:mm}";

    }
    public string GetUserHoldIdKey(string userId)
    {
        return $"hold:user:{userId}";
    }
    public string GetHoldDataKey(Guid holdId)
    {
        return $"hold:{holdId}";
    }
    public string GetFinalizingKey(Guid holdId)
    {
        return $"hold:finalizing:{holdId}";
    }
}