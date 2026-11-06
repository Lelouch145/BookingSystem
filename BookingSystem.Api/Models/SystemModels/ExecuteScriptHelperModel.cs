using StackExchange.Redis;

namespace BookingSystem.Api.Models.SystemModels;

public class ExecuteScriptHelperModel
{
    public Error ErrorMessage {get;set;} = Error.none;
    public RedisResult? RedisResults {get;set;}
}