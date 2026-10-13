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
using Microsoft.Extensions.Caching.Distributed;
using System.Text.Json;
using StackExchange.Redis;

namespace BookingSystem.Api.Services;

/// <summary>
/// Handles booking-related business logic such as creating, cancelling,
/// rescheduling, retrieving bookings, and managing related cache invalidation
/// Uses Redis caching to improve response times, while SQL Server
/// remains the source of truth for booking data.
/// </summary>
public class BookingService
{
    private const string BookingsAll = "bookings:all";
    private const string BookingsUser = "bookings:user:";
    private const string Availability = "Availability";
    private const int TTL = 5;
    private readonly AppDbContext _dbContext;
    private readonly BookingTimeService _bookingTimeService;
    private readonly IDistributedCache _cache;
    private readonly ILogger<BookingService> _logger;

    public BookingService(AppDbContext dbContext, BookingTimeService bookingTimeService, IDistributedCache cache, ILogger<BookingService> logger)
    {
        _dbContext = dbContext;
        _bookingTimeService = bookingTimeService;
        _cache = cache;
        _logger = logger;

    }
    
/// <summary>
/// Creates a booking based on the information provided by the user.
/// Validates that the court exist, if active and that the requested booking does not overlap with another booking.
/// Uses a database transaction so that all database changes are rolled back if something fails.
/// If the booking succeeds, the transaction is committed and related booking and availability cache are invalidated.
/// </summary>
/// <param name="courtId">The ID of the selected court.</param>
/// <param name="startTime">The requested start time for the booking</param>
/// <param name="duration">The booking duration in minutesz/param>
/// <param name="userId">The ID of the user creating the booking</param>
/// <param name="cancellationToken">Token used to cancel the operation</param>
/// <returns>A result containing the created booking or an error.</returns>
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
        var bookingDate = DateOnly.FromDateTime(booking.StartTime);

        await RemoveCacheHelperAsync(BookingsAll, cancellationToken);
        await RemoveCacheHelperAsync($"{BookingsUser}{booking.UserId}", cancellationToken);
        await RemoveAvailabilityCacheAsync(courtId, bookingDate, cancellationToken);
        return result;

    }

/// <summary>
/// Returns all booking that belong to the specified user.
/// If cache booking data exists for the user, the cached data is returned.
/// If no cache exists, the method retrieves the users bookings from the database
/// and orders the by StartTime
/// The result is then stored in cache for faster access on future requests.
/// </summary>
/// <param name="userId">The ID of the user whom wishes to see its bookings</param>
/// <param name="cancellationToken">Token used to cancel the operation</param>
/// <returns>A collection of the users bookings</returns>
    public async Task<IEnumerable<BookingDto>> GetUserBookings(string userId, CancellationToken cancellationToken)
    {
        bool redisAvailable = true;
        string? cache = null;
        try
        {
            cache = await _cache.GetStringAsync($"{BookingsUser}{userId}",cancellationToken);

        }
        catch(RedisConnectionException ex)
        {
            _logger.LogWarning(ex, "Redis is down");
            redisAvailable = false;
        }
        catch(RedisTimeoutException ex)
        {
            _logger.LogWarning(ex, "Redis timedout operation taking to long");
            redisAvailable = false;
        }

        if(cache != null)
        {
            try
            {
                var json = JsonSerializer.Deserialize<List<BookingDto>>(cache);
                if(json != null)
                {
                    return json;
                }
            }
            catch(JsonException ex)
            {
                _logger.LogWarning(ex, "Failed to deserialize json for bookings");
            }
        }

        var showBooking = await _dbContext.Bookings.Where(x => x.UserId == userId).
        OrderBy(x => x.StartTime).ToListAsync(cancellationToken);
        var bookingsReturn = showBooking.Select(x => new BookingDto
        {
            Id = x.Id,
            CourtId = x.CourtId,
            UserId = x.UserId,
            StartTime = x.StartTime,
            EndTime = x.EndTime,
            Status = x.Status,
            CreatedAt = x.CreatedAt
        }).ToList();

        var bookingSerialize = JsonSerializer.Serialize(bookingsReturn);
        var options = new DistributedCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(TTL)
        };
        if (redisAvailable)
        {
            try
            {
                await _cache.SetStringAsync($"{BookingsUser}{userId}", bookingSerialize, options, cancellationToken);
            }
            catch(RedisConnectionException ex)
            {
                _logger.LogWarning(ex, "Redis is down");
            }
            catch(RedisTimeoutException ex)
            {
                _logger.LogWarning(ex, "Redis timedout operation taking to long");
            }
        }
        
        return bookingsReturn;
    }
