using System.Reflection.Metadata.Ecma335;
using BookingSystem.Api.Models.ResponseModel;
using BookingSystem.Api.Models.SystemModels;
using Microsoft.AspNetCore.Identity;

namespace BookingSystem.Api.Services;
/// <summary>
/// Handles user authentication by validating login credentials.
/// Uses UserManager to find users by email and verify their passwords.
/// Uses JWTService to generate a JWT token containing the authenticated users identity and claims.
/// The generated token allows users to access protected endpoints based on their permissions
/// </summary>
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
        if (findEmail == null)
        {
            return new LoginResult
            {
                Success = false,
                ErrorMessage = Error.InvalidCredentials
            };

        }

        var verifyPassword = await _userManager.CheckPasswordAsync(findEmail, password);

        if (!verifyPassword)
        {
            return new LoginResult
            {
                ErrorMessage = Error.InvalidCredentials
            };
        }

        var token = await _jwtService.TokenCreationAsync(findEmail);

        return new LoginResult
        {
            Success = true,
            ErrorMessage = Error.none,
            Token = token
        };


    }
}