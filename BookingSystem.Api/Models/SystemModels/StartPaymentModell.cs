using Stripe.V2;

namespace BookingSystem.Api.Models.SystemModels;

public class StartPaymentModell
{
    public int PaymentId {get;set;}
    public int BookingId {get;set;}
    public PaymentStatus Status {get;set;} = PaymentStatus.Pending;
    public decimal Amount {get;set;}
    public string Currency {get;set;} = "";
    public string? CheckoutUrl {get;set;}
    public Error ErrorMessage {get;set;} = Error.none;
}