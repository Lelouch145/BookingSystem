using BookingSystem.Api.Database;
using BookingSystem.Api.Models.ResponseModel;
using BookingSystem.Api.Models.SystemModels;
using BookingSystem.Api.Services;
using BookingSystem.Api.Services.Caching;
using BookingSystem.Api.Services.interfaces;
using Castle.Components.DictionaryAdapter.Xml;
using Castle.Core.Logging;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Stripe;
using BookingSystem.Api.Models.StripeModels;
using NuGet.Frameworks;

namespace BookingSystem.Tests;

public class HandlePaymentSucceededTests : IClassFixture<DatabaseFixture>, IAsyncLifetime
{
    private readonly DatabaseFixture _databaseFixture;

    public HandlePaymentSucceededTests(DatabaseFixture databaseFixture)
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
    public async Task HandlePaymentSucceeded_BookingNotFound()
    {
        var helper = new HelperUnit();
        var dbContext = helper.DbContextHellper();
        var mockProvider = new Mock<IPaymentProvider>();
        var mockFactory = new Mock<IDbContextFactory<AppDbContext>>();
        var logger = NullLogger<PaymentService>.Instance;

        var paymentService = new PaymentService(dbContext, mockProvider.Object, logger, mockFactory.Object);

        var test = await paymentService.HandlePaymentSucceededAsync("test", 1, 2);

        Assert.Equal(Error.BookingNotFound, test.ErrorMessage);   
    }
    [Fact]
    public async Task HandlePaymentSucceeded_PaymentNotFound()
    {
        var (helper, stripeClient, dbContext, paymentLogger, mock, bookingTimeService, bookingHoldService, dbContextFactory, createBooking, _, newUser) = await CreateSetupCheckout();

        var paymentService = new PaymentService(dbContext, mock.Object, paymentLogger, dbContextFactory);

        var test = await paymentService.HandlePaymentSucceededAsync(newUser.Id, createBooking.ClientBooking.Id, 0);

        Assert.Equal(Error.CouldNotFindPayment, test.ErrorMessage);


    }
    [Fact]
    public async Task HandlePaymentSucceeded_AlreadyConfirmed()
    {
        var (helper, stripeClient, dbContext, paymentLogger, mock, bookingTimeService, bookingHoldService, dbContextFactory, createBooking, paymentService, newUser) = await CreateSetupCheckout();

        var paymentModell = new PaymentModell
        {
            BookingId = createBooking.ClientBooking.Id,
            Status = PaymentStatus.Paid,
        };
        dbContext.PaymentModell.Add(paymentModell);
        var bookingSql = await dbContext.Bookings.FirstAsync(x => x.UserId == newUser.Id);
        bookingSql.Status = BookingStatus.Confirmed;
        await dbContext.SaveChangesAsync();

        var test = await paymentService.HandlePaymentSucceededAsync(newUser.Id, createBooking.ClientBooking.Id, paymentModell.Id);

        Assert.True(test.Success);
        Assert.Equal(Error.none, test.ErrorMessage);
    }
    [Fact]
    public async Task HandlePaymentSucceeded_InvalidState()
    {
        var (helper, stripeClient, dbContext, paymentLogger, mock, bookingTimeService, bookingHoldService, dbContextFactory, createBooking, paymentService, newUser) = await CreateSetupCheckout();

        var paymentModell = new PaymentModell
        {
            BookingId = createBooking.ClientBooking.Id,
            Status = PaymentStatus.Expired,
        };
        dbContext.PaymentModell.Add(paymentModell);
        await dbContext.SaveChangesAsync();

        var test = await paymentService.HandlePaymentSucceededAsync(newUser.Id, createBooking.ClientBooking.Id, paymentModell.Id);

        Assert.Equal(Error.InvalidPaymentState, test.ErrorMessage);
    }
    [Fact]
    public async Task HandlePaymentSucceeded_StripeFails()
    {
        var (helper, stripeClient, dbContext, paymentLogger, mock, bookingTimeService, bookingHoldService, dbContextFactory, createBooking, _, newUser) = await CreateSetupCheckout();

        var paymentModell = new PaymentModell
        {
            BookingId = createBooking.ClientBooking.Id,
            Status = PaymentStatus.Pending,
            StripeCheckOutSessionId = "placeHolder"
        };
        dbContext.PaymentModell.Add(paymentModell);
        await dbContext.SaveChangesAsync();

        var stripeMock = new Mock<IPaymentProvider>();

        stripeMock.Setup(x => x.GetPaymentStatusAsync(
            It.IsAny<string>()))
        .ReturnsAsync(new PaymentStatusResponse
        {
            ErrorMessage = Error.StripeServiceUnavailable
        });

        var paymentService = new PaymentService(dbContext, stripeMock.Object, paymentLogger, dbContextFactory);

        var test = await paymentService.HandlePaymentSucceededAsync(newUser.Id, createBooking.ClientBooking.Id, paymentModell.Id);

        Assert.Equal(Error.StripeServiceUnavailable, test.ErrorMessage);
    }
    
