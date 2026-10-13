namespace BookingSystem.Api.Services;

/// <summary>
/// Helper class responsible for generating consistent cache keys.
/// Centralizes cache key formatting to prevent typos and incosistencies
/// across different services when stroing, retrieving or invalidating cache data
/// </summary>
public class cacheKey
{
    public static string AvailabilityKey(int courtId, DateOnly bookingDate, int duration)
    {
        return $"availability:{courtId}:{bookingDate:yyyy-MM-dd}:{duration}";
    }
}