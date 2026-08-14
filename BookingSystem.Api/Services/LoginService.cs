using BookingSystem.Api.Models.SystemModels;
using Microsoft.AspNetCore.Identity;

namespace BookingSystem.Api.Services;

public class LoginService
{
    private readonly UserManager<ApplicationUser> _userManager;

    public LoginService(UserManager<ApplicationUser> userManager)
    {
        _userManager = userManager;
    }

    public 
}