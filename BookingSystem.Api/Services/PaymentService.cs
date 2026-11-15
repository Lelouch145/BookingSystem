using System.Data.Common;
using BookingSystem.Api.Database;
using BookingSystem.Api.Models.StripeModels;
using BookingSystem.Api.Models.SystemModels;
using BookingSystem.Api.Services.interfaces;
using BookingSystem.Api.Services.PaymentProviders;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualBasic;
using Stripe;

namespace BookingSystem.Api.Services;

public class PaymentService : IPaymentService
{
    private const string Currency = "SEK";
    private readonly AppDbContext _dbContext;
    private readonly IPaymentProvider _stripePaymentProvider;
    private readonly ILogger<PaymentService> _logger;
    private readonly IDbContextFactory<AppDbContext> _dbContextFactory;

    public PaymentService(AppDbContext dbContext, IPaymentProvider stripePaymentProvider, ILogger<PaymentService> logger, IDbContextFactory<AppDbContext> dbContextFactory)
    {
        _dbContext = dbContext;
        _stripePaymentProvider = stripePaymentProvider;
        _logger = logger;
        _dbContextFactory = dbContextFactory;
    }


    public async Task<StartPaymentModell> StartPaymentAsync(string userId, int bookingId)
    {
        var getBooking = await _dbContext.Bookings.FirstOrDefaultAsync(x => x.UserId == userId  && x.Id == bookingId);

        var validate = ValidateBooking(getBooking);

        if(validate != Error.none)
        {
            return new StartPaymentModell
            {
                ErrorMessage = validate
            };
        }

        var booking = await _dbContext.PaymentModell
            .AnyAsync(x => x.BookingId == bookingId && x.Status == PaymentStatus.Pending);

        if (booking)
        {
            return new StartPaymentModell
            {
                ErrorMessage = Error.AnotherPaymentIsAlreadyProccessing
            };
        }

        decimal amount;
        var durationTime = getBooking!.EndTime - getBooking.StartTime;
        int duration = (int)durationTime.TotalMinutes;

        if(duration == 60)
        {
            amount = 300;
        }
        else
        {
            amount = 400;
        }
        var paymentModell = new PaymentModell
        {
            BookingId = getBooking.Id,
            Booking = getBooking,
            Status = PaymentStatus.Pending,
            Amount = amount,
            Currency = Currency,
            StripePaymentIntentId = null
        };
        _dbContext.PaymentModell.Add(paymentModell);
        try
        {
            await _dbContext.SaveChangesAsync();
        }
        catch(DbUpdateException ex)
        {
            _logger.LogError(ex, "Failed to save payment");
            return new StartPaymentModell
            {
                ErrorMessage = Error.CouldNotSavePayment
            };
        }

        var stripe = await _stripePaymentProvider.CreateCheckoutSessionAsync(amount, Currency, paymentModell.Id);
        if(stripe.ErrorMessage != Error.none)
        {
            return new StartPaymentModell
            {
                ErrorMessage = stripe.ErrorMessage
            };
        }
        paymentModell.StripeCheckOutSessionId = stripe.SessionId;
        try
        {
            await _dbContext.SaveChangesAsync();
        }
        catch(DbUpdateException ex)
        {
            _logger.LogError(ex, "Failed to save CheckoutSessionId");

            try
            {
                await _stripePaymentProvider.ExpireCheckoutSessionAsync(stripe.SessionId);
            }
            catch(StripeException x)
            {
                _logger.LogError(x, "Could not expire Stripe session {SessionId} for payment {PaymentId}"
                ,   stripe.SessionId, paymentModell.Id);

                return new StartPaymentModell
                {
                    ErrorMessage = Error.StripeSessionStateUnknown
                };
            }
            await using var factory = _dbContextFactory.CreateDbContext();
            PaymentModell? findPaymentModell;
            try
            {
                findPaymentModell = await factory.PaymentModell.FirstOrDefaultAsync(x => x.Id == paymentModell.Id);
            }
            catch(DbException d)
            {
                _logger.LogError(d, "Failed to retrieve payment {PaymentId} during recovery", paymentModell.Id);

                return new StartPaymentModell
                {
                    ErrorMessage = Error.CouldNotRetrievePayment
                };
            }

            if(findPaymentModell == null)
            {
                return new StartPaymentModell
                {
                    ErrorMessage = Error.CouldNotsaveCheckoutSessionId
                };
            }
            findPaymentModell.Status = PaymentStatus.Cancelled;
            try
            {
                await factory.SaveChangesAsync();

                return new StartPaymentModell
                {
                    ErrorMessage = Error.CouldNotsaveCheckoutSessionId
                };
            }
            catch(DbUpdateException e)
            {
                _logger.LogError(e, "Could not change the status with dbContextFactory");

                return new StartPaymentModell
                {
                    ErrorMessage = Error.CouldNotUpdatepaymentStatus
                };
            }


        }
        return new StartPaymentModell
        {
            PaymentId = paymentModell.Id,
            BookingId = getBooking.Id,
            Amount = amount,
            Currency = Currency,
            CheckoutUrl = stripe.CheckoutUrl
            
        };
    }
    
    private async Task<PaymentStatusResponse> PaymentStatusAsync(string userId, int bookingId, int paymentId)
    {
        var getBooking = await _dbContext.Bookings.FirstOrDefaultAsync(x => x.UserId == userId 
            && x.Id == bookingId);


        if(getBooking == null)
        {
            return new PaymentStatusResponse
            {
                ErrorMessage = Error.BookingNotFound
            };
        }
        
        var getPayment = await _dbContext.PaymentModell.FirstOrDefaultAsync(x => x.BookingId == getBooking.Id && x.Id == paymentId);

        if(getPayment == null)
        {
            return new PaymentStatusResponse
            {
                ErrorMessage = Error.CouldNotFindPayment
            };
        }
        if(string.IsNullOrWhiteSpace(getPayment.StripeCheckOutSessionId))
        {
            return new PaymentStatusResponse
            {
                ErrorMessage = Error.CheckoutSessionIdIsNull
            };
        }
        
        var getSessionStatus = await _stripePaymentProvider.GetPaymentStatusAsync(getPayment.StripeCheckOutSessionId);

        if(getSessionStatus.ErrorMessage != Error.none)
        {
            return new PaymentStatusResponse
            {
                ErrorMessage = getSessionStatus.ErrorMessage
            };
        }
        if(getSessionStatus.SessionId != getPayment.StripeCheckOutSessionId)
        {
            return new PaymentStatusResponse
            {
                ErrorMessage = Error.PaymentSessionMismatch
            };
        }

        return getSessionStatus;
    }


    private Error ValidateBooking(Booking? booking)
    {
        if(booking == null)
        {
            return Error.BookingNotFound;
        }

        if(booking.Status != BookingStatus.PendingPayment)
        {
            return Error.BookingStatusIsNotPendingPayment;
        };

        if(booking.ExpiresAtUtc <= DateTime.UtcNow || booking.ExpiresAtUtc == null)
        {
            return Error.TimeExpiredForPendingPayment;
        };
        return Error.none;
    }
}