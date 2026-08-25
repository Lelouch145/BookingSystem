using Microsoft.AspNetCore.SignalR;
using Microsoft.Net.Http.Headers;


namespace BookingSystem.Api.Models.SystemModels;

public class Court
{
    public int Id { get; set; }
    public string CourtName { get; set; } = string.Empty;
    public string Description { get; set; } = "";
    public bool IsActive { get; set; } = true;
    public Error ErrorMessage { get; set; }

    public ICollection<Booking> Bookings { get; set; } = new List<Booking>();
}