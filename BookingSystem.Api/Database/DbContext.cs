using Microsoft.EntityFrameworkCore;
using BookingSystem.Api.Models;

namespace BookingSystem.Api.Database;

public class Database : DbContext
{

    public Database(DbContextOptions<Database> options)
        : base(options)
    {
    }
    public DbSet<Court> Courts { get; set; }
}