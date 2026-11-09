namespace BookingSystem.Api.Models.SystemModels;

public class PaymentModell
{
    public int Id {get;set;}
    public int BookingId{get;set;}
    public Booking Booking {get;set;} = null!;
    public PaymentStatus Status {get;set;}
    public decimal Amount {get;set;}
    public string Currency {get;set;} = "";
    public string StripePaymentIntentId {get;set;} = "";
    public DateTime CreatedAtUtc {get;set;} = DateTime.UtcNow;

}