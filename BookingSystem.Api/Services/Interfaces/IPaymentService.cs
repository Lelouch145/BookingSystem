using BookingSystem.Api.Models.SystemModels;

namespace BookingSystem.Api.Services.interfaces;

public interface IPaymentService
{
    Task<StartPaymentModell> StartPaymentAsync(string userId, int bookingId);
}