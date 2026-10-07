namespace BookingSystem.Api.Services;

public class cacheKey
{
    public static string AvailabilityKey(int courtId, DateOnly bookingDate, int duration)
    {
        return $"availability:{courtId}:{bookingDate:yyyy-MM-dd}:{duration}";
    }
}