using BookingSystem.Api.Models.ResponseModel;

namespace BookingSystem.Api.Services.interfaces;

public interface IBookingHoldService
{
    Task<CreateHoldResponse> CreateHoldAsync(DateOnly date, TimeOnly bookingTime, int duration, int courtId, string userId, CancellationToken cancellationToken);
    Task<CreateHoldResponse> SwitchHoldAsync(DateOnly newDate, TimeOnly newBookingTime, int duration, int courtId, string userId, CancellationToken cancellationToken);
    Task<BoolResponseModel> ReleaseHoldAsync(string userId);
    Task<CacheHoldResponse> GetValidateHoldAsync(string userId);
    Task<BeginFinalizationResponse> BeginFinalizationAsync(string userId);
    Task<BoolResponseModel> CompleteFinalizationAsync(string userId, Guid? lockToken);
    Task<BoolResponseModel> AbortFinalizationAsync(string userId, Guid? lockToken);
}