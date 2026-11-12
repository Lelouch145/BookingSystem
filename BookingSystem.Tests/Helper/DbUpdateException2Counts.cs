using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace BookingSystem.Tests;

public class UpdateExceptionSecond : SaveChangesInterceptor
{
    private int _counter = 0;

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync
    (
        
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        _counter++;

        if(_counter == 2)
        {
            throw new DbUpdateException("Simulated second save failure");
        }

        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }
}