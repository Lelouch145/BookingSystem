using BookingSystem.Api.Models.SystemModels;

namespace BookingSystem.Api.Models.ResponseModel;

public class BookingDto
{
    public int Id {get;set;}
    public int CourtId {get;set;}
    public string UserId {get;set;} = string.Empty;
    public DateTime StartTime {get;set;}
    public DateTime EndTime {get;set;}
    public BookingStatus Status {get;set;} = BookingStatus.Confirmed;
    public DateTime CreatedAt {get;set;} = DateTime.UtcNow;
}