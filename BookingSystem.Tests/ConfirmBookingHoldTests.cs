using BookingSystem.Api.Models.ResponseModel;
using BookingSystem.Api.Models.SystemModels;
using BookingSystem.Api.Services;
using BookingSystem.Api.Services.Caching;
using BookingSystem.Api.Services.interfaces;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace BookingSystem.Tests;

public class ConfirmBookingHoldTests : IClassFixture<DatabaseFixture>, IAsyncLifetime
{
    private readonly DatabaseFixture _databaseFixture;

    public ConfirmBookingHoldTests(DatabaseFixture databaseFixture)
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
    public async Task ConfirmBookingHold_Success()
    {
        var helperClass = new RedisHelper();
        var (helper, dbContext, bookingService, newUser) = await helperClass.SetupForConfirmBooking();

        var confirmBooking = await bookingService.ConfirmBookingHold(newUser.Id, CancellationToken.None);
        var findBooking = await dbContext.Bookings.FirstOrDefaultAsync(x => x.UserId == newUser.Id);
        Assert.True(confirmBooking.Success);
        Assert.NotNull(findBooking);
        Assert.Equal(findBooking.StartTime, confirmBooking.ClientBooking.StartTime);

    }
    [Fact]
    public async Task ConfirmBookingHold_NoActiveHold()
    {
        var helperClass = new RedisHelper();

        var (helper, dbContext, bookingTimeService, logger, connection, database, courtName, newCourt, newUser, duration, date, bookingTime) = await helperClass.CreateSetup();
        var cache = helper.DistributedCache();
        var loggerBookingService = NullLogger<BookingService>.Instance;
        var holdService = new BookingHoldService(bookingTimeService, connection, dbContext, logger);

        var bookingService = new BookingService(dbContext, bookingTimeService, cache, loggerBookingService, holdService);

        var confirmBooking = await bookingService.ConfirmBookingHold(newUser.Id, CancellationToken.None);

        Assert.Equal(Error.UserDosentHaveAnActiveHold, confirmBooking.ErrorMessage);
    }
    [Fact]
    public async Task ConfirmBookingHold_OverlappingBooking()
    {
        var helperClass = new RedisHelper();
        var (helper, dbContext, bookingTimeService, logger, connection , database, courtName, newCourt, newUser, duration, date, bookingTime, service, createHold ) = await helperClass.CreateSetupValidate();
        Assert.Equal(Error.none, createHold.ErrorMessage);
        var cache = helper.DistributedCache();
        var bookingLogger = NullLogger<BookingService>.Instance;
        var dateTime = date.ToDateTime(bookingTime);

        await database.KeyExpireAsync(GetHoldDataKey(createHold.HoldId), TimeSpan.FromSeconds(30));
        var bookingService = new BookingService(dbContext, bookingTimeService, cache, bookingLogger, service);
        var createBooking = await bookingService.CreateBooking(newCourt.Id, dateTime, duration, newUser.Id, CancellationToken.None, null);
        Assert.Equal(Error.none, createBooking.ErrorMessage);
        var confirmBooking = await bookingService.ConfirmBookingHold(newUser.Id, CancellationToken.None);
        Assert.Equal(Error.BookingTimeIsOverlappingWithAnotherBooking, confirmBooking.ErrorMessage);
        var ttl = await database.KeyTimeToLiveAsync(GetHoldDataKey(createHold.HoldId));
        Assert.NotNull(ttl);
        Assert.InRange(ttl.Value.TotalSeconds, 25, 30);

    }
    [Fact]
    public async Task ConfirmBookingHold_ConcurrentRequests()
    {
        var helperClass = new RedisHelper();
        var (helper, dbContext, bookingTimeService, logger, connection , database, courtName, newCourt, newUser, duration, date, bookingTime, service, createHold ) = await helperClass.CreateSetupValidate();
        var cache = helper.DistributedCache();
        var secondDbContext = helper.DbContextHellper();
        var bookingServiceLogger = NullLogger<BookingService>.Instance;
        var firstBookingService = new BookingService(dbContext,bookingTimeService, cache, bookingServiceLogger, service);
        var secondBookingService = new BookingService(secondDbContext, bookingTimeService, cache, bookingServiceLogger, service);
        
        var firstConfirm = firstBookingService.ConfirmBookingHold(newUser.Id, CancellationToken.None);
        var secondConfirm = secondBookingService.ConfirmBookingHold(newUser.Id, CancellationToken.None);

        var result = await Task.WhenAll(firstConfirm, secondConfirm);
        var lookIfOnlyOneBookignInDatabase = dbContext.Bookings.Where(x => x.UserId == newUser.Id);
        Assert.Single(result, x => x.Success);
        Assert.Single(result, x => !x.Success);
        Assert.Single(lookIfOnlyOneBookignInDatabase);
    }
    [Fact]
    public async Task ConfirmBookingHold_CompleteUnavailable_Retries()
    {
        var helperClass = new RedisHelper();
        var(helper, dbContext, bookingTimeService, logger, connection, database, courtName, newCourt, newUser, duration, date, bookingTime) = await helperClass.CreateSetup();
        var cache = helper.DistributedCache();
        var bookingLogger = NullLogger<BookingService>.Instance;
        var lockToken = Guid.NewGuid();
        var holdServiceMock = new Mock<IBookingHoldService>();

        holdServiceMock.SetupSequence(x => x.CompleteFinalizationAsync(
            It.IsAny<string>(),
            It.IsAny<Guid?>()))
            .ReturnsAsync(new BoolResponseModel
            {
                Success = false,
                ErrorMessage = Error.HoldServiceUnavailable
            })
            .ReturnsAsync(new BoolResponseModel
            {
                Success = true
            });
        var hold = new CacheHoldModel
        {
            HoldId = Guid.NewGuid(),
            CourtId = newCourt.Id,
            UserId = newUser.Id,
            BookingDate = date,
            BookingTime = bookingTime,
            Duration = duration

        };
        holdServiceMock
        .Setup(x => x.BeginFinalizationAsync(It.IsAny<string>()))
        .ReturnsAsync(new BeginFinalizationResponse
        {
            Success = true,
            LockToken = lockToken,
            CacheHoldModel = hold
        });

        var bookingService = new BookingService(dbContext, bookingTimeService, cache, bookingLogger, holdServiceMock.Object);

        var bookingConfirm = await bookingService.ConfirmBookingHold(newUser.Id, CancellationToken.None);
        Assert.True(bookingConfirm.Success);

        holdServiceMock.Verify(
            x => x.CompleteFinalizationAsync(newUser.Id, It.Is<Guid?>(token => token == lockToken)),
            Times.Exactly(2)
        );
    }
    