/// <summary>
/// Retrieves all bookings in the system
/// This method is intended for administrators and is not accessible to regular users.
/// If cached booking data exists, it is returned directly.
/// Otherwise, bookings are retrieved from SQL Server and ordered by StartTime
/// The result is then stored in Redis cache for faster access on future requests.
/// </summary>
/// <param name="cancellationToken">Token used to cancel operation</param>
/// <returns>A collection of BookingDto objects containing booking infromation</returns>
    public async Task<IEnumerable<BookingDto>> GetAllBookings(CancellationToken cancellationToken)
    {
        string? cache = null;
        bool redisAvailable = true;

        try
        {
            cache = await _cache.GetStringAsync(BookingsAll);
        }
        catch(RedisConnectionException ex)
        {
            _logger.LogWarning(ex, "Redis is down");
            redisAvailable = false;
        }
        catch(RedisTimeoutException ex)
        {
            _logger.LogWarning(ex, "Redis timedout operation taking to long");
            redisAvailable = false;
        }
        if(cache != null)
        {
            try
            {
                var json = JsonSerializer.Deserialize<List<BookingDto>>(cache);
                if(json != null)
                {
                    return json;
                }   
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "Failed to deserialize json for all bookings");
            }
        }

        var allBookings = await _dbContext.Bookings.OrderBy(x => x.StartTime)
        .ToListAsync(cancellationToken);
        var bookingsReturn = allBookings.Select(x => new BookingDto
        {
            Id = x.Id,
            CourtId = x.CourtId,
            UserId = x.UserId,
            StartTime = x.StartTime,
            EndTime = x.EndTime,
            Status = x.Status,
            CreatedAt = x.CreatedAt   
        }).ToList();
        var serializedBookings = JsonSerializer.Serialize(bookingsReturn);

        var options = new DistributedCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(TTL)
        };
        if (redisAvailable)
        {
            try
            {
                await _cache.SetStringAsync(BookingsAll, serializedBookings, options);
            }
            catch(RedisConnectionException ex)
            {
                _logger.LogWarning(ex, "Redis is down");
            }
            catch(RedisTimeoutException ex)
            {
                _logger.LogWarning(ex, "Redis timedout operation taking to long");
            }
        }


        return bookingsReturn;
    }
/// <summary>
/// Cancels an existing booking for the specified user.
/// Validates that the booking exists and has not already been cancelled or completed.
/// Regular users must cancel at least 24 hours before the booking starts.
/// Administrators can cancel bookings without this time restriction.
/// If the cancellation succeedds, the related booking and availability cache entries are invalidated
/// </summary>
/// <param name="userId">The ID of the user requested.</param>
/// <param name="bookingId">The ID of the booking to cancel.</param>
/// <param name="cancellationToken">Token used to cancel operation.</param>
/// <param name="isAdmin">Indicates wheter the requesting user has administrator privleges</param>
/// <returns>A result indicating whether the cancellation succeeded or an error occured.</returns>
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

        var dateStartTime = DateOnly.FromDateTime(findBooking.StartTime);
        var durationTimeSpan = findBooking.EndTime - findBooking.StartTime;
        var duration = (int)durationTimeSpan.TotalMinutes;

        await RemoveCacheHelperAsync(BookingsAll, cancellationToken);
        await RemoveCacheHelperAsync($"{BookingsUser}{findBooking.UserId}", cancellationToken);
        await RemoveAvailabilityCacheAsync(findBooking.CourtId, dateStartTime, cancellationToken);
        return new CancelBookingOrUpdate
        {
            Success = true
        };

    }
