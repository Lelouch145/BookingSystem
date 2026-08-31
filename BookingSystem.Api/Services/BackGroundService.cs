
using BookingSystem.Api.Database;
using Microsoft.EntityFrameworkCore;

namespace BookingSystem.Api.Services;

public class BackGroundTaskForComplete : BackgroundService
{

    private readonly IServiceScopeFactory _scopeFactory;

    public BackGroundTaskForComplete(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
    }
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var result = await dbContext.Bookings.Where(x => x.Status == Models.SystemModels.BookingStatus.Confirmed
                && x.EndTime <= DateTime.Now).ToListAsync(stoppingToken);

            foreach (var i in result)
            {
                i.Status = Models.SystemModels.BookingStatus.Completed;
            }
            await dbContext.SaveChangesAsync(stoppingToken);

            await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
            
        }
    }
}