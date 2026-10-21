using BookingSystem.Api.Models.SystemModels;

namespace BookingSystem.Api.Models.ResponseModel;

public class CacheHoldResponse
{
    public CacheHoldModel? CacheHold {get;set;} 
    public Error ErrorMessage {get;set;} = Error.none;
}