using System.Data;

namespace BookingSystem.Api.Models.SystemModels;

public class Booking
{
    public int Id { get; set; }

    public int CourtId { get; set; }
    public Court Court { get; set; } = null!;

    public string UserId { get; set; } = string.Empty;
    public ApplicationUser User { get; set; } = null!;

    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }

    public BookingStatus Status { get; set; } = BookingStatus.Confirmed;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public ICollection<BookingSlot> BookingSlots { get; set; } = new List<BookingSlot>();
    public byte[] RowVersion { get; set; } = null!;
}