using System.Globalization;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Text.Json;
using BookingSystem.Api.Database;
using BookingSystem.Api.Models.ResponseModel;
using BookingSystem.Api.Models.SystemModels;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ValueGeneration.Internal;
using StackExchange.Redis;

namespace BookingSystem.Api.Services.Caching;

public class BookingHoldService
{
    private readonly BookingTimeService _bookingTimeService;
    private readonly IConnectionMultiplexer _connectionMultiplexer;
    private readonly AppDbContext _dbContext;
    private readonly ILogger<BookingHoldService> _logger;

    public BookingHoldService(BookingTimeService bookingTimeService, IConnectionMultiplexer connectionMultiplexer, AppDbContext dbContext, ILogger<BookingHoldService> logger)
    {
        _bookingTimeService = bookingTimeService;
        _connectionMultiplexer = connectionMultiplexer;
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task<CreateHoldResponse> CreateHoldAsync(DateOnly date, TimeOnly bookingTime, int duration, int courtId, string userId, CancellationToken cancellationToken)
    {
        var courtExists = await _dbContext.Courts.AnyAsync(x => x.Id == courtId && x.IsActive, cancellationToken);
        if(!courtExists)
        {
            return new CreateHoldResponse
            {
                ErrorMessage = Error.CouldNotFindTheCourtInDataBase
            };
        }
        var dateTime = date.ToDateTime(bookingTime);
        var validateBooking = _bookingTimeService.ValidateBookingTime(dateTime, duration);

        if(validateBooking != Error.none)
        {
            return new CreateHoldResponse
            {
                ErrorMessage = validateBooking
            };
        }
        var overlapCheck = await _bookingTimeService.CheckOverLapAsync(dateTime, duration, courtId, null, cancellationToken);
        
        if(overlapCheck)
        {
            return new CreateHoldResponse
            {
                ErrorMessage = Error.BookingTimeIsOverlappingWithAnotherBooking
            };
        }
        var hold = new CacheHoldModel
        {
            CourtId = courtId,
            UserId = userId,
            BookingDate = date,
            BookingTime = bookingTime,
            Duration = duration
        };
        var slotCount = duration / 30;
        List<TimeOnly> bookingSlots = new List<TimeOnly>();

        for(int i = 0; i < slotCount; i++)
        {
            var startTime = bookingTime.AddMinutes(i * 30);
            bookingSlots.Add(startTime);
        }
        List<string> redisKeys = new List<string>()
        {
            GetUserHoldKey(userId),
            GetHoldKey(hold.HoldId)
        };
        foreach(var booking in bookingSlots)
        {
            var key = GetSlotHoldKey(courtId, date, booking);
            redisKeys.Add(key);
        }
        RedisKey[] keys = redisKeys.Select(x => (RedisKey)x).ToArray();

        RedisValue[] values =
        {
            hold.HoldId.ToString(),
            JsonSerializer.Serialize(hold)
        };
        return await CreateHoldInRedisAsync(keys, values, hold.HoldId);

    }
    private const string CreateHoldScript = """
    -- Check if any key already exists
    for i = 1, #KEYS do
        if redis.call('EXISTS', KEYS[i]) == 1 then
            return {0, 0}
        end
    end

    -- Save user's HoldId
    redis.call('SET', KEYS[1], ARGV[1], 'EX', 300)

    -- Save reservation information
    redis.call('SET', KEYS[2], ARGV[2], 'EX', 300)

    -- Reserve booking slots
    for i = 3, #KEYS do
        redis.call('SET', KEYS[i], ARGV[1], 'EX', 300)
    end

    return {1, redis.call('EXPIRETIME', KEYS[2])}
    """;
    private string GetSlotHoldKey(int courtId, DateOnly date, TimeOnly slotStart)
    {
        return $"hold:slot:{courtId}:{date:yyyy-MM-dd}:{slotStart:HH:mm}";

    }
    private string GetUserHoldKey(string userId)
    {
        return $"hold:user:{userId}";
    }
    private string GetHoldKey(Guid holdId)
    {
        return $"hold:{holdId}";
    }

    private async Task<CreateHoldResponse> CreateHoldInRedisAsync(RedisKey[] keys, RedisValue[] values, Guid holdId)
    {
        try
        {
            var database = _connectionMultiplexer.GetDatabase();
            var generateScriptForKey = await database.ScriptEvaluateAsync(CreateHoldScript, keys, values);
            RedisResult[]? results = (RedisResult[]?)generateScriptForKey;
            if(results == null || results.Length != 2)
            {
                return new CreateHoldResponse
                {
                    ErrorMessage = Error.HoldServiceUnavailable
                };
            }
            var status = (int)results[0];
            var expiration = (long)results[1];
            if(status == 0)
            {
                return new CreateHoldResponse
                {
                    ErrorMessage = Error.TheBookingIsAlreadyOnHold
                };
            }

            if(status == 1 && expiration > 0)
            {
                var expiresAt = DateTimeOffset.FromUnixTimeSeconds(expiration);
                return new CreateHoldResponse
                {
                    HoldId = holdId,
                    ExpiresAt = expiresAt
                };
            }
            else
            {
                return new CreateHoldResponse
                {
                    ErrorMessage = Error.HoldServiceUnavailable
                };
            }
        }
        catch(RedisConnectionException ex)
        {
            _logger.LogError(ex, "Redis connection failed.");
            return new CreateHoldResponse
            {
                ErrorMessage = Error.HoldServiceUnavailable
            };
        }
        catch(RedisTimeoutException ex)
        {
            _logger.LogError(ex, "Redis operation timed out.");
            return new CreateHoldResponse
            {
                ErrorMessage = Error.HoldServiceUnavailable
            };
        }
        catch(RedisServerException ex)
        {
            _logger.LogError(ex, "Redis server error.");
            return new CreateHoldResponse
            {
                ErrorMessage = Error.HoldServiceUnavailable
            };
        }
        catch(RedisCommandException ex)
        {
            _logger.LogError(ex, "Invalid Redis command.");
            throw;
        }
            
    }
}