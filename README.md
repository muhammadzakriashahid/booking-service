# Booking-Service

## Dependecies
### DB (PostgreSQL) (docker needs to be running for this to work)
- run the `docker-compose.infra.yml` file in the `cinema-deploy` repository
### Showtime-Service (cannot create a booking without it)
- run `dotnet run` in the showtime-service repository → c# project location
## Guide
### Application
- run `dotnet run` in the booking-service directory
> this won't work unless dependecies (mainly db) are up and running
***
### References
- http://localhost:5109/scalar/v1 → Scalar UI
- http://localhost:5109/openapi/v1.json → OpenAPI JSON

> extra-info \
> db → `Npgsql.EntityFrameworkCore.PostgreSQL` \
> db interface (inp/out) → `Microsoft.EntityFrameworkCore.Design` \
> open api gen → `Microsoft.Extensions.ApiDescription.Server`