    [Fact]
    public async Task ConfirmBookingHold_CompleteLockMismatch_NoRetry()
    {
        var helperClass = new RedisHelper();
        var(helper, dbContext, bookingTimeService, logger, connection, database, courtName, newCourt, newUser, duration, date, bookingTime) = await helperClass.CreateSetup();
        var bookingLogger = NullLogger<BookingService>.Instance;
        var cache = helper.DistributedCache();
        var hold = new CacheHoldModel
        {
            CourtId = newCourt.Id,
            UserId = newUser.Id,
            BookingDate = date,
            BookingTime = bookingTime,
            Duration = duration
        };

        var mockHoldService = new Mock<IBookingHoldService>();

        mockHoldService.Setup(x => x.BeginFinalizationAsync(It.IsAny<string>()))
        .ReturnsAsync(new BeginFinalizationResponse
        {
            Success = true,
            LockToken = Guid.NewGuid(),
            CacheHoldModel = hold
        });
        mockHoldService.Setup(x => x.CompleteFinalizationAsync(It.IsAny<string>(), It.IsAny<Guid?>()))
        .ReturnsAsync(new BoolResponseModel
        {
            Success = false,
            ErrorMessage = Error.FinalizationLockMismatch
        });

        var bookingService = new BookingService(dbContext, bookingTimeService, cache, bookingLogger, mockHoldService.Object);

        var confirmBooking = await bookingService.ConfirmBookingHold(newUser.Id, CancellationToken.None);

        Assert.True(confirmBooking.Success);

        mockHoldService.Verify(
            x => x.CompleteFinalizationAsync(It.IsAny<string>(), It.IsAny<Guid?>()),
            Times.Once()
        );
    }
    [Fact]
    public async Task ConfirmBookingHold_CreateBookingThrows()
    {
        var helperClass = new RedisHelper();
        var(helper, dbContext, bookingTimeService, logger, connection, database, courtName, newCourt, newUser, duration, date, bookingTime) = await helperClass.CreateSetup();
        var cache = helper.DistributedCache();
        var bookingLogger = NullLogger<BookingService>.Instance;
        var hold = new CacheHoldModel
        {
            CourtId = newCourt.Id,
            UserId = newUser.Id,
            BookingDate = date,
            BookingTime = bookingTime,
            Duration = duration
        };

        var bookingHoldMoq = new Mock<IBookingHoldService>();

        bookingHoldMoq.Setup(x => x.BeginFinalizationAsync(It.IsAny<string>()))
        .ReturnsAsync(new BeginFinalizationResponse
        {
            Success = true,
            LockToken = Guid.NewGuid(),
            CacheHoldModel = hold
        });

        var bookingService = new BookingService(dbContext, bookingTimeService, cache, bookingLogger, bookingHoldMoq.Object);

        await dbContext.DisposeAsync();

        await Assert.ThrowsAsync<ObjectDisposedException>(async () =>
        {
            await bookingService.ConfirmBookingHold(newUser.Id, CancellationToken.None);
        });

        bookingHoldMoq.Verify(
            x => x.CompleteFinalizationAsync(
            It.IsAny<string>(),
            It.IsAny<Guid?>()),
            Times.Never());
        bookingHoldMoq.Verify(
            x => x.AbortFinalizationAsync(
            It.IsAny<string>(),
            It.IsAny<Guid?>()),
            Times.Never());
        


    }
    [Fact]
    public async Task ConfirmBookingHold_DuplicateHoldId()
    {
        var helperClass = new RedisHelper();
        var(helper, dbContext, bookingTimeService, logger, connection, database, courtName, newCourt, newUser, duration, date, bookingTime) = await helperClass.CreateSetup();
        var bookingLogger = NullLogger<BookingService>.Instance;
        var cache = helper.DistributedCache();

        var holdServiceMock = new Mock<IBookingHoldService>();
        var bookingService = new BookingService(dbContext, bookingTimeService, cache, bookingLogger, holdServiceMock.Object);

        var dateTime = date.ToDateTime(bookingTime);
        var holdId = Guid.NewGuid();

        var createBooking = await bookingService.CreateBooking(newCourt.Id, dateTime, duration, newUser.Id, CancellationToken.None, holdId);
        Assert.True(createBooking.Success);
        var dateNew = DateTime.Today
            .AddYears(1)
            .AddMonths(2)
            .AddDays(20)
            .AddHours(10)
            .AddMinutes(00);
        
        var createNewBooking = await bookingService.CreateBooking(newCourt.Id, dateNew, duration, newUser.Id, CancellationToken.None, holdId);
        Assert.False(createNewBooking.Success);
        Assert.Equal(Error.DuplicateHoldId, createNewBooking.ErrorMessage);
    }
    private string GetHoldDataKey(Guid holdId)
    {
        return $"hold:{holdId}";
    }

}