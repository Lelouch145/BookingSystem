using BookingSystem.Api.Models.StripeModels;
using BookingSystem.Api.Models.SystemModels;

namespace BookingSystem.Api.Services.interfaces;

public interface IPaymentService
{
    Task<StartPaymentModell> StartPaymentAsync(string userId, int bookingId);
    Task<PaymentConfirmationResponse> HandlePaymentSucceededAsync(string userId, int bookingId, int paymentId);
    Task<PaymentConfirmationResponse> HandleWebhookSucceededAsync(string sessionId);
    Task<PaymentConfirmationResponse> HandlePaymentFailedAsync(string sessionId);
}