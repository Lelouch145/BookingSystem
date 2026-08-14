using Microsoft.EntityFrameworkCore;
using BookingSystem.Api.Models.SystemModels;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;

namespace BookingSystem.Api.Database;

public class AppDbContext : IdentityDbContext<ApplicationUser>
{

    public AppDbContext(DbContextOptions<AppDbContext> options)
        : base(options)
    {
        
    }
    public DbSet<Court> Courts { get; set; }

}