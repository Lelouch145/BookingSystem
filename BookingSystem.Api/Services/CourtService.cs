using System.Globalization;
using System.Net.Mime;
using System.Threading.Tasks;
using BookingSystem.Api.Database;
using BookingSystem.Api.Models.ResponseModel;
using BookingSystem.Api.Models.SystemModels;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Update.Internal;
using Microsoft.Extensions.Caching.Distributed;
using System.Text.Json;
using StackExchange.Redis;
using BookingSystem.Api.Services.Caching;

namespace BookingSystem.Api.Services;
/// <summary>
/// Handles court-related operations such as creating, retrieving, updating and disabling courts.
/// Uses AppDbContext to interact with SQL Server through Entity Framework Core.
/// Uses IDistributedCache for basic Redis operations suc as retrieving, storing and removing cache.
/// Uses ILogger to log warnings when chache operations fail.
/// Uses RedisCacheService to scan for and delte availability cache keys
/// through IConnectionMultiplexer
/// </summary>
public class CourtService
{
    private const string CourtsAll = "courts:all";
    private const string CourtsActive = "courts:active";
    private const int TTLTime = 5;
    private readonly AppDbContext _dbContext;
    private readonly IDistributedCache _cache;
    private readonly ILogger<CourtService> _logger;
    private readonly RedisCacheService _redisCacheService;

    public CourtService(AppDbContext dbContext, IDistributedCache cache, ILogger<CourtService> logger, RedisCacheService redisCacheService)
    {
        _dbContext = dbContext;
        _cache = cache;
        _logger = logger;
        _redisCacheService = redisCacheService;
    }
    /// <summary>
    /// Creates a new court after validating the provided court name.
    /// Ensures that the court name is not empty and that no court with the same name already exists.
    /// If the court is successfully created, the cache entries for all courts and active court are invalidated.
    /// This method is intended for administrators only, with authorization enforeced at the endpoint level.
    /// </summary>
    /// <param name="courtName">The name of the court to create</param>
    /// <param name="description">The description of the court</param>
    /// <param name="cancellationToken">The token used to cancel operation</param>
    /// <returns>A CourtResult containing the created court information or an error.</returns>
    public async Task<CourtResult> CreateCourt(string courtName, string description, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(courtName))
        {
            return new CourtResult
            {
                ErrorMessage = Error.InvalidCourtName
            };
        }
        courtName = NormalizeString(courtName);
        try
        {
            var duplicateControl = await _dbContext.Courts.AnyAsync(x =>
                x.CourtName == courtName, cancellationToken);
            if (duplicateControl)
            {
                return new CourtResult
                {
                    ErrorMessage = Error.CourtAlreadyExists
                };
            }
            Court newCourt = new Court
            {
                CourtName = courtName,
                Description = description,
            };

            _dbContext.Courts.Add(newCourt);
            await _dbContext.SaveChangesAsync(cancellationToken);
            CourtResult courtResult = new CourtResult
            {
                Courts = newCourt,
                ErrorMessage = Error.none
            };


            await InvalidateCourtCacheAsync(CourtsAll, cancellationToken);
            await InvalidateCourtCacheAsync(CourtsActive, cancellationToken);
            return courtResult;
        }
        catch (DbUpdateException ex)
        {
            if (ex.InnerException is SqlException sqlException && (sqlException.Number == 2601 || sqlException.Number == 2627))
            {
                return new CourtResult
                {
                    ErrorMessage = Error.CourtAlreadyExists
                };
            }
            throw;
        }


    }
