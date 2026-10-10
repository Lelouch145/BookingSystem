using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.EntityFrameworkCore.Metadata.Internal;
using StackExchange.Redis;
using RedisServer = StackExchange.Redis.IServer;

namespace BookingSystem.Api.Services.Caching;

public class RedisCacheService
{
    private const string Availability = "availability";
    private readonly IConnectionMultiplexer _connectionMultiplexer;
    private readonly ILogger<RedisCacheService> _logger;
    public RedisCacheService(IConnectionMultiplexer connectionMultiplexer, ILogger<RedisCacheService> logger)
    {
        _connectionMultiplexer = connectionMultiplexer;
        _logger = logger;
    }

    public RedisServer GetRedisServer()
    {
        var endpoint = _connectionMultiplexer.GetEndPoints().First();

        var server = _connectionMultiplexer.GetServer(endpoint);

        return server;
    }

    public IEnumerable<RedisKey> GetAvailabilityKeys(int courtId)
    {

        var server = GetRedisServer();

       var keys = server.Keys(pattern: $"{Availability}:{courtId}:*");

       return keys;
    }

    public async Task<long> DeleteFromRedis(int courtId)
    {
        long keyDeleteAsync = 0;
        try
        {
            var keys = GetAvailabilityKeys(courtId);

            var keysArray = keys.ToArray();

            if(keysArray.Length == 0)
            {
                return 0;
            }

            var database = _connectionMultiplexer.GetDatabase();

            keyDeleteAsync = await database.KeyDeleteAsync(keysArray);
            return keyDeleteAsync;
        }
        
        catch(RedisConnectionException ex)
        {
            _logger.LogWarning(ex, "Redis is down");
        }
        catch(RedisTimeoutException ex)
        {
            _logger.LogWarning(ex, "Redis timedout operation taking to long");
        }
        return keyDeleteAsync;



    }
}