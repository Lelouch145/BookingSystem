using BookingSystem.Api.Models.ResponseModel;

namespace BookingSystem.Api.Services.interfaces;

public interface IBookingHoldService
{
    Task<CreateHoldResponse> CreateHoldAsync(DateOnly date, TimeOnly bookingTime, int duration, int courtId, string userId, CancellationToken cancellationToken);
    Task<CreateHoldResponse> SwitchHoldAsync(DateOnly newDate, TimeOnly newBookingTime, int duration, int courtId, string userId, CancellationToken cancellationToken);
    Task<ReleaseHoldResponse> ReleaseHoldAsync(string userId);
    Task<CacheHoldResponse> GetValidateHoldAsync(string userId);
}