using System.ComponentModel;
using System.IO.Compression;
using BookingSystem.Api.Database;
using BookingSystem.Api.Models.SystemModels;
using Microsoft.EntityFrameworkCore;

namespace BookingSystem.Api.Services;

public class AvailabilityService
{
    private readonly BookingTimeService _bookingTimeService;
    private readonly AppDbContext _dbContext;

    public AvailabilityService(BookingTimeService bookingTimeService, AppDbContext dbContext)
    {
        _bookingTimeService = bookingTimeService;
        _dbContext = dbContext;
    }
    public async Task<Schedule> AvailabilitySchedule(DateOnly bookingDate, int duration, int courtId, CancellationToken cancellationToken)
    {
        var courtCheck = _dbContext.Courts.Any(x => x.Id == courtId);
        if (!courtCheck)
        {
            return new Schedule
            {
                ErrorMessage = Error.CouldNotFindTheCourtInDataBase
            };
        }
        var bookingDateTime = bookingDate.ToDateTime(TimeOnly.MinValue);
        var tomorrowDateTime = bookingDateTime.AddDays(1);

        var AvailabilityCheck = _bookingTimeService.AvailabilityCheck(bookingDateTime, duration);

        if(AvailabilityCheck.ErrorMessage != Error.none)
        {
            return new Schedule
            {
                ErrorMessage = AvailabilityCheck.ErrorMessage
            };
        }
        var availabileTime = AvailabilityCheck.Times;
        var occupiedTimes = await _dbContext.BookingSlots.Where(x => x.CourtId == courtId && 
        x.SlotStart >= bookingDateTime && 
        x.SlotStart < tomorrowDateTime).Select(x => x.SlotStart).ToListAsync(cancellationToken);

        List<DateTime> times = new List<DateTime>();

        foreach(var startTime in availabileTime)
        {
            var blocked = false;
            var slotCount = duration / 30;
            for(int i = 0; i < slotCount; i++)
            {
                var time = startTime.AddMinutes(i * 30);
                if(occupiedTimes.Contains(time))
                {
                    blocked = true;
                    break;
                }
            }
            if (!blocked)
            {
                times.Add(startTime);
            }
        }
        return new Schedule
        {
            Times = times
        };
    }
}