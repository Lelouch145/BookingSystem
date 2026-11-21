using BookingSystem.Api.Database;
using BookingSystem.Api.Models.ResponseModel;
using BookingSystem.Api.Models.SystemModels;
using BookingSystem.Api.Services;
using BookingSystem.Api.Services.Caching;
using BookingSystem.Api.Services.interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Stripe;

namespace BookingSystem.Tests;


public class HandlePaymentFailedTests : IClassFixture<DatabaseFixture>, IAsyncLifetime
{
    public readonly DatabaseFixture _databaseFixture;

    public HandlePaymentFailedTests(DatabaseFixture databaseFixture)
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
    public async Task HandlePaymnetFailed_Succeeds()
    {
        var (helper, stripeClient, dbContext, paymentLogger, mock, bookingTimeService, bookingHoldService, dbContextFactory, createBooking, paymentService, newUser) = await CreateSetupCheckout();

        var paymentModell = new PaymentModell
        {
            BookingId = createBooking.ClientBooking.Id,
            Status = PaymentStatus.Pending,
            StripeCheckOutSessionId = "placeHolder"
        };
        dbContext.PaymentModell.Add(paymentModell);
        await dbContext.SaveChangesAsync();
        var test = await paymentService.HandlePaymentFailedAsync(paymentModell.StripeCheckOutSessionId);

        Assert.True(test.Success);
    }
    [Fact]
    public async Task HandlePaymentFailed_AlreadyFailed()
    {
        var (helper, stripeClient, dbContext, paymentLogger, mock, bookingTimeService, bookingHoldService, dbContextFactory, createBooking, paymentService, newUser) = await CreateSetupCheckout();

        var paymentModell = new PaymentModell
        {
            BookingId = createBooking.ClientBooking.Id,
            Status = PaymentStatus.Failed,
            StripeCheckOutSessionId = "placeHolder"
        };
        dbContext.PaymentModell.Add(paymentModell);
        await dbContext.SaveChangesAsync();
        var test = await paymentService.HandlePaymentFailedAsync(paymentModell.StripeCheckOutSessionId);

        Assert.True(test.Success);
    }
    [Fact]
    public async Task HandlePaymentFailed_InvalidState()
    {
        var (helper, stripeClient, dbContext, paymentLogger, mock, bookingTimeService, bookingHoldService, dbContextFactory, createBooking, paymentService, newUser) = await CreateSetupCheckout();

        var paymentModell = new PaymentModell
        {
            BookingId = createBooking.ClientBooking.Id,
            Status = PaymentStatus.Paid,
            StripeCheckOutSessionId = "placeHolder"
        };
        dbContext.PaymentModell.Add(paymentModell);
        await dbContext.SaveChangesAsync();
        var test = await paymentService.HandlePaymentFailedAsync(paymentModell.StripeCheckOutSessionId);

        Assert.False(test.Success);
        Assert.Equal(Error.InvalidPaymentState, test.ErrorMessage);
    }
    [Fact]
    public async Task HandlePaymnetFailed_ConcurrencyConflict()
    {
        var (helper, stripeClient, dbContext, paymentLogger, mock, bookingTimeService, bookingHoldService, dbContextFactory, createBooking, paymentService, newUser) = await CreateSetupCheckout();

        var paymentModell = new PaymentModell
        {
            BookingId = createBooking.ClientBooking.Id,
            Status = PaymentStatus.Pending,
            StripeCheckOutSessionId = "placeHolder"
        };
        dbContext.PaymentModell.Add(paymentModell);
        await dbContext.SaveChangesAsync();
        var secondContext = await dbContextFactory.CreateDbContextAsync();

        var getPayment = await secondContext.PaymentModell.FirstAsync(x => x.BookingId == createBooking.ClientBooking.Id);
        getPayment.Status = PaymentStatus.Paid;
        await secondContext.SaveChangesAsync();
        var test = await paymentService.HandlePaymentFailedAsync(paymentModell.StripeCheckOutSessionId);

        Assert.False(test.Success);
        Assert.Equal(Error.PaymentFailureConcurrencyConflict, test.ErrorMessage);
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