using System.Globalization;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Text.Json;
using BookingSystem.Api.Database;
using BookingSystem.Api.Models.ResponseModel;
using BookingSystem.Api.Models.SystemModels;
using BookingSystem.Api.Services.interfaces;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ValueGeneration.Internal;
using StackExchange.Redis;

namespace BookingSystem.Api.Services.Caching;

public class BookingHoldService : IBookingHoldService
{
    private readonly BookingTimeService _bookingTimeService;
    private readonly IConnectionMultiplexer _connectionMultiplexer;
    private readonly AppDbContext _dbContext;
    private readonly ILogger<BookingHoldService> _logger;
    private static readonly string CreateHoldScript = File.ReadAllText
    (
        Path.Combine
        (
            AppContext.BaseDirectory,
            "Services",
            "Caching",
            "Scripts",
            "CreateHold.lua"
        )
    );
    private static readonly string SwitchHoldScript = File.ReadAllText
    (
        Path.Combine
        (
            AppContext.BaseDirectory,
            "Services",
            "Caching",
            "Scripts",
            "SwitchHold.lua"
        )
    );
    private static readonly string ReleaseHoldScript = File.ReadAllText
    (
        Path.Combine
        (
            AppContext.BaseDirectory,
            "Services",
            "Caching",
            "Scripts",
            "ReleaseHold.lua"
        )
    );
    private static readonly string GetValidateHoldScript = File.ReadAllText
    (
        Path.Combine
        (
            AppContext.BaseDirectory,
            "Services",
            "Caching",
            "Scripts",
            "GetValidateHoldScript.lua"
        )
    );
    private static readonly string BeginFinalizationScript = File.ReadAllText
    (
        Path.Combine
        (
            AppContext.BaseDirectory,
            "Services",
            "Caching",
            "Scripts",
            "BeginFinalization.lua"
        )
    );
    private static readonly string CompleteFinalizationScript = File.ReadAllText
    (
        Path.Combine
        (
            AppContext.BaseDirectory,
            "Services",
            "Caching",
            "Scripts",
            "CompleteFinalization.lua"
        )
    );
    private static readonly string AbortFinalizationScript = File.ReadAllText
    (
        Path.Combine
        (
            AppContext.BaseDirectory,
            "Services",
            "Caching",
            "Scripts",
            "AbortFinalization.lua"
        )
    );

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
        List<string> redisKeys = new List<string>()
        {
            GetUserHoldIdKey(userId),
            GetHoldDataKey(hold.HoldId)
        };

        for(int i = 0; i < slotCount; i++)
        {
            var startTime = bookingTime.AddMinutes(i * 30);
            var key = GetSlotHoldIdKey(courtId, date, startTime);
            redisKeys.Add(key);
        }
        redisKeys.Add(GetFinalizingKey(hold.HoldId));
        RedisKey[] keys = redisKeys.Select(x => (RedisKey)x).ToArray();

