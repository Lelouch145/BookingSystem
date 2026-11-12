using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace BookingSystem.Tests;

public class UpdateException : SaveChangesInterceptor
{
    public override ValueTask<InterceptionResult<int>> SavingChangesAsync
    (
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default
    )
    {
        throw new DbUpdateException("Simulated database failure");
    }
}