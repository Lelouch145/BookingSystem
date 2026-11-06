using StackExchange.Redis;

namespace BookingSystem.Api.Models.SystemModels;

public class PreparedHoldResult
{
    public CacheHoldModel? HoldModel {get;set;}
    public string? OriginalJson {get;set;}
    public RedisKey[] Keys {get;set;} = [];
    public RedisValue[] Values {get;set;} = [];
    public Error ErrorMessage {get;set;} = Error.none;
}