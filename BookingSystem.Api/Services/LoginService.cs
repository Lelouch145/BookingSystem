using BookingSystem.Api.Models.ResponseModel;
using BookingSystem.Api.Models.SystemModels;
using Microsoft.AspNetCore.Identity;

namespace BookingSystem.Api.Services;

public class LoginService
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly JWTService _jwtService;

    public LoginService(UserManager<ApplicationUser> userManager, JWTService jwtService)
    {
        _userManager = userManager;
        _jwtService = jwtService;
    }

    public async Task<LoginResult> LoginAuthentication(string email, string password)
    {
        var findEmail = await _userManager.FindByEmailAsync(email);
        if (findEmail != null)
        {
            var verifyPassword = await _userManager.CheckPasswordAsync(findEmail, password);
            if (verifyPassword)
            {
                var token = await _jwtService.TokenCreationAsync(findEmail);
                return new LoginResult
                {
                    Success = true,
                    ErrorMessage = Error.none,
                    Token = token
                };
            }
            else
            {

                return new LoginResult
                {
                    Success = false,
                    ErrorMessage = Error.InvalidCredentials,

                };
            }
        }
        else
        {
            return new LoginResult
            {
                Success = false,
                ErrorMessage = Error.InvalidCredentials
            };
        }


    }
}