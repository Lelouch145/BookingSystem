using BookingSystem.Api.Database;
using BookingSystem.Api.Models.SystemModels;
using BookingSystem.Api.Services;
using BookingSystem.Api.Services.interfaces;
using BookingSystem.Api.Services.PaymentProviders;
using Castle.Core.Logging;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Stripe;
using Microsoft.Extensions.Logging;
using BookingSystem.Api.Services.Caching;
using BookingSystem.Api.Models.ResponseModel;
using BookingSystem.Api.Models.StripeModels;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.VisualBasic;
namespace BookingSystem.Tests;

public class CreateCheckoutTests : IClassFixture<DatabaseFixture>, IAsyncLifetime
{
    private readonly DatabaseFixture _databaseFixture;

    public CreateCheckoutTests(DatabaseFixture databaseFixture)
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
    public async Task StartPayment_BookingNotFound()
    {
        var (helper, _, dbContext, logger, mock, _, _, dbContextFactory, _, _, _) = await CreateSetupCheckout();
        var newUser = helper.CreateNewUser();
        await dbContext.SaveChangesAsync();
        var service = new PaymentService(dbContext, mock.Object, logger, dbContextFactory);

        var startPayment = await service.StartPaymentAsync(newUser.Id, 0);

        Assert.Equal(Error.BookingNotFound, startPayment.ErrorMessage);
    }

