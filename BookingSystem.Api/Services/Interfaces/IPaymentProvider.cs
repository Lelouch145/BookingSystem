using BookingSystem.Api.Models.StripeModels;

namespace BookingSystem.Api.Services.interfaces;

public interface IPaymentProvider
{
    Task<CheckoutSessionResponse> CreateCheckoutSessionAsync(decimal amount, string currency, int paymentId);
    Task ExpireCheckoutSessionAsync(string sessionId);
}