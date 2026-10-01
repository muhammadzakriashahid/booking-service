using BookingService.Data;
using BookingService.DTOs;
using BookingService.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BookingService.Controllers;

[ApiController]
[Route("v1/bookings")]
public class BookingsController : ControllerBase
{
    private readonly BookingDbContext dbContext;

    public BookingsController(BookingDbContext dbContext)
    {
        this.dbContext = dbContext;
    }

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

        var booking = new Booking
        {
            ShowtimeId = request.ShowtimeId,
            CustomerEmail = customerEmail,
            Status = "Pending",
            TotalPrice = 0m,
            Seats = request.Seats
                .Select(seatLabel => new BookingSeat
                {
                    ShowtimeId = request.ShowtimeId,
                    SeatLabel = seatLabel
                })
                .ToList()
        };

        dbContext.Bookings.Add(booking);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
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