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

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.Entity<BookingSlot>()
        .HasIndex(x => new { x.CourtId, x.SlotStart })
        .IsUnique();

        builder.Entity<BookingSlot>()
        .ToTable(t => t.HasCheckConstraint(
            "CK_BookingSlot_SlotStart_30Minutes",
            "DATEPART(MINUTE, [SlotStart]) IN (0, 30) AND " +
            "DATEPART(SECOND, [SlotStart]) = 0 AND " +
            "DATEPART(NANOSECOND, [SlotStart]) = 0"
        ));

        builder.Entity<BookingSlot>()
        .HasOne(x => x.Court)
        .WithMany()
        .HasForeignKey(x => x.CourtId)
        .OnDelete(DeleteBehavior.NoAction);

        builder.Entity<Booking>()
        .Property(x => x.RowVersion)
        .IsRowVersion();

        builder.Entity<Court>()
        .HasIndex(x => x.CourtName)
        .IsUnique();
    }
    public DbSet<Court> Courts { get; set; }
    public DbSet<Booking> Bookings { get; set; }
    public DbSet<BookingSlot> BookingSlots { get; set; }

}