using BookingSystem.Api.Models.SystemModels;

namespace BookingSystem.Api.Models.ResponseModel;

public class AvailabilityResponse
{
    public List<DateTime> Times { get; set; } = new List<DateTime>();
    public Error ErrorMessage { get; set; }
}