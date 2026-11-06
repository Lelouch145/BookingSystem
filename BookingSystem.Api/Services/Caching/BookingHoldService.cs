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
        var result = await ExecuteRedisScriptAsync(keys, values, redisScript);
        RedisResult[]? results = (RedisResult[]?)result.RedisResults;
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

        var error = MapLuaStatusToError(status);

        return new CreateHoldResponse
        {
            ErrorMessage = error
        };

            
    }

    public async Task<CreateHoldResponse> SwitchHoldAsync(DateOnly newDate, TimeOnly newBookingTime, int duration, int courtId, string userId, CancellationToken cancellationToken)
    {
        var helper = await PreparedHoldAsync(userId);
        if(helper.ErrorMessage != Error.none)
        {
            return new CreateHoldResponse
            {
                ErrorMessage = helper.ErrorMessage
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
            HoldId = helper.HoldModel!.HoldId,
            CourtId = courtId,
            UserId = userId,
            BookingDate = newDate,
            BookingTime = newBookingTime,
            Duration = duration
        };

        var newSlotCount = duration / 30;

        var newSlotKeys = new List<RedisKey>();
        for(var i = 0; i < newSlotCount; i++)
        {
            var time = newBookingTime.AddMinutes(i * 30);
            newSlotKeys.Add(GetSlotHoldIdKey(courtId,newDate, time));
        }

        var keys = helper.Keys.ToList();
        keys.InsertRange(2, newSlotKeys);
        keys.Add(GetFinalizingKey(helper.HoldModel!.HoldId));
        var redisKeys = keys.ToArray();

        var value = helper.Values.ToList();
        value.Insert(1, JsonSerializer.Serialize(newHold));
        value.Insert(2, newSlotCount);
        var values = value.ToArray();

        return await ExecuteHoldScriptAsync(redisKeys, values, newHold.HoldId, SwitchHoldScript);
    }

    public async Task<BoolResponseModel> ReleaseHoldAsync(string userId)
    {
        var helper = await PreparedHoldAsync(userId);

        if(helper.ErrorMessage != Error.none)
        {
            return new BoolResponseModel
            {
                Success = false,
                ErrorMessage = helper.ErrorMessage,
            };
        }
        var keys = helper.Keys
            .Append((RedisKey)GetFinalizingKey(helper.HoldModel!.HoldId))
            .ToArray();

        return await ExecuteBoolResponseModelScripts(keys, helper.Values, ReleaseHoldScript);
    }

    private async Task<BoolResponseModel> ExecuteBoolResponseModelScripts(RedisKey[] keys, RedisValue[] values, string script)
    {

        var result = await ExecuteRedisScriptAsync(keys, values, script);        
        var status = (int)result.RedisResults!;

        if(status == 1)
        {
            return new BoolResponseModel
            {
                Success = true
            };
        }

        var error = MapLuaStatusToError(status);

        return new BoolResponseModel
        {
            ErrorMessage = error
        };


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
        var helper = await PreparedHoldAsync(userId);
        if(helper.ErrorMessage != Error.none)
        {
            return new CacheHoldResponse
            {
                ErrorMessage = helper.ErrorMessage
            };
        }
        var dateTime = helper.HoldModel!.BookingDate.ToDateTime(helper.HoldModel.BookingTime);
        var validateBooking = _bookingTimeService.ValidateBookingTime(dateTime, helper.HoldModel.Duration);

        if(validateBooking != Error.none)
        {
            return new CacheHoldResponse
            {
                ErrorMessage = validateBooking
            };
        }

        return await ExecuteValidateHoldScriptAsync(helper.Keys, helper.Values, helper.HoldModel!);
        
    }

    private async Task<CacheHoldResponse> ExecuteValidateHoldScriptAsync(RedisKey[] keys, RedisValue[] values, CacheHoldModel hold)
    {

        var result = await ExecuteRedisScriptAsync(keys, values, GetValidateHoldScript);

        var status = (int)result.RedisResults!;

        if(status == 1)
        {
            return new CacheHoldResponse
            {
                CacheHold = hold
            };
        }

        var error = MapLuaStatusToError(status);

            return new CacheHoldResponse
            {
                ErrorMessage = error
            };


    }

    public async Task<BeginFinalizationResponse> BeginFinalizationAsync(string userId)
    {
        var helper = await PreparedHoldAsync(userId);
        if(helper.ErrorMessage != Error.none)
        {
            return new BeginFinalizationResponse
            {
                ErrorMessage = helper.ErrorMessage
            };
        }
        var dateTime = helper.HoldModel!.BookingDate.ToDateTime(helper.HoldModel!.BookingTime);
        var validate = _bookingTimeService.ValidateBookingTime(dateTime, helper.HoldModel.Duration);

        if(validate != Error.none)
        {
            return new BeginFinalizationResponse
            {
                ErrorMessage = validate
            };
        }
        var lockToken = Guid.NewGuid();
        var keys = helper.Keys.Append((RedisKey)GetFinalizingKey(helper.HoldModel!.HoldId))
            .ToArray();
        var values = helper.Values.Append((RedisValue)lockToken.ToString())
            .ToArray();
            

        return await BegingFinalizationScriptAsync(keys, values, lockToken, helper.HoldModel);
    }
    private async Task<BeginFinalizationResponse> BegingFinalizationScriptAsync(RedisKey[] keys, RedisValue[] values, Guid lockTocken, CacheHoldModel? hold)
    {

        var result = await ExecuteRedisScriptAsync(keys, values, BeginFinalizationScript);
        if(result.ErrorMessage != Error.none)
        {
            return new BeginFinalizationResponse
            {
                ErrorMessage = result.ErrorMessage
            };
        }

        var status = (int)result.RedisResults!;

        if(status == 1)
        {
            return new BeginFinalizationResponse
            {
                Success = true,
                LockToken = lockTocken,
                CacheHoldModel = hold
            };
        }
        var error = MapLuaStatusToError(status);

        return new BeginFinalizationResponse
        {
            ErrorMessage = error
        };
    }

    

    public async Task<BoolResponseModel> CompleteFinalizationAsync(string userId, Guid? lockToken)
    {
        var helper = await PreparedHoldAsync(userId);
        if(helper.ErrorMessage != Error.none)
        {
            return new BoolResponseModel
            {
                ErrorMessage = helper.ErrorMessage
            };
        }
        var keys = helper.Keys.Append((RedisKey)GetFinalizingKey(helper.HoldModel!.HoldId))
            .ToArray();
        var values = helper.Values.Append((RedisValue)lockToken.ToString())
            .ToArray();
        
        return await ExecuteBoolResponseModelScripts(keys, values, CompleteFinalizationScript);
        
    }
    public async Task<BoolResponseModel> AbortFinalizationAsync(string userId, Guid? lockToken)
    {
        var helper = await PreparedHoldAsync(userId);
        if(helper.ErrorMessage != Error.none)
        {
            return new BoolResponseModel
            {
                ErrorMessage = helper.ErrorMessage
            };
        }

        var keys = helper.Keys.Append((RedisKey)GetFinalizingKey(helper.HoldModel!.HoldId))
            .ToArray();
        var values = helper.Values.Append((RedisValue)lockToken.ToString())
            .ToArray();
        
        return await ExecuteBoolResponseModelScripts(keys, values, AbortFinalizationScript);
    }

    internal async Task<PreparedHoldResult> PreparedHoldAsync(string userId)
    {
        var getHoldId = await GetRedisValueAsync(GetUserHoldIdKey(userId));

        if(getHoldId.ErrorMessage != Error.none)
        {
            return new PreparedHoldResult
            {
                ErrorMessage = getHoldId.ErrorMessage
            };
        }

        if(!Guid.TryParse(getHoldId.ValueRedis, out var holdId))
        {
            return new PreparedHoldResult
            {
                ErrorMessage = Error.UserDosentHaveAnActiveHold
            };
        }
        var getHoldJson = await GetRedisValueAsync(GetHoldDataKey(holdId));
        if(getHoldJson.ErrorMessage != Error.none)
        {
            return new PreparedHoldResult
            {
                ErrorMessage = getHoldJson.ErrorMessage
            };
        }
        else if (getHoldJson.ValueRedis.IsNull)
        {
            return new PreparedHoldResult
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
            return new PreparedHoldResult
            {
                ErrorMessage = Error.CouldNotDeserializeJson
            };
        }

        
        if(userId != getHold.UserId || holdId != getHold.HoldId)
        {
            return new PreparedHoldResult
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
        
        RedisKey[] keys = redisKeys.Select(x => (RedisKey)x).ToArray();
        RedisValue[] values =
        {
            getHold.HoldId.ToString(),
            getHoldJson.ValueRedis.ToString(),
        }; 

        return new PreparedHoldResult
        {
            HoldModel = getHold,
            OriginalJson = getHoldJson.ValueRedis,
            Keys = keys,
            Values = values,
        };
    }

    private Error MapLuaStatusToError(int status)
    {
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
            10 => Error.FinalizationLockMismatch,
            11 => Error.CouldNotDeserializeJson,
            _ => Error.HoldServiceUnavailable
        };

        return error;
    }

    private async Task<ExecuteScriptHelperModel> ExecuteRedisScriptAsync(RedisKey[] keys, RedisValue[] values, string script)
    {
        try
        {
            var database = _connectionMultiplexer.GetDatabase();
            var generateScriptRelease = await database.ScriptEvaluateAsync(script, keys, values);

            return new ExecuteScriptHelperModel
            {
                RedisResults = generateScriptRelease
            };
        }
        catch(RedisConnectionException ex)
        {
            _logger.LogError(ex, "Redis connection failed.");
            return new ExecuteScriptHelperModel
            {
                ErrorMessage = Error.HoldServiceUnavailable
            };
        }
        catch(RedisTimeoutException ex)
        {
            _logger.LogError(ex, "Redis operation timed out.");
            return new ExecuteScriptHelperModel
            {
                ErrorMessage = Error.HoldServiceUnavailable
            };
        }
        catch(RedisServerException ex)
        {
            _logger.LogError(ex, "Redis server error occurred");
            return new ExecuteScriptHelperModel
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