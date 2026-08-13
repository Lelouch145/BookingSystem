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
    CouldNotFins
}