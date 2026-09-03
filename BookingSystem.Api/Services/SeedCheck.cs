using BookingSystem.Api.Models.SystemModels;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace BookingSystem.Api.Services;

public class SeedService
{
    private const string AdminRole = "Admin";
    private const string UserRole = "User";
    private readonly RoleManager<IdentityRole> _roleManager;
    private readonly IConfiguration _config;
    private readonly UserManager<ApplicationUser> _userManager;

    public SeedService(RoleManager<IdentityRole> roleManager, IConfiguration config, UserManager<ApplicationUser> userManager)
    {
        _roleManager = roleManager;
        _config = config;
        _userManager = userManager;
    }

    public async Task SeedDataAsync()
    {
        await EnsureRoleExistsAsync(UserRole);
        await EnsureRoleExistsAsync(AdminRole);
        await SeedAdminUserAsync();
    }

    private async Task EnsureRoleExistsAsync(string roleName)
    {
        var roleExists = await _roleManager.RoleExistsAsync(roleName);

        if (!roleExists)
        {
            var result = await _roleManager.CreateAsync(new IdentityRole(roleName));
            if (!result.Succeeded)
            {
                var errors = string.Join(" , ", result.Errors.Select(x => x.Description));
                throw new InvalidOperationException($"Could not create role {roleName} : {errors}");
            }
        }
    }
    
    private async Task SeedAdminUserAsync()
    {
        var adminPassword = _config["Admin:Password"] ?? throw new InvalidOperationException("Admin password not found");
        var adminEmail = _config["Admin:email"] ?? throw new InvalidOperationException("Admin Email not found");


        var adminUser = await _userManager.FindByEmailAsync(adminEmail);


        if (adminUser == null)
        {

            adminUser = new ApplicationUser
            {
                UserName = _config["Admin:UserName"] ?? throw new InvalidOperationException("Admin user not found"),
                Email = adminEmail
            };
            var createAdminAcc = await _userManager.CreateAsync(adminUser, adminPassword);
            if (!createAdminAcc.Succeeded)
            {
                var errors = string.Join(", ", createAdminAcc.Errors.Select(x => x.Description));
                throw new InvalidOperationException($"Could not Add admin role to admin user: {errors}");


            }
            else
            {
                var errors = string.Join(", ", createAdminAcc.Errors.Select(x => x.Description));
                throw new InvalidOperationException($"Could not create Admin account: {errors}");
            }
        }

        var isAdmin = await _userManager.IsInRoleAsync(adminUser, AdminRole);
        if (!isAdmin)
        {
            var addAdminRole = await _userManager.AddToRoleAsync(adminUser, AdminRole);

            if (!addAdminRole.Succeeded)
            {
                var errors = string.Join(", ", addAdminRole.Errors.Select(x => x.Description));
                throw new InvalidOperationException($"Could not add Admin role to admin user: {errors} ");
            }
        }
        
    }
    
}