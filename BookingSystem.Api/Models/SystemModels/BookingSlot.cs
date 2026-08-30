using System.Xml;
using Microsoft.Identity.Client;

namespace BookingSystem.Api.Models.SystemModels;

public class BookingSlot
{
    public int Id { get; set; }
    public int BookingId { get; set; }
    public Booking Booking { get; set; } = null!;
    public int CourtId { get; set; }
    public Court Court { get; set; } = null!;
    public DateTime SlotStart { get; set; }

}