        RedisValue[] values =
        {
            hold.HoldId.ToString(),
            JsonSerializer.Serialize(hold)
        };
        return await ExecuteHoldScriptAsync(keys, values, hold.HoldId, CreateHoldScript);

    }
    private async Task<CreateHoldResponse> ExecuteHoldScriptAsync(RedisKey[] keys, RedisValue[] values, Guid holdId, string redisScript)
    {
        try
        {
            var database = _connectionMultiplexer.GetDatabase();
            var generateScriptForKey = await database.ScriptEvaluateAsync(redisScript, keys, values);
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



            if(status == 1 && expiration > 0)
            {

                var expiresAt = DateTimeOffset.FromUnixTimeSeconds(expiration);
                return new CreateHoldResponse
                {
                    HoldId = holdId,
                    ExpiresAt = expiresAt
                };
            }

            var error = status switch
            {
                2 => Error.UserDosentHaveAnActiveHold,
                3 => Error.HoldDataHasChanged,
                4 => Error.BookingSlotAlreadyOnHold,
                5 => Error.HoldExpirationTimeUnavailable,
                6 => Error.OldSlotsNoLongerBelongToHold,
                7 => Error.UserAlreadyHasAnActiveHold,
                8 => Error.HoldServiceUnavailable,
                9 => Error.HoldFinalizationInProgress,
                _ => Error.HoldServiceUnavailable
            };

            return new CreateHoldResponse
            {
                ErrorMessage = error
            };
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
            _logger.LogError(ex, "Redis server error occurred");
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

    public async Task<CreateHoldResponse> SwitchHoldAsync(DateOnly newDate, TimeOnly newBookingTime, int duration, int courtId, string userId, CancellationToken cancellationToken)
    {
        var getHoldId = await GetRedisValueAsync(GetUserHoldIdKey(userId));
        if(getHoldId.ErrorMessage != Error.none)
        {
            return new CreateHoldResponse
            {
                ErrorMessage = getHoldId.ErrorMessage
            };
        }

        if(!Guid.TryParse(getHoldId.ValueRedis.ToString(), out var holdId))
        {
            return new CreateHoldResponse
            {
                ErrorMessage = Error.UserDosentHaveAnActiveHold
            };
        }
        var getHold = await GetRedisValueAsync(GetHoldDataKey(holdId));
        if (getHold.ErrorMessage != Error.none)
        {
            return new CreateHoldResponse
            {
                ErrorMessage = getHold.ErrorMessage
            };
        }
        if (getHold.ValueRedis.IsNull)
        {
            return new CreateHoldResponse
            {
                ErrorMessage = Error.DontHaveAHold
            };
        }
        CacheHoldModel? hold = null;
        try
        {
            hold = JsonSerializer.Deserialize<CacheHoldModel>(getHold.ValueRedis.ToString());
        }
        catch(JsonException ex)
        {
            _logger.LogError(ex, "Failed to deserialize json for bookings");
        }
        

        if(hold == null)
        {
            return new CreateHoldResponse
            {
                ErrorMessage = Error.CouldNotDeserializeJson
            };
        }
        if(hold.UserId != userId || hold.HoldId != holdId)
        {
            return new CreateHoldResponse
            {
                ErrorMessage = Error.HoldBookingDosentBelongToTheUser
            };
        }
        var courtExists = await _dbContext.Courts.AnyAsync(x => x.Id == courtId && x.IsActive, cancellationToken);
        if (!courtExists)
        {
            return new CreateHoldResponse
            {
                ErrorMessage = Error.CouldNotFindTheCourtInDataBase
            };
        }
        var dateTime = newDate.ToDateTime(newBookingTime);
        var validateBookingTime = _bookingTimeService.ValidateBookingTime(dateTime, duration);
        if(validateBookingTime != Error.none)
        {
            return new CreateHoldResponse
            {
                ErrorMessage = validateBookingTime
            };
        }
        var overlapCheck = await _bookingTimeService.CheckOverLapAsync(dateTime, duration, courtId, null, cancellationToken);
        if(overlapCheck == true)
        {
            return new CreateHoldResponse
            {
                ErrorMessage = Error.BookingTimeIsOverlappingWithAnotherBooking
            };
        }
        var newHold = new CacheHoldModel
        {
            HoldId = hold.HoldId,
            CourtId = courtId,
            UserId = userId,
            BookingDate = newDate,
            BookingTime = newBookingTime,
            Duration = duration
        };

        var newSlotCount = duration / 30;

        List<string> redisKey = new List<string>
        {
            GetUserHoldIdKey(userId),
            GetHoldDataKey(hold.HoldId)
        };
        for(var i = 0; i < newSlotCount; i++)
        {
            var time = newBookingTime.AddMinutes(i * 30);
            var newSlotKey = GetSlotHoldIdKey(courtId,newDate, time);
            redisKey.Add(newSlotKey);
            
        }

        var oldSlotCount = hold.Duration / 30;
        List<string> bookingSlotsOld = new List<string>();
        for(var i = 0; i < oldSlotCount; i++)
        {
            var oldTime = hold.BookingTime.AddMinutes(i * 30);
            var oldKey = GetSlotHoldIdKey(hold.CourtId, hold.BookingDate, oldTime);
            bookingSlotsOld.Add(oldKey);
        }
        redisKey.AddRange(bookingSlotsOld);
        redisKey.Add(GetFinalizingKey(holdId));
        RedisKey[] keys = redisKey.Select(x => (RedisKey)x).ToArray();

        RedisValue[] values =
        {
            newHold.HoldId.ToString(),
            JsonSerializer.Serialize(newHold),
            newSlotCount,
            getHold.ValueRedis.ToString()
        };

        return await ExecuteHoldScriptAsync(keys, values, newHold.HoldId, SwitchHoldScript);
    }

    public async Task<BoolResponseModel> ReleaseHoldAsync(string userId)
    {
        var userHoldId = await GetRedisValueAsync(GetUserHoldIdKey(userId));

        if (userHoldId.ErrorMessage != Error.none)
        {
           return new BoolResponseModel
           {
               ErrorMessage = userHoldId.ErrorMessage
           }; 
        }
        if(!Guid.TryParse(userHoldId.ValueRedis, out var holdId))
        {
            return new BoolResponseModel
            {
                ErrorMessage = Error.UserDosentHaveAnActiveHold
            };
        }

        var userHold = await GetRedisValueAsync(GetHoldDataKey(holdId));

        if(userHold.ErrorMessage != Error.none)
        {
            return new BoolResponseModel
            {
                ErrorMessage = userHold.ErrorMessage
            };
        }

        if (userHold.ValueRedis.IsNull)
        {
            return new BoolResponseModel
            {
                ErrorMessage = Error.DontHaveAHold
            };
        }

        CacheHoldModel? hold = null;
        try
        {
            hold = JsonSerializer.Deserialize<CacheHoldModel>(userHold.ValueRedis.ToString());
        }
        catch(JsonException ex)
        {
            _logger.LogError(ex, "Failed to deserialize json getHold");
        }
        if(hold == null)
        {
            return new BoolResponseModel
            {
                ErrorMessage = Error.CouldNotDeserializeJson
            };
        }

        if(hold.UserId != userId || hold.HoldId != holdId)
        {
            return new BoolResponseModel
            {
                ErrorMessage = Error.HoldBookingDosentBelongToTheUser
            };
        }

        var slotCount = hold.Duration/30;
        List<string> redisKeys = new List<string>
        {
            GetUserHoldIdKey(userId),
            GetHoldDataKey(hold.HoldId) 
        };

        for(int i = 0; i < slotCount; i++)
        {
            var bookingTime = hold.BookingTime.AddMinutes(i * 30);
            var slots = GetSlotHoldIdKey(hold.CourtId, hold.BookingDate, bookingTime);
            redisKeys.Add(slots);
        }
        redisKeys.Add(GetFinalizingKey(holdId));
        RedisKey[] keys = redisKeys.Select(x => (RedisKey)x).ToArray();

        RedisValue[] values =
        {
            hold.HoldId.ToString(),
            userHold.ValueRedis.ToString()
        };

        return await ExecuteBoolResponseModelScripts(keys, values, ReleaseHoldScript);
    }

    private async Task<BoolResponseModel> ExecuteBoolResponseModelScripts(RedisKey[] keys, RedisValue[] values, string script)
    {
        try
        {
        
            var database = _connectionMultiplexer.GetDatabase();
            var generateScriptRelease = await database.ScriptEvaluateAsync(script, keys, values);
            var status = (int)generateScriptRelease;

            if(status == 1)
            {
                return new BoolResponseModel
                {
                    Success = true
                };
            }

            var error = status switch
            {
                2 => Error.UserDosentHaveAnActiveHold,
                3 => Error.HoldDataHasChanged,
                6 => Error.OldSlotsNoLongerBelongToHold,
                9 => Error.HoldFinalizationInProgress,
                10 => Error.FinalizationLockMismatch,
                11 => Error.CouldNotDeserializeJson,
                _ => Error.HoldServiceUnavailable
            };

            return new BoolResponseModel
            {
                ErrorMessage = error
            };
        }

        
        catch(RedisConnectionException ex)
        {
            _logger.LogError(ex, "Redis connection failed.");
            return new BoolResponseModel
            {
                ErrorMessage = Error.HoldServiceUnavailable
            };
        }
        catch(RedisTimeoutException ex)
        {
            _logger.LogError(ex, "Redis operation timed out.");
            return new BoolResponseModel
            {
                ErrorMessage = Error.HoldServiceUnavailable
            };
        }
        catch(RedisServerException ex)
        {
            _logger.LogError(ex, "Redis server error occurred");
            return new BoolResponseModel
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


    private async Task<RedisValueReturn> GetRedisValueAsync(RedisKey redisKey)
    {
        try
        {
            var database = _connectionMultiplexer.GetDatabase();
            var getHoldId = await database.StringGetAsync(redisKey); 
            return new RedisValueReturn
            {
                ValueRedis = getHoldId
            };
        }
        catch(RedisConnectionException ex)
        {
            _logger.LogError(ex, "Redis connection failed.");
            return new RedisValueReturn
            {
                ErrorMessage = Error.HoldServiceUnavailable
            };
        }
        catch(RedisTimeoutException ex)
        {
            _logger.LogError(ex, "Redis operation timed out.");
            return new RedisValueReturn
            {
                ErrorMessage = Error.HoldServiceUnavailable
            };
        }
        catch(RedisServerException ex)
        {
            _logger.LogError(ex, "Redis server error.");
            return new RedisValueReturn
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

    public async Task<CacheHoldResponse> GetValidateHoldAsync(string userId)
    {
        var userHoldId = await GetRedisValueAsync(GetUserHoldIdKey(userId));
        if(userHoldId.ErrorMessage != Error.none)
        {
            return new CacheHoldResponse
            {
                ErrorMessage = userHoldId.ErrorMessage
            };
        }
        
        if(!Guid.TryParse(userHoldId.ValueRedis, out var holdId))
        {
            return new CacheHoldResponse
            {
                ErrorMessage = Error.UserDosentHaveAnActiveHold
            };
        }
        var getHoldJson = await GetRedisValueAsync(GetHoldDataKey(holdId));
        if(getHoldJson.ErrorMessage != Error.none)
        {
            return new CacheHoldResponse
            {
                ErrorMessage = getHoldJson.ErrorMessage
            };
        }
        else if (getHoldJson.ValueRedis.IsNull)
        {
            return new CacheHoldResponse
            {
                ErrorMessage = Error.DontHaveAHold
            };
        }
        CacheHoldModel? getHold = null;
        try
        {
            getHold = JsonSerializer.Deserialize<CacheHoldModel>(getHoldJson.ValueRedis.ToString());
        }
        catch(JsonException ex)
        {
            _logger.LogError(ex, "Failed to deserialize json for bookings");
        }
        if(getHold == null)
        {
            return new CacheHoldResponse
            {
                ErrorMessage = Error.DontHaveAHold
            };
        }
        
        if(userId != getHold.UserId || holdId != getHold.HoldId)
        {
            return new CacheHoldResponse
            {
                ErrorMessage = Error.HoldBookingDosentBelongToTheUser
            };
        }
        var dateTime = getHold.BookingDate.ToDateTime(getHold.BookingTime);
        var validateBooking = _bookingTimeService.ValidateBookingTime(dateTime, getHold.Duration);

        if(validateBooking != Error.none)
        {
            return new CacheHoldResponse
            {
                ErrorMessage = validateBooking
            };
        }
        
        List<string> rediskeys = new List<string>
        {
            GetUserHoldIdKey(userId),
            GetHoldDataKey(holdId)
        };
        var slotCount = getHold.Duration / 30;
        for(int i = 0; i < slotCount; i++)
        {
            var startTime = getHold.BookingTime.AddMinutes(i * 30);
            var slotKey = GetSlotHoldIdKey(getHold.CourtId, getHold.BookingDate, startTime);
            rediskeys.Add(slotKey);
        }
        
        RedisKey[] keys = rediskeys.Select(x => (RedisKey)x).ToArray();

        RedisValue[] values =
        {
            getHold.HoldId.ToString(),
            getHoldJson.ValueRedis.ToString()
        };

        return await ExecuteValidateHoldScriptAsync(keys, values, getHold);
        
    }

    private async Task<CacheHoldResponse> ExecuteValidateHoldScriptAsync(RedisKey[] keys, RedisValue[] values, CacheHoldModel hold)
    {
        try
        {
        
            var database = _connectionMultiplexer.GetDatabase();
            var generateScriptRelease = await database.ScriptEvaluateAsync(GetValidateHoldScript, keys, values);
            var status = (int)generateScriptRelease;

            if(status == 1)
            {
                return new CacheHoldResponse
                {
                    CacheHold = hold
                };
            }

            var error = status switch
            {
                2 => Error.UserDosentHaveAnActiveHold,
                3 => Error.HoldDataHasChanged,
                6 => Error.OldSlotsNoLongerBelongToHold,
                9 => Error.HoldFinalizationInProgress,
                _ => Error.HoldServiceUnavailable
            };

            return new CacheHoldResponse
            {
                ErrorMessage = error
            };
        }

        
        catch(RedisConnectionException ex)
        {
            _logger.LogError(ex, "Redis connection failed.");
            return new CacheHoldResponse
            {
                ErrorMessage = Error.HoldServiceUnavailable
            };
        }
        catch(RedisTimeoutException ex)
        {
            _logger.LogError(ex, "Redis operation timed out.");
            return new CacheHoldResponse
            {
                ErrorMessage = Error.HoldServiceUnavailable
            };
        }
        catch(RedisServerException ex)
        {
            _logger.LogError(ex, "Redis server error occurred");
            return new CacheHoldResponse
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

    public async Task<BeginFinalizationResponse> BeginFinalizationAsync(string userId)
    {
        var getHoldId = await GetRedisValueAsync(GetUserHoldIdKey(userId));

        if(getHoldId.ErrorMessage != Error.none)
        {
            return new BeginFinalizationResponse
            {
                ErrorMessage = getHoldId.ErrorMessage
            };
        }

        if(!Guid.TryParse(getHoldId.ValueRedis, out var holdId))
        {
            return new BeginFinalizationResponse
            {
                ErrorMessage = Error.UserDosentHaveAnActiveHold
            };
        }
        var getHoldJson = await GetRedisValueAsync(GetHoldDataKey(holdId));
        if(getHoldJson.ErrorMessage != Error.none)
        {
            return new BeginFinalizationResponse
            {
                ErrorMessage = getHoldJson.ErrorMessage
            };
        }
        else if (getHoldJson.ValueRedis.IsNull)
        {
            return new BeginFinalizationResponse
            {
                ErrorMessage = Error.DontHaveAHold
            };
        }
        CacheHoldModel? getHold = null;
        try
        {
            getHold = JsonSerializer.Deserialize<CacheHoldModel>(getHoldJson.ValueRedis.ToString());
        }
        catch(JsonException ex)
        {
            _logger.LogError(ex, "Failed to deserialize json for bookings");
        }
        if(getHold == null)
        {
            return new BeginFinalizationResponse
            {
                ErrorMessage = Error.DontHaveAHold
            };
        }

        
        if(userId != getHold.UserId || holdId != getHold.HoldId)
        {
            return new BeginFinalizationResponse
            {
                ErrorMessage = Error.HoldBookingDosentBelongToTheUser
            };
        }
        var dateTime = getHold.BookingDate.ToDateTime(getHold.BookingTime);
        var validate = _bookingTimeService.ValidateBookingTime(dateTime, getHold.Duration);

        if(validate != Error.none)
        {
            return new BeginFinalizationResponse
            {
                ErrorMessage = validate
            };
        }

        var slotCount = getHold.Duration / 30;
        List<string> redisKeys = new List<string>
        {
            GetUserHoldIdKey(userId),
            GetHoldDataKey(holdId)
        };
        for(int i = 0; i < slotCount; i++)
        {
            var startTime = getHold.BookingTime.AddMinutes(i * 30);
            var slotKey = GetSlotHoldIdKey(getHold.CourtId, getHold.BookingDate, startTime);
            redisKeys.Add(slotKey);
        }
        redisKeys.Add(GetFinalizingKey(holdId));
        
        var lockToken = Guid.NewGuid();

        RedisKey[] keys = redisKeys.Select(x => (RedisKey)x).ToArray();
        RedisValue[] values =
        {
            getHold.HoldId.ToString(),
            getHoldJson.ValueRedis.ToString(),
            lockToken.ToString(),
        };

        return await BegingFinalizationScriptAsync(keys, values, lockToken);
    }
    private async Task<BeginFinalizationResponse> BegingFinalizationScriptAsync(RedisKey[] keys, RedisValue[] values, Guid lockTocken)
    {
        try
        {
        
            var database = _connectionMultiplexer.GetDatabase();
            var generateScriptRelease = await database.ScriptEvaluateAsync(BeginFinalizationScript, keys, values);
            var status = (int)generateScriptRelease;

            if(status == 1)
            {
                return new BeginFinalizationResponse
                {
                    Success = true,
                    LockToken = lockTocken
                };
            }

            var error = status switch
            {
                2 => Error.UserDosentHaveAnActiveHold,
                3 => Error.HoldDataHasChanged,
                5 => Error.HoldExpirationTimeUnavailable,
                6 => Error.OldSlotsNoLongerBelongToHold,
                9 => Error.HoldFinalizationInProgress,
                10 => Error.FinalizationLockMismatch,
                11 => Error.CouldNotDeserializeJson,
                _ => Error.HoldServiceUnavailable
            };

            return new BeginFinalizationResponse
            {
                ErrorMessage = error
            };
        }

        
        catch(RedisConnectionException ex)
        {
            _logger.LogError(ex, "Redis connection failed.");
            return new BeginFinalizationResponse
            {
                ErrorMessage = Error.HoldServiceUnavailable
            };
        }
        catch(RedisTimeoutException ex)
        {
            _logger.LogError(ex, "Redis operation timed out.");
            return new BeginFinalizationResponse
            {
                ErrorMessage = Error.HoldServiceUnavailable
            };
        }
        catch(RedisServerException ex)
        {
            _logger.LogError(ex, "Redis server error occurred");
            return new BeginFinalizationResponse
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

    public async Task<BoolResponseModel> CompleteFinalizationaAync(string userId, Guid? lockToken)
    {
      var getHoldId = await GetRedisValueAsync(GetUserHoldIdKey(userId));

        if(getHoldId.ErrorMessage != Error.none)
        {
            return new BoolResponseModel
            {
                ErrorMessage = getHoldId.ErrorMessage
            };
        }

        if(!Guid.TryParse(getHoldId.ValueRedis, out var holdId))
        {
            return new BoolResponseModel
            {
                ErrorMessage = Error.UserDosentHaveAnActiveHold
            };
        }
        var getHoldJson = await GetRedisValueAsync(GetHoldDataKey(holdId));
        if(getHoldJson.ErrorMessage != Error.none)
        {
            return new BoolResponseModel
            {
                ErrorMessage = getHoldJson.ErrorMessage
            };
        }
        else if (getHoldJson.ValueRedis.IsNull)
        {
            return new BoolResponseModel
            {
                ErrorMessage = Error.DontHaveAHold
            };
        }
        CacheHoldModel? getHold = null;
        try
        {
            getHold = JsonSerializer.Deserialize<CacheHoldModel>(getHoldJson.ValueRedis.ToString());
        }
        catch(JsonException ex)
        {
            _logger.LogError(ex, "Failed to deserialize json for bookings");
        }
        if(getHold == null)
        {
            return new BoolResponseModel
            {
                ErrorMessage = Error.DontHaveAHold
            };
        }

        
        if(userId != getHold.UserId || holdId != getHold.HoldId)
        {
            return new BoolResponseModel
            {
                ErrorMessage = Error.HoldBookingDosentBelongToTheUser
            };
        }

        var slotCount = getHold.Duration / 30;
        List<string> redisKeys = new List<string>
        {
            GetUserHoldIdKey(userId),
            GetHoldDataKey(holdId)
        };
        for(int i = 0; i < slotCount; i++)
        {
            var startTime = getHold.BookingTime.AddMinutes(i * 30);
            var slotKey = GetSlotHoldIdKey(getHold.CourtId, getHold.BookingDate, startTime);
            redisKeys.Add(slotKey);
        }
        redisKeys.Add(GetFinalizingKey(holdId));
        
        RedisKey[] keys = redisKeys.Select(x => (RedisKey)x).ToArray();
        RedisValue[] values =
        {
            getHold.HoldId.ToString(),
            getHoldJson.ValueRedis.ToString(),
            lockToken.ToString()
        };
        
        return await ExecuteBoolResponseModelScripts(keys, values, CompleteFinalizationScript);
        
    }
    public async Task<BoolResponseModel> AbortFinalizationAsync(string userId, Guid? lockToken)
    {
        var getHoldId = await GetRedisValueAsync(GetUserHoldIdKey(userId));

        if(getHoldId.ErrorMessage != Error.none)
        {
            return new BoolResponseModel
            {
                ErrorMessage = getHoldId.ErrorMessage
            };
        }

        if(!Guid.TryParse(getHoldId.ValueRedis, out var holdId))
        {
            return new BoolResponseModel
            {
                ErrorMessage = Error.UserDosentHaveAnActiveHold
            };
        }
        var getHoldJson = await GetRedisValueAsync(GetHoldDataKey(holdId));
        if(getHoldJson.ErrorMessage != Error.none)
        {
            return new BoolResponseModel
            {
                ErrorMessage = getHoldJson.ErrorMessage
            };
        }
        else if (getHoldJson.ValueRedis.IsNull)
        {
            return new BoolResponseModel
            {
                ErrorMessage = Error.DontHaveAHold
            };
        }
        CacheHoldModel? getHold = null;
        try
        {
            getHold = JsonSerializer.Deserialize<CacheHoldModel>(getHoldJson.ValueRedis.ToString());
        }
        catch(JsonException ex)
        {
            _logger.LogError(ex, "Failed to deserialize json for bookings");
        }
        if(getHold == null)
        {
            return new BoolResponseModel
            {
                ErrorMessage = Error.DontHaveAHold
            };
        }

        
        if(userId != getHold.UserId || holdId != getHold.HoldId)
        {
            return new BoolResponseModel
            {
                ErrorMessage = Error.HoldBookingDosentBelongToTheUser
            };
        }

        var slotCount = getHold.Duration / 30;
        List<string> redisKeys = new List<string>
        {
            GetUserHoldIdKey(userId),
            GetHoldDataKey(holdId)
        };
        for(int i = 0; i < slotCount; i++)
        {
            var startTime = getHold.BookingTime.AddMinutes(i * 30);
            var slotKey = GetSlotHoldIdKey(getHold.CourtId, getHold.BookingDate, startTime);
            redisKeys.Add(slotKey);
        }
        redisKeys.Add(GetFinalizingKey(holdId));
        
        RedisKey[] keys = redisKeys.Select(x => (RedisKey)x).ToArray();
        RedisValue[] values =
        {
            getHold.HoldId.ToString(),
            getHoldJson.ValueRedis.ToString(),
            lockToken.ToString()
        }; 

        return await ExecuteBoolResponseModelScripts(keys, values, AbortFinalizationScript);
    }

    private string GetSlotHoldIdKey(int courtId, DateOnly date, TimeOnly slotStart)
    {
        return $"hold:slot:{courtId}:{date:yyyy-MM-dd}:{slotStart:HH:mm}";

    }
    private string GetUserHoldIdKey(string userId)
    {
        return $"hold:user:{userId}";
    }
    private string GetHoldDataKey(Guid holdId)
    {
        return $"hold:{holdId}";
    }
    public string GetFinalizingKey(Guid holdId)
    {
        return $"hold:finalizing:{holdId}";
    }
}