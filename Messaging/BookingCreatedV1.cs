namespace BookingService.Messaging;

public sealed record BookingCreatedData(
    Guid BookingId,
    Guid ShowtimeId,
    IReadOnlyList<string> Seats,
    decimal TotalPrice);

public sealed record BookingCreatedV1(
    Guid EventId,
    string EventType,
    int SchemaVersion,
    DateTimeOffset OccurredAtUtc,
    string CorrelationId,
    string Producer,
    BookingCreatedData Data);