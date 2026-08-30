using System.IO.Compression;
using System.Threading.Tasks;
using BookingSystem.Api.Database;
using BookingSystem.Api.Models.ResponseModel;
using BookingSystem.Api.Models.SystemModels;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualBasic;
using Microsoft.AspNetCore.Identity;
using System.ComponentModel;
using Microsoft.Data.SqlClient;
using System.Data;

namespace BookingSystem.Api.Services;

public class BookingService
{
    private readonly AppDbContext _dbContext;

    public BookingService(AppDbContext dbContext)
    {
        _dbContext = dbContext;

    }

    public async Task<BookingResult> CreateBooking(int courtId, DateTime startTime, int duration, string userId)
    {

        var checkAvailability = AvailabilityCheck(startTime, duration);
        if (checkAvailability.ErrorMessage == Error.InvalidDuration ||
            checkAvailability.ErrorMessage == Error.BookingCannotBeInThePast)
        {
            return new BookingResult
            {
                ErrorMessage = checkAvailability.ErrorMessage
            };
        }
        var times = checkAvailability.Times.Any(x => x == startTime);
        if (!times)
        {
            return new BookingResult
            {
                ErrorMessage = Error.BookingTimeIsNotAvailable
            };
        }
        var endTime = startTime.AddMinutes(duration);
        List<BookingSlot> bookingSlots = new List<BookingSlot>();
        var slotCount = duration / 30;

        await using var transaction = await _dbContext.Database.
            BeginTransactionAsync();
            
        var findCourt = await _dbContext.Courts.FirstOrDefaultAsync(x => x.Id == courtId);

        if (findCourt == null)
        {
            return new BookingResult
            {
                ErrorMessage = Error.CouldNotFindTheCourtInDataBase
            };
        }
        if (!findCourt.IsActive)
        {
            return new BookingResult
            {
                ErrorMessage = Error.CourtIsNotAvailable
            };
        }

        var checkOverLap = CheckOverLap(startTime, duration, courtId, null);

        if (checkOverLap == true)
        {
            return new BookingResult
            {
                ErrorMessage = Error.BookingTimeIsOverlappingWithAnotherBooking
            };
        }

        for(int i = 0; i < slotCount; i++)
        {
            var slotStartTime = startTime.AddMinutes(i * 30);
            BookingSlot bookingSlot = new BookingSlot
            {
                CourtId = findCourt.Id,
                SlotStart = slotStartTime
            };
            bookingSlots.Add(bookingSlot);
        }
        Booking booking = new Booking
        {
            CourtId = findCourt.Id,
            StartTime = startTime,
            UserId = userId,
            EndTime = endTime,
            Status = BookingStatus.Confirmed,
            CreatedAt = DateTime.UtcNow,
            BookingSlots = bookingSlots

        };
        _dbContext.Bookings.Add(booking);
        try
        {
            await _dbContext.SaveChangesAsync();
            await transaction.CommitAsync();

        }
        catch(DbUpdateException ex)
        {
            await transaction.RollbackAsync();
            if (ex.InnerException is SqlException sqlException && (sqlException.Number == 2601 || sqlException.Number == 2627))
            {
                return new BookingResult
                {
                    ErrorMessage = Error.BookingTimeIsOverlappingWithAnotherBooking
                };
            }

            throw;
        }

        BookingClient bookingClient = new BookingClient
        {
            Id = booking.Id,
            CourtId = booking.CourtId,
            StartTime = booking.StartTime,
            EndTime = booking.EndTime,
            Duration = duration,
            Status = booking.Status,
            CreatedAt = booking.CreatedAt
        };
        BookingResult result = new BookingResult
        {
            Success = true,
            ClientBooking = bookingClient,
            ErrorMessage = Error.none
        };
        return result;

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

    public bool CheckOverLap(DateTime userInput, int duration, int courtId, int? bookingIdToIgnore)
    {

        var whenBookingEnding = userInput.AddMinutes(duration);
        var booking = _dbContext.Bookings
            .Where(x => x.CourtId == courtId &&
                x.Status == BookingStatus.Confirmed && x.Id != bookingIdToIgnore);


        foreach (var time in booking)
        {
            if (userInput < time.EndTime && whenBookingEnding > time.StartTime)
            {
                return true;
            }
        }
        return false;
    }

    public async Task<IEnumerable<Booking>> GetUserBookings(string userId)
    {
        var showBooking = await _dbContext.Bookings.Where(x => x.UserId == userId).
        OrderBy(x => x.StartTime).ToListAsync();

        return showBooking;
    }

    public async Task<IEnumerable<Booking>> GetAllBookings()
    {
        var allBookings = await _dbContext.Bookings.OrderBy(x => x.StartTime)
        .ToListAsync();

        return allBookings;
    }

    public async Task<CancelBookingOrUpdate> CancelUserBooking(string userId, int bookingId)
    {
        var findBooking = await _dbContext.Bookings.FirstOrDefaultAsync(x => x.UserId == userId
        && x.Id == bookingId);

        if (findBooking == null)
        {
            return new CancelBookingOrUpdate
            {
                ErrorMessage = Error.BookingNotFound
            };
        }
        else if (findBooking.Status == BookingStatus.Cancelled)
        {
            return new CancelBookingOrUpdate
            {
                ErrorMessage = Error.BookingIsAlreadyCancelled
            };
        }
        else if (findBooking.Status == BookingStatus.Completed)
        {
            return new CancelBookingOrUpdate
            {
                ErrorMessage = Error.BookingIsCompletedAndCannotBeCancelled
            };
        }
        var startTime = findBooking.StartTime;
        var timeLimit = startTime.AddHours(-24);
        if (DateTime.Now >= timeLimit)
        {
            return new CancelBookingOrUpdate
            {
                ErrorMessage = Error.CannotCancelBooking24HoursBeforeTheBooking
            };
        }
        findBooking.Status = BookingStatus.Cancelled;
        try
        {
            await _dbContext.SaveChangesAsync();
        }
        catch(DbUpdateConcurrencyException)
        {
            return new CancelBookingOrUpdate
            {
                ErrorMessage = Error.BookingWasModifiedByAnotherRequest
            };
        }

        return new CancelBookingOrUpdate
        {
            Success = true
        };

    }

    public async Task<CancelBookingOrUpdate> CancelAdminBooking(int bookingId)
    {
        var findBooking = await _dbContext.Bookings.FirstOrDefaultAsync(x => x.Id == bookingId);

        if (findBooking == null)
        {
            return new CancelBookingOrUpdate
            {
                ErrorMessage = Error.BookingNotFound
            };
        }
        else if (findBooking.Status == BookingStatus.Cancelled)
        {
            return new CancelBookingOrUpdate
            {
                ErrorMessage = Error.BookingIsAlreadyCancelled
            };
        }
        else if (findBooking.Status == BookingStatus.Completed)
        {
            return new CancelBookingOrUpdate
            {
                ErrorMessage = Error.BookingIsCompletedAndCannotBeCancelled
            };
        }
        findBooking.Status = BookingStatus.Cancelled;
        await _dbContext.SaveChangesAsync();

        return new CancelBookingOrUpdate
        {
            Success = true
        };
    }

    public async Task<CancelBookingOrUpdate> RescheduleBooking(string userId, int bookingId, int duration, DateTime newStartTime, bool isAdmin)
    {
        var findBooking = await _dbContext.Bookings.FirstOrDefaultAsync(x => x.Id == bookingId);

        if (findBooking == null)
        {
            return new CancelBookingOrUpdate
            {
                ErrorMessage = Error.BookingNotFound
            };
        }
        if (!isAdmin)
        {
            if (findBooking.UserId != userId)
            {
                return new CancelBookingOrUpdate
                {
                    ErrorMessage = Error.UserDoesNotOwnBooking
                }; 
            }
        }
        if (findBooking.Status == BookingStatus.Cancelled)
        {
            return new CancelBookingOrUpdate
            {
                ErrorMessage = Error.BookingIsCancelledAndCannotBeChanged
            };
        }
        else if (findBooking.Status == BookingStatus.Completed)
        {
            return new CancelBookingOrUpdate
            {
                ErrorMessage = Error.BookingIsCompletedAndCannotBeChanged
            };
        }

        var checkAvailability = AvailabilityCheck(newStartTime, duration);
        if (checkAvailability.ErrorMessage == Error.InvalidDuration ||
            checkAvailability.ErrorMessage == Error.BookingCannotBeInThePast)
        {
            return new CancelBookingOrUpdate
            {
                ErrorMessage = checkAvailability.ErrorMessage
            };
        }


        var times = checkAvailability.Times.Any(x => x == newStartTime);
        if (!times)
        {
            return new CancelBookingOrUpdate
            {
                ErrorMessage = Error.BookingTimeIsNotAvailable
            };
        }
        var checkOverLap = CheckOverLap(newStartTime, duration, findBooking.CourtId, findBooking.Id);

        if (checkOverLap == true)
        {
            return new CancelBookingOrUpdate
            {
                ErrorMessage = Error.BookingTimeIsOverlappingWithAnotherBooking
            };
        }
        var endTime = newStartTime.AddMinutes(duration);

        findBooking.StartTime = newStartTime;
        findBooking.EndTime = endTime;
        try
        {
            await _dbContext.SaveChangesAsync();
        }
        catch(DbUpdateConcurrencyException)
        {
            return new CancelBookingOrUpdate
            {
                ErrorMessage = Error.BookingWasModifiedByAnotherRequest
            };
        }

        return new CancelBookingOrUpdate
        {
            Success = true
        };
    }
    

}