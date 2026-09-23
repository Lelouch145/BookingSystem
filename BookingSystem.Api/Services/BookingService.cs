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

public class BookingService
{
    private const string BookingsAll = "bookings:all";
    private const string BookingsUser = "bookings:user:";
    private const int TTL = 5;
    private readonly AppDbContext _dbContext;
    private readonly BookingTimeService _bookingTimeService;
    private readonly IDistributedCache _cache;
    private readonly ILogger _logger;

    public BookingService(AppDbContext dbContext, BookingTimeService bookingTimeService, IDistributedCache cache, ILogger logger)
    {
        _dbContext = dbContext;
        _bookingTimeService = bookingTimeService;
        _cache = cache;
        _logger = logger;

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

        await RemoveCacheHelperAsync(BookingsAll, cancellationToken);
        await RemoveCacheHelperAsync($"{BookingsUser}{booking.UserId}", cancellationToken);
        return result;

    }


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
        var serializedCourts = JsonSerializer.Serialize(bookingsReturn);

        var options = new DistributedCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(TTL)
        };
        if (redisAvailable)
        {
            try
            {
                await _cache.SetStringAsync(BookingsAll, serializedCourts, options);
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

        await RemoveCacheHelperAsync(BookingsAll, cancellationToken);
        await RemoveCacheHelperAsync($"{BookingsUser}{findBooking.UserId}", cancellationToken);
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

        await RemoveCacheHelperAsync(BookingsAll, cancellationToken);
        await RemoveCacheHelperAsync($"{BookingsUser}{findBooking.UserId}", cancellationToken);
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
    

    

}