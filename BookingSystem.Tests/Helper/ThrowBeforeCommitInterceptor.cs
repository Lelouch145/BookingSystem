using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace BookingSystem.Tests;

public class ThrowBeforeCommitInterceptor : DbTransactionInterceptor
{
    public override ValueTask<InterceptionResult> TransactionCommittingAsync(DbTransaction transaction, TransactionEventData eventData, InterceptionResult result, CancellationToken cancellationToken = default)
    {
        throw new InvalidOperationException("Simulated exception before SQL commit");
    }
}