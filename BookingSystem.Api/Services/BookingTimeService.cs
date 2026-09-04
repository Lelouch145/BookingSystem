using BookingSystem.Api.Database;
using BookingSystem.Api.Models.ResponseModel;
using BookingSystem.Api.Models.SystemModels;
using Microsoft.EntityFrameworkCore;

namespace BookingSystem.Api.Services;

public class BookingTimeService
{
    private readonly AppDbContext _dbContext;

    public BookingTimeService(AppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public AvailabilityResponse AvailabilityCheck(DateTime userInput, int duration)
    {

        List<DateTime> times = new List<DateTime>();
        DateTime endTime;
        DateTime openingTime;
        DateTime closingTime;
        DateTime currentTime;

        if (duration != 60 && duration != 90)
        {
            return new AvailabilityResponse
            {
                ErrorMessage = Error.InvalidDuration
            };
        }
        if (userInput.DayOfWeek == DayOfWeek.Saturday
            || userInput.DayOfWeek == DayOfWeek.Sunday)
        {
            openingTime = userInput.Date.AddHours(12);
            closingTime = userInput.Date.AddDays(1);
            currentTime = openingTime;
        }
        else
        {
            openingTime = userInput.Date.AddHours(10);
            closingTime = userInput.Date.AddHours(22);
            currentTime = openingTime;
        }

        if (userInput.Date < DateTime.Today)
        {
            return new AvailabilityResponse
            {
                ErrorMessage = Error.BookingCannotBeInThePast
            };
        }
        while (currentTime < closingTime)
        {
            endTime = currentTime.AddMinutes(duration);
            if (endTime <= closingTime && currentTime > DateTime.Now)
            {
                times.Add(currentTime);
            }
            currentTime = currentTime.AddMinutes(30);
        }

        return new AvailabilityResponse
        {
            Times = times,
            ErrorMessage = Error.none
        };
    }

    public Error ValidateBookingTime(DateTime startTime, int duration)
    {
        var checkAvailability = AvailabilityCheck(startTime, duration);
        if (checkAvailability.ErrorMessage == Error.InvalidDuration ||
            checkAvailability.ErrorMessage == Error.BookingCannotBeInThePast)
        {


            return checkAvailability.ErrorMessage;

        }
        var times = checkAvailability.Times.Any(x => x == startTime);
        if (!times)
        {
            return Error.BookingTimeIsNotAvailable;

        }
        return Error.none;
    }
    
    public async Task<bool> CheckOverLapAsync(DateTime userInput, int duration, int courtId, int? bookingIdToIgnore, CancellationToken cancellationToken)
    {

        var whenBookingEnding = userInput.AddMinutes(duration);
        var hasOverlap = await _dbContext.Bookings
            .AnyAsync(x => x.CourtId == courtId &&
                x.Status == BookingStatus.Confirmed && x.Id != bookingIdToIgnore && x.EndTime > userInput 
                && x.StartTime < whenBookingEnding, cancellationToken);

        if(hasOverlap == true)
        {
            return true;
        }
        return false;
    }
}