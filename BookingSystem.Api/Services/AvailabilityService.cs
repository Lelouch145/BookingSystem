using System.ComponentModel;
using System.IO.Compression;
using System.Text.Json;
using BookingSystem.Api.Database;
using BookingSystem.Api.Models.SystemModels;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using StackExchange.Redis;

namespace BookingSystem.Api.Services;

public class AvailabilityService
{
    private const int TTL = 5;
    private readonly BookingTimeService _bookingTimeService;
    private readonly AppDbContext _dbContext;
    private readonly IDistributedCache _cache;
    private readonly ILogger<AvailabilityService> _logger;

    public AvailabilityService(BookingTimeService bookingTimeService, AppDbContext dbContext, IDistributedCache cache, ILogger<AvailabilityService> logger)
    {
        _bookingTimeService = bookingTimeService;
        _dbContext = dbContext;
        _cache = cache;
        _logger = logger;
    }
    public async Task<Schedule> AvailabilitySchedule(DateOnly bookingDate, int duration, int courtId, CancellationToken cancellationToken)
    {
        string cacheSignature = cacheKey.AvailabilityKey(courtId, bookingDate, duration);
        string? cache = null;
        bool redisAvailable = true;
        try
        {
            cache = await _cache.GetStringAsync(cacheSignature, cancellationToken);
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
                var serializedSchedule = JsonSerializer.Deserialize<Schedule>(cache);

                if(serializedSchedule != null)
                {
                    return serializedSchedule;
                }  
            }
            catch(JsonException ex)
            {
                _logger.LogWarning(ex, "Failed to deserialize json for schedule");
            }

        }
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
        var schedule = new Schedule
        {
            Times = times
        };
        var scheduleSerialize = JsonSerializer.Serialize(schedule);
        var options = new DistributedCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(TTL)
        };
        if (redisAvailable)
        {
            try
            {
                await _cache.SetStringAsync(cacheSignature, scheduleSerialize, options, cancellationToken);
            }
            catch(RedisConnectionException ex)
            {
                _logger.LogWarning(ex, "Redis is down");
            }
            catch(RedisTimeoutException ex)
            {
                _logger.LogWarning(ex, "Redis timedout operatin taking to long");
            }
        }
        return schedule;
    }
}