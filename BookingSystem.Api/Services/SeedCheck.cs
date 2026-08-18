using BookingSystem.Api.Models.SystemModels;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace BookingSystem.Api.Services;

public class SeedService
{
    private readonly RoleManager<IdentityRole> _roleManager;
    private readonly IConfiguration _config;
    private readonly UserManager<ApplicationUser> _userManager;

    public SeedService(RoleManager<IdentityRole> roleManager, IConfiguration config, UserManager<ApplicationUser> userManager)
    {
        _roleManager = roleManager;
        _config = config;
        _userManager = userManager;
    }

    public async Task SeedRoleAsync()
    {
        var userRole = await _roleManager.RoleExistsAsync("User");
        var adminRole = await _roleManager.RoleExistsAsync("Admin");

        if (!userRole)
        {
            var createUserRole = await _roleManager.CreateAsync(new IdentityRole("User"));
            if (!createUserRole.Succeeded)
            {
                var errors = string.Join(", ", createUserRole.Errors.Select(x => x.Description));

                throw new InvalidOperationException($"Could not create User role: {errors}");
            }
        }
        if (!adminRole)
        {
            var createAdminRole = await _roleManager.CreateAsync(new IdentityRole("Admin"));


            if (!createAdminRole.Succeeded)
            {
                var errors = string.Join(", ", createAdminRole.Errors.Select(x => x.Description));

                throw new InvalidOperationException($"Could not create Admin role: {errors}");
            }
        }
        
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
            if (createAdminAcc.Succeeded)
            {
                var addAdminRole = await _userManager.AddToRoleAsync(adminUser, "Admin");
                if (!addAdminRole.Succeeded)
                {
                    var errors = string.Join(", ", addAdminRole.Errors.Select(x => x.Description));
                    throw new InvalidOperationException($"Could not Add admin role to admin user: {errors}");
                }

            }
            else
            {
                var errors = string.Join(", ", createAdminAcc.Errors.Select(x => x.Description));
                throw new InvalidOperationException($"Could not Admin account: {errors}");
            }
        }
        var isAdmin = await _userManager.IsInRoleAsync(adminUser, "Admin");

        if(!isAdmin)
        {
            var addAdminRole = await _userManager.AddToRoleAsync(adminUser, "Admin");

            if (!addAdminRole.Succeeded)
            {
                var errors = string.Join(", ", addAdminRole.Errors.Select(x => x.Description));


                throw new InvalidOperationException($"Could not add Admin role to admin user: {errors}");
            }
        }
        

    }
    
}