    [Fact]
    public async Task StartPayment_BookingNotPending()
    {
        var (helper, stripeClient, dbContext, paymentLogger, mock, bookingTimeService, bookingHoldService, dbContextFactory, createBooking, paymentService, newUser) = await CreateSetupCheckout();

        var changeStatus = await dbContext.Bookings.FirstOrDefaultAsync(x => x.Id == createBooking.ClientBooking.Id);
        Assert.NotNull(changeStatus);

        changeStatus.Status = BookingStatus.Cancelled;
        await dbContext.SaveChangesAsync();

        var startPayment = await paymentService.StartPaymentAsync(newUser.Id, createBooking.ClientBooking.Id);

        Assert.Equal(Error.BookingStatusIsNotPendingPayment, startPayment.ErrorMessage);
    }
    [Fact]
    public async Task StartPayment_BookingExpired()
    {
        var (helper, stripeClient, dbContext, paymentLogger, mock, bookingTimeService, bookingHoldService, dbContextFactory, createBooking, paymentService, newUser) = await CreateSetupCheckout();

        var changeTime = await dbContext.Bookings.FirstOrDefaultAsync(x => x.Id == createBooking.ClientBooking.Id);
        Assert.NotNull(changeTime);
        Assert.NotNull(changeTime.ExpiresAtUtc);
        changeTime.ExpiresAtUtc = DateTime.UtcNow.AddMinutes(-1);

        var startPayment = await paymentService.StartPaymentAsync(newUser.Id, createBooking.ClientBooking.Id);
        Assert.Equal(Error.TimeExpiredForPendingPayment, startPayment.ErrorMessage);

    }
    [Fact]
    public async Task StartPayment_ExpirationIsNull()
    {
        var (helper, stripeClient, dbContext, paymentLogger, mock, bookingTimeService, bookingHoldService, dbContextFactory, createBooking, paymentService, newUser) = await CreateSetupCheckout();

        var changeTime = await dbContext.Bookings.FirstOrDefaultAsync(x => x.Id == createBooking.ClientBooking.Id);
        Assert.NotNull(changeTime);
        Assert.NotNull(changeTime.ExpiresAtUtc);
        changeTime.ExpiresAtUtc = null;
        await dbContext.SaveChangesAsync();

        var startPayment = await paymentService.StartPaymentAsync(newUser.Id, createBooking.ClientBooking.Id);
        Assert.Equal(Error.TimeExpiredForPendingPayment, startPayment.ErrorMessage);

    }
    [Fact]
    public async Task StartPayment_AlreadyHasPendingPayment()
    {
        var (helper, stripeClient, dbContext, paymentLogger, mock, bookingTimeService, bookingHoldService, dbContextFactory, createBooking, _, newUser) = await CreateSetupCheckout();

        mock.Setup(x => x.CreateCheckoutSessionAsync(
            It.IsAny<decimal>(),
            It.IsAny<string>(),
            It.IsAny<int>()))
            .ReturnsAsync(new CheckoutSessionResponse
            {
                SessionId = "cs_test_123",
                CheckoutUrl = "https://example.com"
            });

        var paymentService = new PaymentService(dbContext, mock.Object, paymentLogger, dbContextFactory);
        var firstPayment = await paymentService.StartPaymentAsync(newUser.Id, createBooking.ClientBooking.Id);
        Assert.Equal(Error.none, firstPayment.ErrorMessage);

        var secondPayment = await paymentService.StartPaymentAsync(newUser.Id, createBooking.ClientBooking.Id);
        Assert.Equal(Error.AnotherPaymentIsAlreadyProccessing, secondPayment.ErrorMessage);


    }
    [Fact]
    public async Task StartPayment_Success60Minutes()
    {
        var (helper, stripeClient, dbContext, paymentLogger, mock, bookingTimeService, bookingHoldService, dbContextFactory, createBooking, _, newUser) = await CreateSetupCheckout();

        mock.Setup(x => x.CreateCheckoutSessionAsync(
            It.IsAny<decimal>(),
            It.IsAny<string>(),
            It.IsAny<int>()))
            .ReturnsAsync(new CheckoutSessionResponse
            {
                SessionId = "cs_test_123",
                CheckoutUrl = "https//example.com"
            });

        var paymentService = new PaymentService(dbContext, mock.Object, paymentLogger, dbContextFactory);

        var payment = await paymentService.StartPaymentAsync(newUser.Id, createBooking.ClientBooking.Id);
        mock.Verify(x => x.CreateCheckoutSessionAsync(
            300m,
            "SEK",
            It.IsAny<int>()),
            Times.Once);
    }
    [Fact]
    public async Task StartPayment_Success90Minutes()
    {
        var (helper, stripeClient, dbContext, paymentLogger, mock, bookingTimeService, bookingHoldService, dbContextFactory, createBooking, _, newUser) = await CreateSetupCheckout();

        mock.Setup(x => x.CreateCheckoutSessionAsync(
            It.IsAny<decimal>(),
            It.IsAny<string>(),
            It.IsAny<int>()))
            .ReturnsAsync(new CheckoutSessionResponse
            {
                SessionId = "cs_test_123",
                CheckoutUrl = "https//example.com"
            });
        var changeDuration = await dbContext.Bookings.FirstOrDefaultAsync(x => x.Id == createBooking.ClientBooking.Id);
        Assert.NotNull(changeDuration);
        changeDuration.EndTime = changeDuration.EndTime.AddMinutes(30);
        await dbContext.SaveChangesAsync();
        var paymentService = new PaymentService(dbContext, mock.Object, paymentLogger, dbContextFactory);

        var payment = await paymentService.StartPaymentAsync(newUser.Id, createBooking.ClientBooking.Id);
        mock.Verify(x => x.CreateCheckoutSessionAsync(
            400m,
            "SEK",
            It.IsAny<int>()),
            Times.Once);
        Assert.Equal("https//example.com", payment.CheckoutUrl);
    }
    [Fact]
    public async Task StartPayment_FirstSaveChangesFails()
    {
        var (helper, stripeClient, dbContext, paymentLogger, mock, bookingTimeService, bookingHoldService, dbContextFactory, createBooking, _, newUser) = await CreateSetupCheckout();

        mock.Setup(x => x.CreateCheckoutSessionAsync(
            It.IsAny<decimal>(),
            It.IsAny<string>(),
            It.IsAny<int>()))
            .ReturnsAsync(new CheckoutSessionResponse
            {
                SessionId = "cs_test_123",
                CheckoutUrl = "https//example.com"
            });
        var interceptor = new UpdateException();

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(dbContext.Database.GetConnectionString())
            .AddInterceptors(interceptor)
            .Options;
        await using var interceptedContext = new AppDbContext(options);

        var paymentService = new PaymentService(interceptedContext, mock.Object, paymentLogger, dbContextFactory);
        var payment = await paymentService.StartPaymentAsync(newUser.Id, createBooking.ClientBooking.Id);
        mock.Verify(x => x.CreateCheckoutSessionAsync(
            It.IsAny<decimal>(),
            It.IsAny<string>(),
            It.IsAny<int>()),
            Times.Never
        );
        Assert.Equal(Error.CouldNotSavePayment, payment.ErrorMessage);
    }
    [Fact]
    public async Task StartPayment_StripeFails()
    {
        var (helper, stripeClient, dbContext, paymentLogger, mock, bookingTimeService, bookingHoldService, dbContextFactory, createBooking, _, newUser) = await CreateSetupCheckout();

        mock.Setup(x => x.CreateCheckoutSessionAsync(
            It.IsAny<decimal>(),
            It.IsAny<string>(),
            It.IsAny<int>()))
            .ReturnsAsync(new CheckoutSessionResponse
            {
                ErrorMessage = Error.StripeServiceUnavailable
            });
        var paymentService = new PaymentService(dbContext, mock.Object, paymentLogger, dbContextFactory);

        var payment = await paymentService.StartPaymentAsync(newUser.Id, createBooking.ClientBooking.Id);
        Assert.Equal(Error.StripeServiceUnavailable, payment.ErrorMessage);
    }
    [Fact]
    public async Task StartPayment_SecondSaveChangesFails()
    {
        var (helper, stripeClient, dbContext, paymentLogger, mock, bookingTimeService, bookingHoldService, dbContextFactory, createBooking, _, newUser) = await CreateSetupCheckout();

        mock.Setup(x => x.CreateCheckoutSessionAsync
        (
            It.IsAny<decimal>(),
            It.IsAny<string>(),
            It.IsAny<int>()))
            .ReturnsAsync(new CheckoutSessionResponse
            {
                SessionId = "cs_test_123",
                CheckoutUrl = "https//example.com"
            });
        
        var interceptor = new UpdateExceptionSecond();

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(dbContext.Database.GetConnectionString())
            .AddInterceptors(interceptor)
            .Options;
        
        await using var interceptedContext = new AppDbContext(options);

        var paymentService = new PaymentService(interceptedContext, mock.Object, paymentLogger, dbContextFactory);

        var payment = await paymentService.StartPaymentAsync(newUser.Id, createBooking.ClientBooking.Id);
        Assert.Equal(Error.CouldNotsaveCheckoutSessionId, payment.ErrorMessage);
    }
    [Fact]
    public async Task StartPayment_ExpireCheckoutSessionFails()
    {
       var (helper, stripeClient, dbContext, paymentLogger, mock, bookingTimeService, bookingHoldService, dbContextFactory, createBooking, _, newUser) = await CreateSetupCheckout();

        mock.Setup(x => x.CreateCheckoutSessionAsync
        (
            It.IsAny<decimal>(),
            It.IsAny<string>(),
            It.IsAny<int>()))
            .ReturnsAsync(new CheckoutSessionResponse
            {
                SessionId = "cs_test_123",
                CheckoutUrl = "https//example.com"
            });
        
        var interceptor = new UpdateExceptionSecond();

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(dbContext.Database.GetConnectionString())
            .AddInterceptors(interceptor)
            .Options;
        
        await using var interceptedContext = new AppDbContext(options);
        mock.Setup(x => x.ExpireCheckoutSessionAsync
        (
            It.IsAny<string>()))
        .ThrowsAsync(new StripeException("Simulated Stripe failure"));

        var paymentService = new PaymentService(interceptedContext, mock.Object, paymentLogger, dbContextFactory);

        var payment = await paymentService.StartPaymentAsync(newUser.Id, createBooking.ClientBooking.Id);
        Assert.Equal(Error.StripeSessionStateUnknown, payment.ErrorMessage);
    }
    [Fact]
    public async Task StartPayment_ExpireCheckoutSessionSucceeds()
    {
        var (helper, stripeClient, dbContext, paymentLogger, mock, bookingTimeService, bookingHoldService, dbContextFactory, createBooking, _, newUser) = await CreateSetupCheckout();

        mock.Setup(x => x.CreateCheckoutSessionAsync
        (
            It.IsAny<decimal>(),
            It.IsAny<string>(),
            It.IsAny<int>()))
            .ReturnsAsync(new CheckoutSessionResponse
            {
                SessionId = "cs_test_123",
                CheckoutUrl = "https://example.com"
            });
        mock.Setup(x => x.ExpireCheckoutSessionAsync
        (
            It.IsAny<string>()
        ))
        .Returns(Task.CompletedTask);
        var interceptor = new UpdateExceptionSecond();

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(dbContext.Database.GetConnectionString())
            .AddInterceptors(interceptor)
            .Options;
        
        await using var interceptedContext = new AppDbContext(options);
        var paymentService = new PaymentService(interceptedContext, mock.Object, paymentLogger, dbContextFactory);

        var payment = await paymentService.StartPaymentAsync(newUser.Id, createBooking.ClientBooking.Id);
        var getPayment = await dbContext.PaymentModell.AsNoTracking().FirstOrDefaultAsync(x => x.BookingId == createBooking.ClientBooking.Id);
        Assert.NotNull(getPayment);
        Assert.Equal(PaymentStatus.Cancelled, getPayment.Status);
    }
    [Fact]
    public async Task StartPayment_RecoveryDatabaseFails()
    {
        var (helper, stripeClient, dbContext, paymentLogger, mock, bookingTimeService, bookingHoldService, _, createBooking, _, newUser) = await CreateSetupCheckout();

        mock.Setup(x => x.CreateCheckoutSessionAsync
        (
            It.IsAny<decimal>(),
            It.IsAny<string>(),
            It.IsAny<int>()))
            .ReturnsAsync(new CheckoutSessionResponse
            {
                SessionId = "cs_test_123",
                CheckoutUrl = "https://example.com"
            });
        mock.Setup(x => x.ExpireCheckoutSessionAsync
        (
            It.IsAny<string>()
        ))
        .Returns(Task.CompletedTask);
        var interceptor = new UpdateExceptionSecond();

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(dbContext.Database.GetConnectionString())
            .AddInterceptors(interceptor)
            .Options;
        
        await using var interceptedContext = new AppDbContext(options); 

        var recoveryOptions = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(dbContext.Database.GetConnectionString())
            .AddInterceptors(new UpdateException())
            .Options;
        var factoryMock = new Mock<IDbContextFactory<AppDbContext>>();

        factoryMock.Setup(x => x.CreateDbContext())
            .Returns(() => new AppDbContext(recoveryOptions));
        
        var paymentService = new PaymentService(interceptedContext, mock.Object, paymentLogger, factoryMock.Object);

        var payment = await paymentService.StartPaymentAsync(newUser.Id, createBooking.ClientBooking.Id);

        Assert.Equal(Error.CouldNotUpdatepaymentStatus, payment.ErrorMessage);

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