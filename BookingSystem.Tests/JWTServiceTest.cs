using System.IdentityModel.Tokens.Jwt;
using System.IO.Compression;
using System.Security.Claims;
using BookingSystem.Api.Database;
using BookingSystem.Api.Models.SystemModels;
using BookingSystem.Api.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualBasic;

namespace BookingSystem.Tests;

public class JWTServiceTest
{
    [Fact]
    public async Task TokenCreationAsync()
    {
        var dictionary = new Dictionary<string, string?>()
        {
            {"JWT:KEY", "asdasdalksdalskdjaakjashdahskdwhdahjsdasllawdakldklasdklawd" },
            {"JWT:Issuer", "TestIssuer"},
            {"JWT:Audience", "TestAudience"},
            {"JWT:ExpirationMinutes", "60"},
        };
        var helper = new HelperUnit();
        var config = new ConfigurationBuilder().AddInMemoryCollection(dictionary).Build();
        var configUserSecret = new ConfigurationBuilder().AddUserSecrets<JWTServiceTest>().Build();
        var connectionString = configUserSecret.GetConnectionString("DefaultConnection");

        var services = new ServiceCollection();

        var user = helper.CreateNewUser();

        services.AddDbContext<AppDbContext>(options =>
            options.UseSqlServer(connectionString))
            .AddIdentityCore<ApplicationUser>()
            .AddRoles<IdentityRole>()
            .AddEntityFrameworkStores<AppDbContext>();

        var serviceProvider = services.BuildServiceProvider();
        var userManager = serviceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var roleManager = serviceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        var createUser = await userManager.CreateAsync(user, "TestPassword_123");

        var roleExist = await roleManager.RoleExistsAsync("User");
        if (!roleExist)
        {
           await roleManager.CreateAsync(new IdentityRole("User"));
        }
        var roleUser = await  userManager.AddToRoleAsync(user, "User");

        var findUser = await userManager.FindByEmailAsync(user.Email!);
        Assert.NotNull(findUser);

        var service = new JWTService(userManager, config);

        var result = await service.TokenCreationAsync(user);
        var token = new JwtSecurityTokenHandler().ReadJwtToken(result);
        Assert.NotNull(result);
        var claims = token.Claims.Select(x => new { x.Type, x.Value }).ToList();
    
        Assert.Equal("TestIssuer", token.Issuer);
        Assert.Contains(claims, x => x.Type == ClaimTypes.Email && x.Value == "test@test.com");
        Assert.Contains(claims, x => x.Type == ClaimTypes.NameIdentifier && x.Value == user.Id);
        Assert.Contains(claims, x => x.Type == ClaimTypes.Name && x.Value == "TestUser");


            
    }
}