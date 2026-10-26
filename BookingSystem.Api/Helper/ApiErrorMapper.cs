namespace BookingSystem.Api.Helper;

using BookingSystem.Api.Models.SystemModels;

public class ErrorMessageHandler
{
    public static IResult MapError(Error error)
    {
        return error switch
        {
            // 400 Bad Request
            Error.InvalidCourtName or
            Error.InvalidCourtSearchName or
            Error.MailCannotBeEmpty or
            Error.UserNameCannotBeEmpty or
            Error.BookingCannotBeInThePast or
            Error.InvalidDuration or
            Error.CannotCancelBooking24HoursBeforeTheBooking or
            Error.CannotRescheduleBooking24HoursBeforeTheBooking
                => Results.BadRequest(error),

            // 401 Unauthorized – Ogiltiga inloggningsuppgifter
            Error.InvalidCredentials
                => Results.Json(error, statusCode: 401),

            // 403 Forbidden – Saknar behörighet
            Error.UserDoesNotOwnBooking or
            Error.BookingDosentBelongToTheUser or
            Error.HoldBookingDosentBelongToTheUser
                => Results.Json(error, statusCode: 403),

            // 404 Not Found – Resursen finns inte
            Error.CouldNotFindTheCourtInDataBase or
            Error.BookingNotFound or
            Error.CouldNotFins
                => Results.NotFound(error),

            // 409 Conflict – Konflikt med befintligt tillstånd
            Error.CourtAlreadyExists or
            Error.CourtIsAlreadyInActive or
            Error.CourtIsNotAvailable or
            Error.BookingTimeIsOverlappingWithAnotherBooking or
            Error.BookingTimeIsNotAvailable or
            Error.BookingIsAlreadyCancelled or
            Error.BookingIsCompletedAndCannotBeCancelled or
            Error.BookingIsCancelledAndCannotBeChanged or
            Error.BookingIsCompletedAndCannotBeChanged or
            Error.BookingWasModifiedByAnotherRequest or
            Error.TheBookingIsAlreadyOnHold or
            Error.DontHaveAHold or
            Error.UserDosentHaveAnActiveHold or
            Error.HoldDataHasChanged or
            Error.OldSlotsNoLongerBelongToHold or
            Error.UserAlreadyHasAnActiveHold or
            Error.BookingSlotAlreadyOnHold or
            Error.HoldExpirationTimeUnavailable
                => Results.Conflict(error),

            // 503 Service Unavailable
            Error.HoldServiceUnavailable
                => Results.Problem(title: "Hold service unavailable",
                statusCode: 503),

            // 500 Internal Server Error
            Error.CouldNotDeserializeJson or
            Error.CouldNotGetOldBookingSlots
                => Results.Problem(title: "Internal server error",
                statusCode: 500),
            
            Error.none
                => throw new ArgumentException("MapError should not be called with Error.none"),

            // unforseen errors
            _ => Results.Problem(title: "Unexpected server error",
                statusCode: 500)
            
        };
    }
}