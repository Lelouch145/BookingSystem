namespace BookingSystem.Api.Services.CustomExceptions;

public enum BookingTransactionStatus
{
    none,
    RolledBack,
    CommitOutcomeUnknown
}