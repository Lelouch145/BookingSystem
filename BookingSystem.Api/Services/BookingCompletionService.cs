using BookingSystem.Api.Database;
using BookingSystem.Api.Models.SystemModels;
using Microsoft.EntityFrameworkCore;
namespace BookingSystem.Api.Services;

public class BookingCompletionService
{
    private readonly AppDbContext _dbContext;

    public BookingCompletionService(AppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task CompleteExpiresBookingAsync(CancellationToken cancellationToken)
    {
            var expiredBookings = await _dbContext.Bookings.Where(x => x.Status == BookingStatus.Confirmed
            && x.EndTime <= DateTime.Now).ToListAsync(cancellationToken);

            foreach (var booking in expiredBookings)
            {
                booking.Status = BookingStatus.Completed;
            }
            await _dbContext.SaveChangesAsync(cancellationToken);
    }
}