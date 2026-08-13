namespace BookingSystem.Api.Models.ResponseModel;

using BookingSystem.Api.Models.SystemModels;

public class CourtResult
{
    public Court? Courts { get; set; } = new Court();
    public Error ErrorMessage { get; set; } = new Error();
}