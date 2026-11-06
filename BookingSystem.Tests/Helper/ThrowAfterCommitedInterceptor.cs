using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace BookingSystem.Tests;

public class ThrowAfterCommitedInterceptor : DbTransactionInterceptor
{
    public override Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default)
    {
        throw new InvalidOperationException("Simulated exception after successful SQL commit");
    }

}