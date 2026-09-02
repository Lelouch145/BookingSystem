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
    private readonly BookingTimeService _bookingTimeService;

    public BookingService(AppDbContext dbContext, BookingTimeService bookingTimeService)
    {
        _dbContext = dbContext;
        _bookingTimeService = bookingTimeService;

    }

    public async Task<BookingResult> CreateBooking(int courtId, DateTime startTime, int duration, string userId, CancellationToken cancellationToken)
    {

        var validationError = _bookingTimeService.ValidateBookingTime(startTime, duration);

        if(validationError != Error.none)
        {
            return new BookingResult
            {
                ErrorMessage = validationError
            };
        }
        var endTime = startTime.AddMinutes(duration);

        await using var transaction = await _dbContext.Database.
            BeginTransactionAsync(cancellationToken);
            
        var findCourt = await _dbContext.Courts.FirstOrDefaultAsync(x => x.Id == courtId, cancellationToken);

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

        var checkOverLap = await _bookingTimeService.CheckOverLapAsync(startTime, duration, courtId, null, cancellationToken);

        if (checkOverLap == true)
        {
            return new BookingResult
            {
                ErrorMessage = Error.BookingTimeIsOverlappingWithAnotherBooking
            };
        }
        var bookingSlots = CreateBookingSlots(courtId, startTime, duration, null);
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
            await _dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

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


    public async Task<IEnumerable<Booking>> GetUserBookings(string userId, CancellationToken cancellationToken)
    {
        var showBooking = await _dbContext.Bookings.Where(x => x.UserId == userId).
        OrderBy(x => x.StartTime).ToListAsync(cancellationToken);

        return showBooking;
    }

    public async Task<IEnumerable<Booking>> GetAllBookings(CancellationToken cancellationToken)
    {
        var allBookings = await _dbContext.Bookings.OrderBy(x => x.StartTime)
        .ToListAsync(cancellationToken);

        return allBookings;
    }

    public async Task<CancelBookingOrUpdate> CancelBooking(string userId, int bookingId, CancellationToken cancellationToken, bool isAdmin)
    {
        var findBooking = await _dbContext.Bookings.Include(x => x.BookingSlots)
            .FirstOrDefaultAsync(x => x.Id == bookingId, cancellationToken);

        if (findBooking == null)
        {
            return new CancelBookingOrUpdate
            {
                ErrorMessage = Error.BookingNotFound
            };
        }
        if(!isAdmin && userId != findBooking.UserId)
        {
            return new CancelBookingOrUpdate
            {
                ErrorMessage = Error.UserDoesNotOwnBooking
            };
        }
        if (findBooking.Status == BookingStatus.Cancelled)
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

        if (!isAdmin)
        {

            var startTime = findBooking.StartTime;
            var timeLimit = startTime.AddHours(-24);
            if (DateTime.Now >= timeLimit)
            {
                return new CancelBookingOrUpdate
                {
                    ErrorMessage = Error.CannotCancelBooking24HoursBeforeTheBooking
                };
            }

        }




        findBooking.Status = BookingStatus.Cancelled;
        try
        {
            _dbContext.BookingSlots.RemoveRange(findBooking.BookingSlots);
            await _dbContext.SaveChangesAsync(cancellationToken);
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

    public async Task<CancelBookingOrUpdate> RescheduleBooking(string userId, int bookingId, int duration, DateTime newStartTime, bool isAdmin, CancellationToken cancellationToken)
    {
        var findBooking = await _dbContext.Bookings.Include(x => x.BookingSlots)
            .FirstOrDefaultAsync(x => x.Id == bookingId, cancellationToken);

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

        var validationError = _bookingTimeService.ValidateBookingTime(newStartTime, duration);

        if(validationError != Error.none)
        {
            return new CancelBookingOrUpdate
            {
                ErrorMessage = validationError
            };
        }
        var checkOverLap = await _bookingTimeService.CheckOverLapAsync(newStartTime, duration, findBooking.CourtId, findBooking.Id, cancellationToken);

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



        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            _dbContext.BookingSlots.RemoveRange(findBooking.BookingSlots);
            await _dbContext.SaveChangesAsync(cancellationToken);

            var newBookingSlots = CreateBookingSlots(findBooking.CourtId, newStartTime, duration, bookingId);

            _dbContext.BookingSlots.AddRange(newBookingSlots);

            await _dbContext.SaveChangesAsync();
            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            await transaction.RollbackAsync();
            return new CancelBookingOrUpdate
            {
                ErrorMessage = Error.BookingWasModifiedByAnotherRequest
            };
        }
        catch (DbUpdateException ex)
        {
            await transaction.RollbackAsync();
            if (ex.InnerException is SqlException sqlException &&
                (sqlException.Number == 2601 || sqlException.Number == 2627))
            {
                return new CancelBookingOrUpdate
                {
                    ErrorMessage = Error.BookingTimeIsOverlappingWithAnotherBooking
                };
            }
            throw;
        }

        return new CancelBookingOrUpdate
        {
            Success = true
        };
    }

    private List<BookingSlot> CreateBookingSlots(int courtId, DateTime startTime, int duration, int? bookingId)
    {
        var slotCount = duration / 30;
        List<BookingSlot> bookingSlots = new List<BookingSlot>();

        for (int i = 0; i < slotCount; i++)
        {
            var slotStartTime = startTime.AddMinutes(i * 30);
            BookingSlot newBookingSlot = new BookingSlot
            {
                CourtId = courtId,
                SlotStart = slotStartTime
            };
            if (bookingId.HasValue)
            {
                newBookingSlot.BookingId = bookingId.Value;
            }
            bookingSlots.Add(newBookingSlot);
        }
        return bookingSlots;
    }
    

    

}