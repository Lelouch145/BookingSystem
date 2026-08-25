using BookingSystem.Api.Models.SystemModels;

namespace BookingSystem.Api.Models.ResponseModel;

public class CancelBookingOrUpdate
{
    public Error ErrorMessage { get; set; } = Error.none;
    public bool Success { get; set; }
}