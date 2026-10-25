using BookingService.Data;
using BookingService.DTOs;
using BookingService.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using BookingService.Clients;
using System.Text.Json;
using BookingService.Messaging;
using Npgsql;

namespace BookingService.Controllers;

[ApiController]
[Route("v1/bookings")]
public class BookingsController : ControllerBase
{
    private readonly BookingDbContext dbContext;
    private readonly IShowtimeApiClient showtimeClient;

    public BookingsController(
        BookingDbContext dbContext,
        IShowtimeApiClient showtimeClient)
    {
        this.dbContext = dbContext;
        this.showtimeClient = showtimeClient;
    }

    private static readonly JsonSerializerOptions EventJsonOptions = new(JsonSerializerDefaults.Web);

    [HttpGet]
    public async Task<ActionResult<List<BookingResponseDto>>> GetBookings(
        CancellationToken cancellationToken)
    {
        var bookings = await dbContext.Bookings
            .Include(booking => booking.Seats)
            .OrderByDescending(booking => booking.CreatedAtUtc)
            .Select(booking => new BookingResponseDto(
                booking.Id,
                booking.ShowtimeId,
                booking.CustomerEmail,
                booking.Status,
                booking.TotalPrice,
                booking.CreatedAtUtc,
                booking.Seats
                    .Select(seat => seat.SeatLabel)
                    .ToList()
            ))
            .ToListAsync(cancellationToken);

        return Ok(bookings);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<BookingResponseDto>> GetBooking(
        Guid id,
        CancellationToken cancellationToken)
    {
        var booking = await dbContext.Bookings
            .Include(item => item.Seats)
            .FirstOrDefaultAsync(item => item.Id == id, cancellationToken);

        if (booking is null)
        {
            return NotFound();
        }

        return Ok(new BookingResponseDto(
            booking.Id,
            booking.ShowtimeId,
            booking.CustomerEmail,
            booking.Status,
            booking.TotalPrice,
            booking.CreatedAtUtc,
            booking.Seats
                .Select(seat => seat.SeatLabel)
                .ToList()
        ));
    }

    [HttpPost]
    public async Task<ActionResult<BookingResponseDto>> CreateBooking(
        [FromBody] CreateBookingRequestDto request,
        CancellationToken cancellationToken)
    {
        if (request.ShowtimeId == Guid.Empty)
        {
            return BadRequest("ShowtimeId is required.");
        }

        if (request.Seats is null || request.Seats.Count == 0)
        {
            return BadRequest("At least one seat is required.");
        }

        if (request.Seats.Distinct(StringComparer.OrdinalIgnoreCase).Count()
            != request.Seats.Count)
        {
            return BadRequest("A seat cannot be selected more than once.");
        }

        var customerEmail = Request.Headers["X-Customer-Email"].FirstOrDefault();

        if (string.IsNullOrWhiteSpace(customerEmail))
        {
            return BadRequest("X-Customer-Email header is required for now.");
        }

        ShowtimeDetailDto showtime;

        try
        {
            showtime = await showtimeClient.GetShowtimeByIdAsync(
                request.ShowtimeId,
                cancellationToken);
        }
        catch (ApiException exception)
            when (exception.StatusCode == StatusCodes.Status404NotFound)
        {
            return NotFound("The requested showtime does not exist.");
        }
        catch (ApiException)
        {
            return StatusCode(
                StatusCodes.Status502BadGateway,
                "The showtime service returned an error.");
        }
        catch (HttpRequestException)
        {
            return StatusCode(
                StatusCodes.Status503ServiceUnavailable,
                "The showtime service is unavailable.");
        }

        if (showtime.SeatLabels is null)
        {
            return StatusCode(
                StatusCodes.Status502BadGateway,
                "The showtime service did not return seat labels.");
        }

        foreach (var seat in request.Seats)
        {
            if (!showtime.SeatLabels.Contains(seat, StringComparer.OrdinalIgnoreCase))
            {
                return BadRequest($"Seat '{seat}' does not exist for this showtime.");
            }
        }

        var booking = new Booking
        {
            ShowtimeId = request.ShowtimeId,
            CustomerEmail = customerEmail,
            Status = "Pending",
            TotalPrice = (decimal)showtime.Price * request.Seats.Count,
            Seats = request.Seats
                .Select(seatLabel => new BookingSeat
                {
                    ShowtimeId = request.ShowtimeId,
                    SeatLabel = seatLabel
                })
                .ToList()
        };

        var eventId = Guid.NewGuid();

        var bookingCreatedEvent = new BookingCreatedV1(
            EventId: eventId,
            EventType: "booking.created",
            SchemaVersion: 1,
            OccurredAtUtc: DateTimeOffset.UtcNow,
            CorrelationId:
                Request.Headers["X-Correlation-ID"].FirstOrDefault()
                ?? Guid.NewGuid().ToString("N"),
            Producer: "booking-service",
            Data: new BookingCreatedData(
                BookingId: booking.Id,
                ShowtimeId: booking.ShowtimeId,
                Seats: booking.Seats
                    .Select(seat => seat.SeatLabel)
                    .ToList(),
                TotalPrice: booking.TotalPrice));

        var outboxMessage = new OutboxMessage
        {
            Id = eventId,
            MessageType = "booking.created.v1",
            Payload = JsonSerializer.Serialize(
                bookingCreatedEvent,
                EventJsonOptions),
            OccurredAtUtc = bookingCreatedEvent.OccurredAtUtc
        };

        dbContext.Bookings.Add(booking);
        dbContext.OutboxMessages.Add(outboxMessage);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (
            exception.InnerException is PostgresException
            {
                SqlState: PostgresErrorCodes.UniqueViolation
            })
        {
            return Conflict("One or more selected seats are already booked.");
        }

        var response = new BookingResponseDto(
            booking.Id,
            booking.ShowtimeId,
            booking.CustomerEmail,
            booking.Status,
            booking.TotalPrice,
            booking.CreatedAtUtc,
            booking.Seats
                .Select(seat => seat.SeatLabel)
                .ToList()
        );

        return CreatedAtAction(
            nameof(GetBooking),
            new { id = booking.Id },
            response
        );
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> CancelBooking(
        Guid id,
        CancellationToken cancellationToken)
    {
        var booking = await dbContext.Bookings
            .Include(item => item.Seats)
            .FirstOrDefaultAsync(item => item.Id == id, cancellationToken);

        if (booking is null)
        {
            return NotFound();
        }

        booking.Status = "Cancelled";

        foreach (var seat in booking.Seats)
        {
            seat.IsActive = false;
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        return NoContent();
    }
}