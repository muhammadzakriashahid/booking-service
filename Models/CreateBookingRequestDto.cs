namespace BookingService.DTOs;

public sealed record CreateBookingRequestDto(
    Guid ShowtimeId,
    List<string> Seats
);