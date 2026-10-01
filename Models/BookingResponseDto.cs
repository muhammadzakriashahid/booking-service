namespace BookingService.DTOs;

public record BookingResponseDto(
    Guid Id,
    Guid ShowtimeId,
    string CustomerEmail,
    string Status,
    decimal TotalPrice,
    DateTime CreatedAtUtc,
    List<string> Seats
);