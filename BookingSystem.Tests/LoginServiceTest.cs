using System.Threading.Tasks;
using BookingSystem.Api.Database;
using BookingSystem.Api.Models.SystemModels;
using BookingSystem.Api.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace BookingSystem.Tests;

public class LoginServiceTest : IClassFixture<DatabaseFixture>, IAsyncLifetime 
{
    private readonly DatabaseFixture _databaseFixture;

    public LoginServiceTest(DatabaseFixture databaseFixture)
    {
        _databaseFixture = databaseFixture;
    }
    public async Task InitializeAsync()
    {
        await _databaseFixture.ResetDatabaseAsync();
    }
    public Task DisposeAsync()
    {
        return Task.CompletedTask;
    }
    [Fact]
    public async Task LoginServiceLoginTest()
    {
       

        var helper = new HelperUnit();

        var user = helper.CreateNewUser();

        var services = new ServiceCollection();


        var dictionary = new Dictionary<string, string?>()
        {
            {"JWT:KEY", "asdasdalksdalskdjaakjashdahskdwhdahjsdasllawdakldklasdklawd" },
            {"JWT:Issuer", "TestIssuer"},
            {"JWT:Audience", "TestAudience"},
            {"JWT:ExpirationMinutes", "60"},
        };

        var config = new ConfigurationBuilder().AddInMemoryCollection(dictionary).Build();
        var configUserSecret = new ConfigurationBuilder().AddUserSecrets<JWTServiceTest>().Build();
        var connectionString = configUserSecret.GetConnectionString("DefaultConnection");


        services.AddDbContext<AppDbContext>(options =>
            options.UseSqlServer(connectionString))
            .AddIdentityCore<ApplicationUser>()
            .AddRoles<IdentityRole>()
            .AddEntityFrameworkStores<AppDbContext>();

        var serviceProvider = services.BuildServiceProvider();

        var userManager = serviceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        var createUser = await userManager.CreateAsync(user, "TestPassword_123");


        var jwtService = new JWTService(userManager, config);

        var loginService = new LoginService(userManager, jwtService);

        var result = await loginService.LoginAuthentication(user.Email!, "TestPassword_123");

        Assert.True(result.Success);
        Assert.NotNull(result.Token);
        Assert.Equal(Error.none, result.ErrorMessage);

    }

    [Fact]
    public async Task LoginServiceTest_WrongPassword()
    {
        var helper = new HelperUnit();

        var user = helper.CreateNewUser();

        var services = new ServiceCollection();


        var dictionary = new Dictionary<string, string?>()
        {
            {"JWT:KEY", "asdasdalksdalskdjaakjashdahskdwhdahjsdasllawdakldklasdklawd" },
            {"JWT:Issuer", "TestIssuer"},
            {"JWT:Audience", "TestAudience"},
            {"JWT:ExpirationMinutes", "60"},
        };

        var config = new ConfigurationBuilder().AddInMemoryCollection(dictionary).Build();
        var configUserSecret = new ConfigurationBuilder().AddUserSecrets<JWTServiceTest>().Build();
        var connectionString = configUserSecret.GetConnectionString("DefaultConnection");


        services.AddDbContext<AppDbContext>(options =>
            options.UseSqlServer(connectionString))
            .AddIdentityCore<ApplicationUser>()
            .AddRoles<IdentityRole>()
            .AddEntityFrameworkStores<AppDbContext>();

        var serviceProvider = services.BuildServiceProvider();

        var userManager = serviceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        var createUser = await userManager.CreateAsync(user, "Test_123");

        var jwtService = new JWTService(userManager, config);

        var loginService = new LoginService(userManager, jwtService);

        var result = await loginService.LoginAuthentication(user.Email!, "Test_467");

        Assert.False(result.Success);
        Assert.Empty(result.Token);
        Assert.Equal(Error.InvalidCredentials, result.ErrorMessage);
    }
    [Fact]
    public async Task LoginServiceTest_WrongEmail()
    {
        var helper = new HelperUnit();

        var user = helper.CreateNewUser();

        var services = new ServiceCollection();


        var dictionary = new Dictionary<string, string?>()
        {
            {"JWT:KEY", "asdasdalksdalskdjaakjashdahskdwhdahjsdasllawdakldklasdklawd" },
            {"JWT:Issuer", "TestIssuer"},
            {"JWT:Audience", "TestAudience"},
            {"JWT:ExpirationMinutes", "60"},
        };

        var config = new ConfigurationBuilder().AddInMemoryCollection(dictionary).Build();
        var configUserSecret = new ConfigurationBuilder().AddUserSecrets<JWTServiceTest>().Build();
        var connectionString = configUserSecret.GetConnectionString("DefaultConnection");


        services.AddDbContext<AppDbContext>(options =>
            options.UseSqlServer(connectionString))
            .AddIdentityCore<ApplicationUser>()
            .AddRoles<IdentityRole>()
            .AddEntityFrameworkStores<AppDbContext>();

        var serviceProvider = services.BuildServiceProvider();

        var userManager = serviceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        var createUser = await userManager.CreateAsync(user, "Test_123");

        var jwtService = new JWTService(userManager, config);

        var loginService = new LoginService(userManager, jwtService);

        var result = await loginService.LoginAuthentication("Test@gmail.se", "Test_123");

        Assert.False(result.Success);
        Assert.Empty(result.Token);
        Assert.Equal(Error.InvalidCredentials, result.ErrorMessage);
    }
}