    [Fact]
    public async Task HandlePaymentSucceeded_NotPaid()
    {
        var (helper, stripeClient, dbContext, paymentLogger, mock, bookingTimeService, bookingHoldService, dbContextFactory, createBooking, _, newUser) = await CreateSetupCheckout();

        var paymentModell = new PaymentModell
        {
            BookingId = createBooking.ClientBooking.Id,
            StripeCheckOutSessionId = "placeholder",
            Status = PaymentStatus.Pending
        }; 
        dbContext.PaymentModell.Add(paymentModell);
        await dbContext.SaveChangesAsync();

        var stripeMock = new Mock<IPaymentProvider>();

        stripeMock.Setup(x => x.GetPaymentStatusAsync
        (
            It.IsAny<string>()))
        .ReturnsAsync(new PaymentStatusResponse
        {
            SessionStatus = "open",
            PaymentStatus = "unpaid",
            SessionId = "placeholder",
            ErrorMessage = Error.none
        });

        var paymentService = new PaymentService(dbContext, stripeMock.Object, paymentLogger, dbContextFactory);

        var test = await paymentService.HandlePaymentSucceededAsync(newUser.Id, createBooking.ClientBooking.Id, paymentModell.Id);

        Assert.False(test.Success);
        Assert.Equal(Error.PaymentIsNotPayedYet, test.ErrorMessage);
    }
    [Fact]
    public async Task HandlePaymentSucceeded_Succeeds()
    {
        var (helper, stripeClient, dbContext, paymentLogger, mock, bookingTimeService, bookingHoldService, dbContextFactory, createBooking, _, newUser) = await CreateSetupCheckout();

        var paymentModell = new PaymentModell
        {
            BookingId = createBooking.ClientBooking.Id,
            StripeCheckOutSessionId = "placeholder",
            Status = PaymentStatus.Pending
        }; 
        dbContext.PaymentModell.Add(paymentModell);
        await dbContext.SaveChangesAsync();

        var stripeMock = new Mock<IPaymentProvider>();

        stripeMock.Setup(x => x.GetPaymentStatusAsync
        (
            It.IsAny<string>()))
        .ReturnsAsync(new PaymentStatusResponse
        {
            SessionStatus = "complete",
            PaymentStatus = "paid",
            SessionId = "placeholder",
            ErrorMessage = Error.none
        });

        var paymentService = new PaymentService(dbContext, stripeMock.Object, paymentLogger, dbContextFactory);

        var test = await paymentService.HandlePaymentSucceededAsync(newUser.Id, createBooking.ClientBooking.Id, paymentModell.Id);

        Assert.True(test.Success);
        Assert.Equal(Error.none, test.ErrorMessage);

        var getBooking = await dbContext.Bookings.FirstAsync(x => x.UserId == newUser.Id);
        var paymentSql = await dbContext.PaymentModell.FirstAsync(x => x.BookingId == getBooking.Id);
        Assert.Equal(BookingStatus.Confirmed, getBooking.Status);
        Assert.Equal(PaymentStatus.Paid, paymentSql.Status);

    }
    [Fact]
    public async Task HandlePaymentSucceeded_ConcurrencyConflict()
    {
        var (helper, stripeClient, dbContext, paymentLogger, mock, bookingTimeService, bookingHoldService, dbContextFactory, createBooking, _, newUser) = await CreateSetupCheckout();

        var paymentModell = new PaymentModell
        {
            BookingId = createBooking.ClientBooking.Id,
            StripeCheckOutSessionId = "placeholder",
            Status = PaymentStatus.Pending
        }; 
        dbContext.PaymentModell.Add(paymentModell);
        await dbContext.SaveChangesAsync();

        var stripeMock = new Mock<IPaymentProvider>();

        stripeMock.Setup(x => x.GetPaymentStatusAsync(It.IsAny<string>()))
            .Returns(async () =>
            {
                await using var secondContext = await dbContextFactory.CreateDbContextAsync();

                var booking = await secondContext.Bookings.FirstAsync(x => x.Id == createBooking.ClientBooking.Id);;

                booking.Status = BookingStatus.Cancelled;

                await secondContext.SaveChangesAsync();

                return new PaymentStatusResponse
                {
                    SessionId = "placeholder",
                    SessionStatus = "complete",
                    PaymentStatus = "paid",
                    ErrorMessage = Error.none
                };
            });
        var paymentService = new PaymentService(dbContext, stripeMock.Object, paymentLogger, dbContextFactory);

        var test = await paymentService.HandlePaymentSucceededAsync(newUser.Id, createBooking.ClientBooking.Id, paymentModell.Id);

        Assert.False(test.Success);
        Assert.Equal(Error.PaymentSuccessConcurrencyConflict, test.ErrorMessage);
    }
    [Fact]
    public async Task HandlePaymentSucceeded_DbUpdateFails()
    {
        var (helper, stripeClient, dbContext, paymentLogger, mock, bookingTimeService, bookingHoldService, dbContextFactory, createBooking, _, newUser) = await CreateSetupCheckout();

        var paymentModell = new PaymentModell
        {
            BookingId = createBooking.ClientBooking.Id,
            StripeCheckOutSessionId = "placeholder",
            Status = PaymentStatus.Pending
        }; 
        dbContext.PaymentModell.Add(paymentModell);
        await dbContext.SaveChangesAsync();

        var stripeMock = new Mock<IPaymentProvider>();

        stripeMock.Setup(x => x.GetPaymentStatusAsync(It.IsAny<string>()))
            .ReturnsAsync( () =>
            {
                
                var newPaymentModell = new PaymentModell
                {
                    BookingId = -999,
                    StripeCheckOutSessionId = "placeholder",
                    Status = PaymentStatus.Pending
                };
                dbContext.PaymentModell.Add(newPaymentModell);
                return new PaymentStatusResponse
                {
                    SessionId = "placeholder",
                    SessionStatus = "complete",
                    PaymentStatus = "paid",
                    ErrorMessage = Error.none
                };
            });
        
        var paymentService = new PaymentService(dbContext, stripeMock.Object, paymentLogger, dbContextFactory);

        var test = await paymentService.HandlePaymentSucceededAsync(newUser.Id, createBooking.ClientBooking.Id, paymentModell.Id);

        Assert.Equal(Error.PaymentSuccessUpdateFailed, test.ErrorMessage);
    }

