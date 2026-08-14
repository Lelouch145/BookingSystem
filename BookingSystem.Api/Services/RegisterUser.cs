namespace BookingSystem.Api.Services;

using BookingSystem.Api.Models.ResponseModel;
using BookingSystem.Api.Models.SystemModels;
using Microsoft.AspNetCore.Identity;

public class RegisterService
{

    private readonly UserManager<ApplicationUser> _userManager;

    public RegisterService(UserManager<ApplicationUser> userManager)
    {
        _userManager = userManager;
    }
    public async Task<RegisterResult> Register(string email, string userName, string password)
    {
        ApplicationUser applicationUser = new ApplicationUser()
        {
            UserName = userName,
            Email = email
        };


        var creation = await _userManager.CreateAsync(applicationUser, password);

        UserRegisterResponse userRegisterResponse = new UserRegisterResponse()
        {
            UserName = applicationUser.UserName,
            Email = applicationUser.Email
        };

        if (creation.Succeeded)
        {
            return new RegisterResult
            {
                User = userRegisterResponse,
                Success = true,
                Errors = creation.Errors
            };
        }
        else
        {
            return new RegisterResult
            {
                User = null,
                Success = false,
                Errors = creation.Errors
            };
        }

    }
}