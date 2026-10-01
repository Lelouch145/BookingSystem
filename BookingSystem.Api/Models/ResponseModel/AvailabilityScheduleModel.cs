using BookingSystem.Api.Models.SystemModels;

namespace BookingSystem.Api.Services;

public class Schedule
{
    public List<DateTime> Times {get;set;} = [];
    public Error ErrorMessage {get;set;} = Error.none;
}