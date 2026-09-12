using System.Threading.Tasks;
using BookingSystem.Api.Database;
using BookingSystem.Api.Models.SystemModels;
using BookingSystem.Api.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BookingSystem.Tests;

public class RegisterServiceTest : IClassFixture<DatabaseFixture>, IAsyncLifetime
{
    private DatabaseFixture _databaseFixture;

    public RegisterServiceTest(DatabaseFixture databaseFixture)
    {
        _databaseFixture = databaseFixture;
    }

    public async Task InitializeAsync()
    {
        await _databaseFixture.ResetDatabaseAsync();
    }

    public  Task DisposeAsync()
    {
        return Task.CompletedTask;
    }
    [Fact]
    public async Task RegisterUserServiceTest()
    {
        var services = new ServiceCollection();
        var configUserSecret = new ConfigurationBuilder().AddUserSecrets<RegisterServiceTest>().Build();
        var connectionString = configUserSecret.GetConnectionString("DefaultConnection");

        services.AddDbContext<AppDbContext>(options =>
            options.UseSqlServer(connectionString))
            .AddIdentityCore<ApplicationUser>()
            .AddRoles<IdentityRole>()
            .AddEntityFrameworkStores<AppDbContext>();
        var serviceProvider = services.BuildServiceProvider();

        var userManager = serviceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        var roleManager = serviceProvider.GetRequiredService<RoleManager<IdentityRole>>();

        var service = new RegisterService(userManager);

        var roleExist = await roleManager.RoleExistsAsync("User");
        if (!roleExist)
        {
            var createRole = roleManager.CreateAsync(new IdentityRole("User"));
        }

        var result = await service.Register("Test@gmail.com", "Test_123", "Test.password123");

        Assert.True(result.Success);
    }
}