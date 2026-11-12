using BookingSystem.Api.Models.SystemModels;

namespace BookingSystem.Api.Models.StripeModels;

public class CheckoutSessionResponse
{
    public string SessionId {get;set;} = "";
    public string? CheckoutUrl {get;set;}
    public Error ErrorMessage {get;set;} = Error.none;
}