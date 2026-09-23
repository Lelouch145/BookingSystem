using System.Globalization;
using System.Text.Json;
using System.Threading.Tasks;
using BookingSystem.Api.Models.ResponseModel;
using BookingSystem.Api.Models.SystemModels;
using BookingSystem.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace BookingSystem.Tests;

public class BookingServiceTests : IClassFixture<DatabaseFixture>, IAsyncLifetime
{
     private const string BookingsAll = "bookings:all";
    private const string BookingsUser = "bookings:user:";
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
        var cache = helper.DistributedCache();
        var logger = NullLogger.Instance;

        var bookingTimeService = new BookingTimeService(dbContext);


        var courtName = $"Court{Guid.NewGuid()}";
        var newCourt = helper.CreateNewCourt(courtName);
        dbContext.Courts.Add(newCourt);
        await dbContext.SaveChangesAsync(CancellationToken.None);


        var newUser = helper.CreateNewUser();
        dbContext.Users.Add(newUser);
        var bookingService = new BookingService(dbContext, bookingTimeService, cache, logger);

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
        var cache = helper.DistributedCache();
        var logger = NullLogger.Instance;

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

        var service = new BookingService(dbContext, bookingTimeService, cache, logger);

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
        var cache = helper.DistributedCache();
        var logger = NullLogger.Instance;

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

        var service = new BookingService(dbContext, bookingTimeService, cache, logger);

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
        var cache = helper.DistributedCache();
        var logger = NullLogger.Instance;

        var newCourtName = $"Court-{Guid.NewGuid()}";
        var newCourt = helper.CreateNewCourt(newCourtName);
        dbContext.Courts.Add(newCourt);
        var user = helper.CreateNewUser();
        dbContext.Users.Add(user);

        var service = new BookingService(dbContext, bookingTimeService, cache, logger);

        var startTime = DateTime.Now.Date.AddDays(1).AddHours(18).AddMinutes(30);
        var result = await service.CreateBooking(newCourt.Id, startTime, 60, user.Id, CancellationToken.None);

