using BookingService.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BookingService.Controllers;

[ApiController]
[Route("health")]
public class HealthController : ControllerBase
{
    private readonly BookingDbContext dbContext;

    public HealthController(BookingDbContext dbContext)
    {
        this.dbContext = dbContext;
    }

    [HttpGet("ready")]
    public async Task<IActionResult> Ready(
        CancellationToken cancellationToken)
    {
        var databaseIsAvailable =
            await dbContext.Database.CanConnectAsync(cancellationToken);

        return databaseIsAvailable
            ? Ok(new { status = "ready" })
            : StatusCode(503, new { status = "unavailable" });
    }
}