/// <summary>
/// Retrieves all courts that have been created, including inactive courts.
/// If cached court data exists, it is returned directly.
/// Otherwise, the method retrieves all courts from the database
/// and stores the result in cache for faster access on future requests.
/// This method is intended for administrators only, with authorization enforced at the endpoint level.
/// </summary>
/// <param name="cancellationToken">Token used to cancel the operation</param>
/// <returns>An IEnumerable of Court objects containing all courts</returns>
    public async Task<IEnumerable<Court>> ShowCourts(CancellationToken cancellationToken)
    {
        string? cacheCourts = null;
        bool redisAvailable = true;
        try
        {
            cacheCourts = await _cache.GetStringAsync(CourtsAll, cancellationToken);
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
        if (cacheCourts != null)
        {
            try
            {
                var deserializedCache = JsonSerializer.Deserialize<List<Court>>(cacheCourts);
                if (deserializedCache != null)
                {
                    return deserializedCache;
                }
            }
            catch(JsonException ex)
            {
                _logger.LogWarning(ex, "Failed to deserialize cached courts");
            }

        }

        var courts = await _dbContext.Courts.ToListAsync(cancellationToken);
        var serializedCourts = JsonSerializer.Serialize(courts);
        var options = new DistributedCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(TTLTime)
        };
        if (redisAvailable)
        {
            try
            {
                await _cache.SetStringAsync(CourtsAll, serializedCourts, options, cancellationToken);
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
        return courts;

    }
/// <summary>
/// Retrieves all active courts.
/// If cached court data exists, it is returned directly
/// Otherwise, the method retrieves all active courts from the database
/// and stores the result in cache for faster access on future requests.
/// This method is intended for regular users to view available courts.
/// </summary>
/// <param name="cancellationToken">Token used to cancel the operation.</param>
/// <returns>An IEnumerable of Court objects containing all active courts</returns>
    public async Task<IEnumerable<Court>> ShowActiveCourts(CancellationToken cancellationToken)
    {
        string? cacheActiveCourts = null;
        bool redisAvailable = true;
        try
        {
            cacheActiveCourts = await _cache.GetStringAsync(CourtsActive, cancellationToken);
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

        if (cacheActiveCourts != null)
        {
            try
            {
                var deserializedCache = JsonSerializer.Deserialize<List<Court>>(cacheActiveCourts);
                if (deserializedCache != null)
                {
                    return deserializedCache;
                }
            }
            catch (JsonException ex)
            {
                _logger.LogError(ex, "Failed to deserialize active courts");
            }
        }
        var activeCourts = await _dbContext.Courts.Where(x => x.IsActive).ToListAsync(cancellationToken);
        var serializedCourts = JsonSerializer.Serialize(activeCourts);
        var options = new DistributedCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(TTLTime)
        };
        if (redisAvailable)
        {
            try
            {
                
                await _cache.SetStringAsync(CourtsActive, serializedCourts, options, cancellationToken);
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
        return activeCourts;

    }
/// <summary>
/// Disables and existing court.
/// Validates courtName and searches in the database for the specified court.
/// If court exists and is active it gets its status changed to inactive.
/// Invalidates the cached lists of all courts and active courts via IDistributedCache.
/// Uses RedisCacheService to scan for and remove all availablility cache key associated with the disabled court.
/// This method is intended for administrators only, with authorization enforced at the endpoint level.
/// </summary>
/// <param name="courtName">The desired courts name.</param>
/// <param name="cancellationToken">Token used to cancel the operation.</param>
/// <returns>A CourtResult indicating the outcome of the operation.</returns>
    public async Task<CourtResult> DisableCourt(string courtName, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(courtName))
        {
            return new CourtResult
            {
                ErrorMessage = Error.InvalidCourtName
            };
        }
        courtName = NormalizeString(courtName);
        var search = await _dbContext.Courts.FirstOrDefaultAsync(x => x.CourtName == courtName, cancellationToken);

        if (search == null)
        {
            return new CourtResult
            {
                ErrorMessage = Error.CouldNotFindTheCourtInDataBase
            };
        }
        if(search.IsActive == false)
        {
            return new CourtResult
            {
                Courts = null,
                ErrorMessage = Error.CourtIsAlreadyInActive
            };
        }
        search.IsActive = false;

        await _dbContext.SaveChangesAsync(cancellationToken);
        await InvalidateCourtCacheAsync(CourtsAll, cancellationToken);
        await InvalidateCourtCacheAsync(CourtsActive, cancellationToken);
        await _redisCacheService.DeleteFromRedis(search.Id);
        return new CourtResult
        {
            ErrorMessage = Error.none
        };


    }
/// <summary>
/// Updates an existing court.
/// Allows updating the courts name, description and active status.
/// Invalidates the cached lists of all courts and active courts via IDistributedCache.
/// This method is intended for administrators only, with authorization enforced at the endpoint level.
/// </summary>
/// <param name="findCourt">The name of the court to update</param>
/// <param name="courtName">The new name of the court</param>
/// <param name="description">The new description of the court</param>
/// <param name="isActive">The new active status of the court</param>
/// <param name="cancellationToken">Token used to cancel the operation</param>
/// <returns>CourtResult indicating the outcome of the operation</returns>
    public async Task<CourtResult> UpdateCourt(string findCourt, string courtName, string description, bool isActive, CancellationToken cancellationToken)
    {

        if (string.IsNullOrWhiteSpace(courtName))
        {
            return new CourtResult
            {
                Courts = null,
                ErrorMessage = Error.InvalidCourtName
            };
        }
        if (string.IsNullOrWhiteSpace(findCourt))
        {
            return new CourtResult
            {
                ErrorMessage = Error.InvalidCourtSearchName
            };
        }

        courtName = NormalizeString(courtName);

        var databaseCourt = await _dbContext.Courts.FirstOrDefaultAsync(x => x.CourtName == findCourt, cancellationToken);

        if (databaseCourt == null)
        {
            return new CourtResult
            {
                ErrorMessage = Error.CouldNotFindTheCourtInDataBase
            };
        }
        try
        {
            var duplicateControl = await _dbContext.Courts.AnyAsync(x =>
            x.CourtName == courtName && x.Id != databaseCourt.Id, cancellationToken);
            if (duplicateControl)
            {
                return new CourtResult
                {
                    ErrorMessage = Error.CourtAlreadyExists
                };
            }

            databaseCourt.CourtName = courtName;
            databaseCourt.Description = description;
            databaseCourt.IsActive = isActive;
            await _dbContext.SaveChangesAsync(cancellationToken);
            await InvalidateCourtCacheAsync(CourtsAll, cancellationToken);
            await InvalidateCourtCacheAsync(CourtsActive, cancellationToken);

            return new CourtResult
            {
                Courts = databaseCourt,
                ErrorMessage = Error.none
            };
        }
        catch(DbUpdateException ex)
        {
            if (ex.InnerException is SqlException sqlException && (sqlException.Number == 2601 || sqlException.Number == 2627))
            {
                return new CourtResult
                {
                    ErrorMessage = Error.CourtAlreadyExists
                };
            }
            throw;
        }


    }

    private string NormalizeString(string courtName)
    {
        courtName = courtName.Trim().ToLowerInvariant();
        courtName = CultureInfo.InvariantCulture.TextInfo.ToTitleCase(courtName);
        return courtName;
    }
    /// <summary>
    /// Removes a specified cache entry using its cache key.
    /// Handles Redis connection and timeout erros to prevent cache failures
    /// fro affecting the main operation
    /// </summary>
    /// <param name="cacheKey">The keyof the cache entry to remove. </param>
    /// <param name="cancellationToken">Token used to cancel operation</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    private async Task InvalidateCourtCacheAsync(string removeCache, CancellationToken cancellationToken)
    {
        try
        {
            await _cache.RemoveAsync(removeCache);
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