# Booking-Service

## Guide
### DB (PostgreSQL)
- run `docker compose up -d` in the root directory
### Application
- run `dotnet run` in the booking-service directory
***
### References
- http://localhost:5109/scalar/v1 → Scalar UI
- http://localhost:5109/openapi/v1.json → OpenAPI JSON

> extra-info \
> db → `Npgsql.EntityFrameworkCore.PostgreSQL` \
> db interface (inp/out) → `Microsoft.EntityFrameworkCore.Design` \
> open api gen → `Microsoft.Extensions.ApiDescription.Server`