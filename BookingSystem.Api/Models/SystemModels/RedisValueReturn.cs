using StackExchange.Redis;

namespace BookingSystem.Api.Models.SystemModels;

public class RedisValueReturn
{
    public RedisValue ValueRedis {get;set;}
    public Error ErrorMessage {get;set;} = Error.none;
}