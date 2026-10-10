using System.Data.Common;
using System.Runtime.InteropServices;
using BookingSystem.Api.Services.Caching;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using StackExchange.Redis;

namespace BookingSystem.Tests;

public class RedisIntegrationHelper
{
    public (IConnectionMultiplexer connection, IDatabase database, RedisCacheService redisCacheService) CreateRedisSetup()
    {
        var connection = ConnectionMultiplexer.Connect("localhost:6379");

        var database = connection.GetDatabase();

        var logger = NullLogger<RedisCacheService>.Instance;
        
        var redisCacheService = new RedisCacheService(connection, logger);

        return(connection, database, redisCacheService);
    }
}