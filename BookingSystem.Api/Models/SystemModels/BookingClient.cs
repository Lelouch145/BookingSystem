using BookingSystem.Api.Models.SystemModels;
using Microsoft.Identity.Client;
using Microsoft.Net.Http.Headers;

namespace BookingSystem.Api.Models.ResponseModel;

public class BookingClient
{
    public int Id { get; set; }
    public int CourtId { get; set; }
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
    public int Duration { get; set; }
    public BookingStatus Status { get; set; }
    public DateTime CreatedAt { get; set; }
}