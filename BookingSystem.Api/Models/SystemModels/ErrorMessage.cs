namespace BookingSystem.Api.Models.SystemModels;

public enum Error
{
    none,
    InvalidCourtName,
    CourtsDatabaseIsEmpty,
    CouldNotFindTheCourtInDataBase,
    CourtAlreadyExists,
    CourtIsAlreadyInActive,
    InvalidCourtSearchName,
    CouldNotFins,
    MailCannotBeEmpty,
    UserNameCannotBeEmpty,
    InvalidCredentials,
    CourtIsNotAvailable,
    BookingTimeIsOverlappingWithAnotherBooking,
    BookingCannotBeInThePast,
    InvalidDuration,
    BookingTimeIsNotAvailable,
    BookingNotFound,
    BookingIsAlreadyCancelled,
    CannotCancelBooking24HoursBeforeTheBooking,
    BookingIsCompletedAndCannotBeCancelled,
    BookingIsCancelledAndCannotBeChanged,
    BookingIsCompletedAndCannotBeChanged,
    UserDoesNotOwnBooking
}