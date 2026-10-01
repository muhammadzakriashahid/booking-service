namespace BookingService.Models;

public class Booking
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ShowtimeId { get; set; }
    public string CustomerEmail { get; set; } = string.Empty;
    public string Status { get; set; } = "Pending"; // Pending, Confirmed, Cancelled
    public decimal TotalPrice { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public List<BookingSeat> Seats { get; set; } = new();
}