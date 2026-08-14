using BookingSystem.Api.Models.SystemModels;
using Microsoft.AspNetCore.Identity;

namespace BookingSystem.Api.Models.ResponseModel;

public class LoginResult
{
    public bool Success { get; set; }
    public Error ErrorMessage { get; set; } = new Error();
    public string Token { get; set; } = "";
}