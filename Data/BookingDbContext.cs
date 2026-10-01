using BookingService.Models;
using Microsoft.EntityFrameworkCore;

namespace BookingService.Data;

public class BookingDbContext : DbContext
{
    public BookingDbContext(DbContextOptions<BookingDbContext> options)
        : base(options)
    {
    }

    public DbSet<Booking> Bookings => Set<Booking>();
    public DbSet<BookingSeat> Seats => Set<BookingSeat>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Booking>(entity =>
        {
            entity.HasKey(booking => booking.Id);

            entity.Property(booking => booking.CustomerEmail)
                .IsRequired()
                .HasMaxLength(320);

            entity.Property(booking => booking.Status)
                .IsRequired()
                .HasMaxLength(30);

            entity.Property(booking => booking.TotalPrice)
                .HasPrecision(10, 2);

            entity.HasMany(booking => booking.Seats)
                .WithOne(seat => seat.Booking)
                .HasForeignKey(seat => seat.BookingId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<BookingSeat>(entity =>
        {
            entity.HasKey(seat => seat.Id);

            entity.Property(seat => seat.SeatLabel)
                .IsRequired()
                .HasMaxLength(10);

            entity.HasIndex(seat => new
            {
                seat.ShowtimeId,
                seat.SeatLabel
            })
            .IsUnique()
            .HasFilter("\"IsActive\" = TRUE");
        });
    }
}