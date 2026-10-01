namespace BookingService.Models;

public class BookingSeat
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid BookingId { get; set; }
    public Guid ShowtimeId { get; set; }
    public string SeatLabel { get; set; } = string.Empty; // e.g. "A1", "B5"
    public bool IsActive { get; set; } = true;

    public Booking? Booking { get; set; }
}