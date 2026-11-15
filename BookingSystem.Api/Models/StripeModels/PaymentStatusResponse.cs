using BookingSystem.Api.Models.SystemModels;

namespace BookingSystem.Api.Models.StripeModels;

public class PaymentStatusResponse
{
    public string SessionId {get;set;} = "";
    public string SessionStatus {get;set;} = "";
    public string PaymentStatus {get;set;} = "";
    public string? PaymentIntentId {get;set;}
    public string? ClientReferenceId {get;set;}
    public Error ErrorMessage {get;set;} = Error.none;
}