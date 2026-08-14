namespace BookingSystem.Api.Models.ResponseModel;

using BookingSystem.Api.Models.SystemModels;
using Microsoft.AspNetCore.Identity;

public class RegisterResult
{
    public UserRegisterResponse? User { get; set; }
    public bool Success { get; set; }
    public IEnumerable<IdentityError>? Errors { get; set; }
}