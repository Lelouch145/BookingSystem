using BookingSystem.Api.Models.SystemModels;

namespace BookingSystem.Api.Models.ResponseModel;

public class ReleaseHoldResponse
{
    public bool Success {get; set;}
    public Error ErrorMessage {get; set;} = Error.none;
}