    private async Task<(HelperUnit helper, StripeClient stripeClient, AppDbContext dbContext, ILogger<PaymentService> paymentLogger, Mock<IPaymentProvider> mock, BookingTimeService bookingTimeService, Mock<IBookingHoldService> bookingHoldService, IDbContextFactory<AppDbContext> dbContextFactory, BookingResult createBooking, PaymentService paymentService, ApplicationUser newUser)> CreateSetupCheckout()
    {
        var helperClass = new RedisHelper();
        var(helper, dbContext, bookingTimeService, logger, connection, database, courtName, newCourt, newUser, duration, date, bookingTime, dbContextFactory) = await helperClass.CreateSetup();
        var cache = helper.DistributedCache();
        var bookingDate = date.ToDateTime(bookingTime);
        var bookingServiceLogger = NullLogger<BookingService>.Instance;
        var bookingHoldService = new Mock<IBookingHoldService>();
        var bookingService = new BookingService(dbContext, bookingTimeService, cache, bookingServiceLogger, bookingHoldService.Object, dbContextFactory);

        var holdService = new BookingHoldService(bookingTimeService, connection, dbContext, logger);
        var createHold = await holdService.CreateHoldAsync(date, bookingTime, duration, newCourt.Id, newUser.Id, CancellationToken.None);
        var createBooking = await bookingService.CreateBooking(newCourt.Id, bookingDate, duration, newUser.Id, CancellationToken.None, createHold.HoldId);

        var mock = new Mock<IPaymentProvider>();
        var paymentLogger = NullLogger<PaymentService>.Instance;
        var paymentService = new PaymentService(dbContext, mock.Object, paymentLogger, dbContextFactory);
        var stripeClient = new StripeClient();


        return(helper, stripeClient, dbContext, paymentLogger, mock, bookingTimeService, bookingHoldService, dbContextFactory, createBooking, paymentService, newUser);
    }
}