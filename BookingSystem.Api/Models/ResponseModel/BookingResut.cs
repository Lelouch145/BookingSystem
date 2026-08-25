using BookingSystem.Api.Models.SystemModels;
using Microsoft.Identity.Client;
using Microsoft.Net.Http.Headers;

namespace BookingSystem.Api.Models.ResponseModel;

public class BookingResult
{
    public bool Success { get; set; }
    public BookingClient ClientBooking { get; set; } = new BookingClient();
    public Error ErrorMessage { get; set; } = Error.none;
}