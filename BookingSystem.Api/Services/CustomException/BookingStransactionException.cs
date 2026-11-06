namespace BookingSystem.Api.Services.CustomExceptions;

public class BookingTransactionException : Exception
{
    public BookingTransactionStatus TransactionStatus {get; }

    public BookingTransactionException(BookingTransactionStatus transactionStatus, Exception innerException) : base("Booking transaction", innerException)
    {
        TransactionStatus = transactionStatus;
    }
}