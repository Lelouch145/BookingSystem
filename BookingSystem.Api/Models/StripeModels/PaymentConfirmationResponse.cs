using BookingSystem.Api.Models.SystemModels;

namespace BookingSystem.Api.Models.StripeModels;

public class PaymentConfirmationResponse
{
    public bool Success {get;set;}
    public Error ErrorMessage {get;set;} = Error.none;
}