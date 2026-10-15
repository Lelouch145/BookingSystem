namespace BookingSystem.Api.Models.SystemModels;

public class CacheHoldModel
{
    public Guid HoldId {get;set;} = Guid.NewGuid();

    public int CourtId {get;set;}
    public string UserId {get;set;} = string.Empty;
    public DateOnly BookingDate {get;set;}
    public TimeOnly BookingTime {get;set;}
    public int Duration {get;set;}
}