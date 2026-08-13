using Microsoft.EntityFrameworkCore;
using BookingSystem.Api.Models.SystemModels;

namespace BookingSystem.Api.Database;

public class AppDbContext : DbContext
{

    public AppDbContext(DbContextOptions<AppDbContext> options)
        : base(options)
    {
    }
    public DbSet<Court> Courts { get; set; }
}