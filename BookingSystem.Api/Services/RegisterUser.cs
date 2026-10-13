namespace BookingSystem.Api.Services;

using BookingSystem.Api.Models.ResponseModel;
using BookingSystem.Api.Models.SystemModels;
using Microsoft.AspNetCore.Identity;
using Microsoft.VisualBasic;

/// <summary>
/// Handles the registration of a new user account.
/// Uses Usermanager to create users and assign roles to newly registered accounts.
/// If role assignment fails, the newly created account is deleted to prevent users from existing without an assigned role.
/// 
/// </summary>
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

        if (!creation.Succeeded)
        {
            return new RegisterResult
            {
                Success = false,
                Errors = creation.Errors
            };
        }
        var roleResult = await _userManager.AddToRoleAsync(applicationUser, RoleNames.User);
        if (!roleResult.Succeeded)
        {
            var deleteResult = await _userManager.DeleteAsync(applicationUser);
            if (!deleteResult.Succeeded)
            {
                var errors = roleResult.Errors.Concat(deleteResult.Errors);
                return new RegisterResult
                {
                    Errors = errors
                };
            }
            return new RegisterResult
            {
                Errors = roleResult.Errors
            };
        }
        UserRegisterResponse userRegisterResponse = new UserRegisterResponse()
        {
            UserName = applicationUser.UserName,
            Email = applicationUser.Email
        };
            return new RegisterResult
            {
                User = userRegisterResponse,
                Success = true,
            };


    }


}