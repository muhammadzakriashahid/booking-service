namespace BookingService.Messaging;

public sealed record PaymentCompletedV1(
    Guid EventId,
    string EventType,
    int SchemaVersion,
    DateTimeOffset OccurredAtUtc,
    string CorrelationId,
    string Producer,
    PaymentCompletedData? Data);

public sealed record PaymentCompletedData(
    Guid BookingId,
    Guid PaymentId,
    decimal Amount);