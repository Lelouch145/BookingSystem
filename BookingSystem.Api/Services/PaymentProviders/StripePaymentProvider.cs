using BookingSystem.Api.Database;
using BookingSystem.Api.Models.StripeModels;
using Stripe;
using Stripe.Checkout;
using Stripe.V2;
using BookingSystem.Api.Models.SystemModels;
using BookingSystem.Api.Services.interfaces;

namespace BookingSystem.Api.Services.PaymentProviders;

public class StripePaymentProvider : IPaymentProvider
{
    private readonly StripeClient _stripeClient;
    private readonly ILogger<StripePaymentProvider> _logger;

    public StripePaymentProvider(StripeClient stripeClient, ILogger<StripePaymentProvider> logger)
    {
        _stripeClient = stripeClient;
        _logger = logger;
    }

    public async Task<CheckoutSessionResponse> CreateCheckoutSessionAsync(decimal amount, string currency, int paymentId)
    {
        var options = new SessionCreateOptions
        {
            Mode = "payment",
            SuccessUrl = "http://localhost:5183/payment/success",
            CancelUrl = "http://localhost:5183/payment/cancel",
            ClientReferenceId = paymentId.ToString(),

            LineItems = new List<SessionLineItemOptions>
            {
                new SessionLineItemOptions
                {
                    Quantity = 1,

                    PriceData = new SessionLineItemPriceDataOptions
                    {
                        Currency = currency.ToLowerInvariant(),
                        UnitAmount = (long)(amount * 100m),

                        ProductData = new SessionLineItemPriceDataProductDataOptions
                        {
                            Name = "Padelbana"
                        }
                    }
                }
            }
        };

        var service = new SessionService(_stripeClient);
        Session session;
        try
        {
            session = await service.CreateAsync(options);
        }
        catch(StripeException ex)
        {
            _logger.LogError(ex, "Stripe Checkout failed");
            return new CheckoutSessionResponse
            {
                ErrorMessage = Error.StripeServiceUnavailable
            };
        }
        if(session.Url == null)
        {
            return new CheckoutSessionResponse
            {
                ErrorMessage = Error.StripeSessionUrlIsNull
            };
        }

        return new CheckoutSessionResponse
        {
            SessionId = session.Id,
            CheckoutUrl = session.Url
        };
    }

    public async Task ExpireCheckoutSessionAsync(string sessionId)
    {
        var service = new SessionService(_stripeClient);
        await service.ExpireAsync(sessionId);
    }
}