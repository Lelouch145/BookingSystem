using BookingSystem.Api.Models.SystemModels;
using Microsoft.VisualBasic;

namespace BookingSystem.Api.Models.ResponseModel;

public class CreateHoldResponse
{
    public Error ErrorMessage {get; set;} = Error.none;
    public Guid HoldId {get;set;}
    public DateTimeOffset ExpiresAt {get;set;}
}