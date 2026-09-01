
using BookingSystem.Api.Database;
using Microsoft.EntityFrameworkCore;
using BookingSystem.Api.Models.SystemModels;
namespace BookingSystem.Api.Services;

public class BookingCompletionBackgroundService : BackgroundService
{

    private readonly IServiceScopeFactory _scopeFactory;

    public BookingCompletionBackgroundService(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;

    }
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var completionService = scope.ServiceProvider.GetRequiredService<BookingCompletionService>();

            await completionService.CompleteExpiresBookingAsync(stoppingToken);

            await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);

        }
    }
    


}