using BookingSystem.Api.Database;
using Microsoft.Extensions.Configuration;
using Microsoft.EntityFrameworkCore;

using BookingSystem.Api.Models.SystemModels;
using BookingSystem.Api.Services;

namespace BookingSystem.Tests;

public class DbContextHelper
{
    public AppDbContext DbContextHellper()
    {
        var builder = new ConfigurationBuilder().AddUserSecrets<BackGroundTest>().Build();
        var connectionString = builder.GetConnectionString("DefaultConnection");
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(connectionString).Options;
        var dbContext = new AppDbContext(options);
        return dbContext;
    }
}