/// <summary>
/// Reschedules and existing booking for the specified user.
/// Users can change the bookings start time and duration, but not the court.
/// Regular users can only reschedule their own bookings, while administrators can reschedule any booking.
/// Uses BookingTimeService to validate the requested time and duration.
/// Also checks that the new booking time does not overlap with existring bookings.
/// Uses a database transaction to ensure that all changes are committed together or roled back if an error occurs.
/// If successful, the related booking and availability cache entries are invalidated.
/// </summary>
/// <param name="userId">The ID of the user requesting to reschedule.</param>
/// <param name="bookingId">The ID of the booking to reschedule.</param>
/// <param name="duration">Duration of the new booking in minutes.</param>
/// <param name="newStartTime">Bookings new start time.</param>
/// <param name="isAdmin">Indicates whether the requesting user has administrator privileges.</param>
/// <param name="cancellationToken">Token used to cancel the operation.</param>
/// <returns>A result indicating whether the reschedule succeeded or an error occured</returns>
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
            var startTime = findBooking.StartTime;
            var timeLimit = startTime.AddHours(-24);
            if (DateTime.Now >= timeLimit)
            {
                return new CancelBookingOrUpdate
                {
                    ErrorMessage = Error.CannotRescheduleBooking24HoursBeforeTheBooking
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
        var oldBookingDurationTimeSpan = findBooking.EndTime - findBooking.StartTime;
        var dateOnlyFindBooking = DateOnly.FromDateTime(findBooking.StartTime);
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


        var durationOldTime = (int)oldBookingDurationTimeSpan.TotalMinutes;
        var dateOnlyNewStartTime = DateOnly.FromDateTime(newStartTime);

        await RemoveCacheHelperAsync(BookingsAll, cancellationToken);
        await RemoveCacheHelperAsync($"{BookingsUser}{findBooking.UserId}", cancellationToken);
        await RemoveAvailabilityCacheAsync(findBooking.CourtId, dateOnlyFindBooking, cancellationToken);
        if(dateOnlyFindBooking != dateOnlyNewStartTime)
        {
            await RemoveAvailabilityCacheAsync(findBooking.CourtId, dateOnlyNewStartTime, cancellationToken);
        }
        return new CancelBookingOrUpdate
        {
            Success = true
        };
    }
    /// <summary>
    /// Creates booking slots for a specified court, start time and duration.
    /// Each slot represents a 30-minute period of the booking.
    /// The number of slots depends on the booking duration (2 for 60, 3 for 90 minutes).
    /// Booking slots are used to prevent overlapping bookings.
    /// SQL Server enforces uniqueness through a unique index on CourtId and SlotsStart,
    /// ensuring that two bookings cannot occupy the same court at the same time
    /// </summary>
    /// <param name="courtId">The ID of the court where the slots will be created</param>
    /// <param name="startTime">The start time of the booking.</param>
    /// <param name="duration">The booking duration in minutes, determining how many slots are created./param>
    /// <param name="bookingId">The ID which the bookng associated with the slots.</param>
    /// <returns>A collection of the created booking slots</returns>

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
/// <summary>
/// Removes a specified cache entry using its cache key.
/// Handles Redis connection and timeout erros to prevent cache failures
/// fro affecting the main operation
/// </summary>
/// <param name="cacheKey">The keyof the cache entry to remove. </param>
/// <param name="cancellationToken">Token used to cancel operation</param>
/// <returns>A task representing the asynchronous operation.</returns>
    private async Task RemoveCacheHelperAsync(string cacheKey, CancellationToken cancellationToken)
    {
        try
        {
            await _cache.RemoveAsync(cacheKey, cancellationToken);
        }
        catch(RedisConnectionException ex)
        {
            _logger.LogWarning(ex, "Redis is down");
        }
        catch(RedisTimeoutException ex)
        {
            _logger.LogWarning(ex, "Redis timedout operation taking to long");
        }
    }
/// <summary>
/// Helper method to invalidate availability cache for a specified court and date.
/// Uses CacheKeys to generate the cache keys for both 60-minute and 90-minute bookings.
/// Removes the cache entries directly using IDistributedCache.
/// Handles Redis connection and timeout errors to prevent cache failures
/// from affecting the main operation
/// </summary>
/// <param name="courtId">The ID of the court whoes availability cache should be invalidated/param>
/// <param name="date">The date for which availability cache should be invalidated</param>
/// <param name="cancellationToken">Token used to cancel operation</param>
/// <returns>A task representing the asynchronous operation</returns>
    private async Task RemoveAvailabilityCacheAsync(int courtId, DateOnly date, CancellationToken cancellationToken)
    {
        try
        {
            await _cache.RemoveAsync(cacheKey.AvailabilityKey(courtId,date,60), cancellationToken);
            await _cache.RemoveAsync(cacheKey.AvailabilityKey(courtId,date,90), cancellationToken);
        }
        catch(RedisConnectionException ex)
        {
            _logger.LogWarning(ex, "Redis is down");
        }
        catch(RedisTimeoutException ex)
        {
            _logger.LogWarning(ex, "Redis timedout operation taking to long");
        }
    }
    

    

}