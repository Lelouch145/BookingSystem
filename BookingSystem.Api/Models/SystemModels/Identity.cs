namespace BookingSystem.Api.Models.SystemModels;
using Microsoft.AspNetCore.Identity;

public class ApplicationUser : IdentityUser
{
    public ICollection<Booking> Bookings { get; set; } = new List<Booking>();
}