        Assert.Equal(Error.CouldNotFindTheCourtInDataBase, result.ErrorMessage);
    }
    [Fact]
    public async Task CancelBooking_BookingNotFound()
    {
        var helper = new HelperUnit();
        var dbContext = helper.DbContextHellper();
        var cache = helper.DistributedCache();
        var logger = NullLogger.Instance;

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

        var service = new BookingService(dbContext, bookingTimeService, cache, logger);

        var result = await service.CancelBooking(newUser.Id, booking.Id, CancellationToken.None, false);
        Assert.False(result.Success);
        Assert.Equal(Error.BookingNotFound, result.ErrorMessage);
    }
    [Fact]
    public async Task CancelBooking_BookingIsAlreadyCancelled()
    {
        var helper = new HelperUnit();
        var dbContext = helper.DbContextHellper();
        var cache = helper.DistributedCache();
        var logger = NullLogger.Instance;

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

        var service = new BookingService(dbContext, bookingTimeService, cache, logger);

        var result = await service.CancelBooking(newUser.Id, booking.Id, CancellationToken.None, false);
        Assert.False(result.Success);
        Assert.Equal(Error.BookingIsAlreadyCancelled, result.ErrorMessage);
    }

    [Fact]
    public async Task CancelBooking_CannotCancelBooking24HoursBeforeTheBooking()
    {
        var helper = new HelperUnit();
        var dbContext = helper.DbContextHellper();
        var cache = helper.DistributedCache();
        var logger = NullLogger.Instance;

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

        var service = new BookingService(dbContext, bookingTimeService, cache, logger);

        var result = await service.CancelBooking(newUser.Id, booking.Id, CancellationToken.None, false);
        Assert.False(result.Success);
        Assert.Equal(Error.CannotCancelBooking24HoursBeforeTheBooking, result.ErrorMessage);
    }

    [Fact]
    public async Task CancelBooking_AdminCancel24HoursBeforeTheBooking()
    {
        var helper = new HelperUnit();
        var dbContext = helper.DbContextHellper();
        var cache = helper.DistributedCache();
        var logger = NullLogger.Instance;

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

        var service = new BookingService(dbContext, bookingTimeService, cache, logger);

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
        var cache = helper.DistributedCache();
        var logger = NullLogger.Instance;

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

        var service = new BookingService(dbContext, bookingTimeService, cache, logger);

        var result = await service.RescheduleBooking(user.Id, booking.Id, 60, newStartTime, false, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal(Error.BookingNotFound, result.ErrorMessage);

    }

    [Fact]
    public async Task CreateBooking_CacheDeleteConfirm()
    {
        var helper = new HelperUnit();
        var dbContext = helper.DbContextHellper();
        var cache = helper.DistributedCache();
        var logger = NullLogger.Instance;
        
        var courtName = $"Court-{Guid.NewGuid()}";
        var newCourt = helper.CreateNewCourt(courtName);
        dbContext.Courts.Add(newCourt);
        var newUser = helper.CreateNewUser();
        dbContext.Users.Add(newUser);
        await dbContext.SaveChangesAsync();

        var bookingTimeService = new BookingTimeService(dbContext);
        var bookingService = new BookingService(dbContext, bookingTimeService, cache, logger);

        await cache.SetStringAsync(BookingsAll, "Test-cache");
        await cache.SetStringAsync($"{BookingsUser}{newUser.Id}", "Test-userCache");
        var cacheBeforeResult = await cache.GetStringAsync(BookingsAll);
        var cacheBeforeResultUserId = await cache.GetStringAsync($"{BookingsUser}{newUser.Id}");
        Assert.NotNull(cacheBeforeResult);
        Assert.NotNull(cacheBeforeResultUserId);
        var startTime = DateTime.UtcNow.Date.AddDays(2).AddHours(18);

        var result = await bookingService.CreateBooking(newCourt.Id, startTime, 60, newUser.Id, CancellationToken.None);

        var cacheAfterResult = await cache.GetStringAsync(BookingsAll);
        var cacheAfterResultUserId = await cache.GetStringAsync($"{BookingsUser}{newUser.Id}");
        Assert.Null(cacheAfterResult);
        Assert.Null(cacheAfterResultUserId);


    }

    [Fact]
    public async Task GetUserBooking_CacheHit()
    {
        var helper = new HelperUnit();
        var dbContext = helper.DbContextHellper();
        var cache = helper.DistributedCache();
        var logger = NullLogger.Instance;
        var bookingTimeService = new BookingTimeService(dbContext);

        var newUser = helper.CreateNewUser();
        dbContext.Users.Add(newUser);
        var newCourt = helper.CreateNewCourt($"Court-{Guid.NewGuid()}");
        dbContext.Courts.Add(newCourt);
        await dbContext.SaveChangesAsync();

        var newBooking = helper.NewBooking(newCourt.Id, newUser.Id);
        dbContext.Bookings.Add(newBooking);
        await dbContext.SaveChangesAsync();

        var cacheBooking = new BookingDto
        {
            Id = newBooking.Id,
            CourtId = newCourt.Id,
            UserId = newUser.Id,
            StartTime = newBooking.StartTime,
            EndTime = newBooking.EndTime,
            Status = newBooking.Status,
            CreatedAt = newBooking.CreatedAt
        };
        var cacheBookingList = new List<BookingDto>
        {
            cacheBooking
        };
        var json = JsonSerializer.Serialize<List<BookingDto>>(cacheBookingList);
        await cache.SetStringAsync($"{BookingsUser}{newUser.Id}", json);
        
        var service = new BookingService(dbContext, bookingTimeService, cache, logger);

        var result = await service.GetUserBookings(newUser.Id, CancellationToken.None);
        var resultResponse = result.First(x => x.Id == newBooking.Id);
        var cacheGet = await cache.GetStringAsync($"{BookingsUser}{newUser.Id}");
        Assert.Equal(newBooking.Id, resultResponse.Id);
        Assert.Equal(newCourt.Id, resultResponse.CourtId);
        Assert.NotNull(cacheGet);
        
        

    }

    [Fact]
    public async Task GetUserBooking_CacheMiss()
    {
        var helper = new HelperUnit();
        var dbContext = helper.DbContextHellper();
        var cache = helper.DistributedCache();
        var logger = NullLogger.Instance;
        var bookingTimeService = new BookingTimeService(dbContext);

        var newUser = helper.CreateNewUser();
        dbContext.Users.Add(newUser);
        var newCourt = helper.CreateNewCourt($"Court-{Guid.NewGuid()}");
        dbContext.Courts.Add(newCourt);
        await dbContext.SaveChangesAsync();

        var newBooking = helper.NewBooking(newCourt.Id, newUser.Id);
        dbContext.Bookings.Add(newBooking);
        await dbContext.SaveChangesAsync();
        
        var service = new BookingService(dbContext, bookingTimeService, cache, logger);

        var result = await service.GetUserBookings(newUser.Id, CancellationToken.None);
        var resultResponse = result.First(x => x.CourtId == newCourt.Id);
        var cacheGet = await cache.GetStringAsync($"{BookingsUser}{newUser.Id}");
        Assert.NotNull(cacheGet);
        var cacheDeserialize = JsonSerializer.Deserialize<List<BookingDto>>(cacheGet!); 
        Assert.NotNull(cacheDeserialize);
        var test = cacheDeserialize.First(x => x.UserId == newUser.Id);
        Assert.Equal(newUser.Id, resultResponse.UserId);
        Assert.Equal(newCourt.Id, test.CourtId);

    }

    [Fact]
    public async Task GetAllBookings_CacheHit()
    {
        var helper = new HelperUnit();
        var dbContext = helper.DbContextHellper();
        var cache = helper.DistributedCache();
        var logger = NullLogger.Instance;
        var bookingTimeService = new BookingTimeService(dbContext);

        var newUser = helper.CreateNewUser();
        dbContext.Users.Add(newUser);
        var newCourt = helper.CreateNewCourt($"Court-{Guid.NewGuid()}");
        dbContext.Courts.Add(newCourt);
        await dbContext.SaveChangesAsync();

        var newBooking = helper.NewBooking(newCourt.Id, newUser.Id);
        dbContext.Bookings.Add(newBooking);
        await dbContext.SaveChangesAsync();

        var cacheBooking = new BookingDto
        {
            Id = newBooking.Id,
            CourtId = newCourt.Id,
            UserId = newBooking.UserId,
            StartTime = newBooking.StartTime,
            EndTime = newBooking.EndTime,
            Status = newBooking.Status,
            CreatedAt = newBooking.CreatedAt
        };
        var cacheBookingList = new List<BookingDto>
        {
            cacheBooking
        };
        var json = JsonSerializer.Serialize<List<BookingDto>>(cacheBookingList);
        await cache.SetStringAsync(BookingsAll, json);
        
        var service = new BookingService(dbContext, bookingTimeService, cache, logger);

        var result = await service.GetAllBookings(CancellationToken.None);
        var resultResponse = result.First(x => x.Id == newBooking.Id);
        var cacheGet = await cache.GetStringAsync(BookingsAll);
        Assert.Equal(newBooking.Id, resultResponse.Id);
        Assert.Equal(newCourt.Id, resultResponse.CourtId);
        Assert.NotNull(cacheGet);
        
        

    }

    [Fact]
    public async Task GetAllBookings_CacheMiss()
    {
        var helper = new HelperUnit();
        var dbContext = helper.DbContextHellper();
        var cache = helper.DistributedCache();
        var logger = NullLogger.Instance;
        var bookingTimeService = new BookingTimeService(dbContext);

        var newUser = helper.CreateNewUser();
        dbContext.Users.Add(newUser);
        var newCourt = helper.CreateNewCourt($"Court-{Guid.NewGuid()}");
        dbContext.Courts.Add(newCourt);
        await dbContext.SaveChangesAsync();

        var newBooking = helper.NewBooking(newCourt.Id, newUser.Id);
        dbContext.Bookings.Add(newBooking);
        await dbContext.SaveChangesAsync();
        
        var service = new BookingService(dbContext, bookingTimeService, cache, logger);

        var result = await service.GetAllBookings(CancellationToken.None);
        var resultResponse = result.First(x => x.CourtId == newCourt.Id);
        var cacheGet = await cache.GetStringAsync(BookingsAll);
        Assert.NotNull(cacheGet);
        var cacheDeserialize = JsonSerializer.Deserialize<List<BookingDto>>(cacheGet!); 
        Assert.NotNull(cacheDeserialize);
        var test = cacheDeserialize.First(x => x.UserId == newUser.Id);
        Assert.Equal(newUser.Id, resultResponse.UserId);
        Assert.Equal(newCourt.Id, test.CourtId);
    }

    [Fact]
    public async Task CancelBooking_CacheUserDelte_AdminIsDeleting()
    {
        var helper = new HelperUnit();
        var cache = helper.DistributedCache();
        var dbContext = helper.DbContextHellper();
        var logger = NullLogger.Instance;
        var bookingTimeService = new BookingTimeService(dbContext);

        var newUser = helper.CreateNewUser();
        dbContext.Users.Add(newUser);
        var newCourt = helper.CreateNewCourt($"Court-{Guid.NewGuid()}");
        dbContext.Courts.Add(newCourt);
        await dbContext.SaveChangesAsync();

        var newBooking = helper.NewBooking(newCourt.Id, newUser.Id);
        dbContext.Bookings.Add(newBooking);
        await dbContext.SaveChangesAsync();
        await cache.SetStringAsync(BookingsAll, "Test-CacheAll");
        await cache.SetStringAsync($"{BookingsUser}{newUser.Id}", "Test-CacheUser");

        var cacheAllBeforeResult = await cache.GetStringAsync(BookingsAll);
        Assert.NotNull(cacheAllBeforeResult);
        var cacheUserBeforeResult = await cache.GetStringAsync($"{BookingsUser}{newUser.Id}");
        Assert.NotNull(cacheUserBeforeResult);

        var service = new BookingService(dbContext, bookingTimeService, cache, logger);
        var result = await service.CancelBooking(newUser.Id, newBooking.Id, CancellationToken.None, true);

        var cacheDeleteConfirmAll = await cache.GetStringAsync(BookingsAll);
        Assert.Null(cacheDeleteConfirmAll);
        var cacheDeleteConfirmUser = await cache.GetStringAsync($"{BookingsUser}{newUser.Id}");
        Assert.Null(cacheDeleteConfirmUser);
    }

    [Fact]
    public async Task GetUserBookings_RedisDown()
    {
        var helper = new HelperUnit();
        var cache = new FakeDistributedCache();
        var dbContext = helper.DbContextHellper();
        var logger = NullLogger.Instance;
        var bookingTimeService = new BookingTimeService(dbContext);

        var newUser = helper.CreateNewUser();
        dbContext.Users.Add(newUser);
        var newCourt = helper.CreateNewCourt($"Court-{Guid.NewGuid()}");
        dbContext.Courts.Add(newCourt);
        await dbContext.SaveChangesAsync();

        var newBooking = helper.NewBooking(newCourt.Id, newUser.Id);
        dbContext.Bookings.Add(newBooking);
        await dbContext.SaveChangesAsync();

        var service = new BookingService(dbContext, bookingTimeService, cache, logger);

        var result = await service.GetUserBookings(newUser.Id, CancellationToken.None);
        var findBooking = result.First(x => x.UserId == newUser.Id);

        Assert.Equal(newUser.Id, findBooking.UserId);
        Assert.Equal(newCourt.Id, findBooking.CourtId);

    }

    [Fact]
    public async Task CreateBooking_RedisDown()
    {
        var helper = new HelperUnit();
        var dbContext = helper.DbContextHellper();
        var cache = new FakeDistributedCache();
        var logger = NullLogger.Instance;
        
        var courtName = $"Court-{Guid.NewGuid()}";
        var newCourt = helper.CreateNewCourt(courtName);
        dbContext.Courts.Add(newCourt);
        var newUser = helper.CreateNewUser();
        dbContext.Users.Add(newUser);
        await dbContext.SaveChangesAsync();

        var bookingTimeService = new BookingTimeService(dbContext);
        var bookingService = new BookingService(dbContext, bookingTimeService, cache, logger);

        var startTime = DateTime.UtcNow.Date.AddDays(2).AddHours(18);

        var result = await bookingService.CreateBooking(newCourt.Id, startTime, 60, newUser.Id, CancellationToken.None);
        var databaseSave = dbContext.Bookings.First(x => x.CourtId == newCourt.Id);
        Assert.True(result.Success);
        Assert.Equal(Error.none, result.ErrorMessage);
        Assert.Equal(newUser.Id, databaseSave.UserId);


    }
}