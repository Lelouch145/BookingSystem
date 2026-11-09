# BookingSystem.Api

An ASP.NET Core API for booking padel courts, built to explore backend development beyond basic CRUD: database integrity, concurrent requests, temporary reservations and coordination between SQL Server and Redis.

> **Status: actively under development.** Core booking logic and Redis hold services are implemented, with automated tests in the repository. Payment integration is the next development phase. Stripe setup has started; a complete payment flow is not implemented.

## Why I am building this

I am building this project to strengthen my C#/.NET skills and prepare for a LIA placement (Learning in Work). It gives me a practical setting for learning how to translate business rules into API behavior, protect data when multiple requests arrive together, and test failure scenarios.

The project is a learning portfolio with ongoing improvements, rather than a finished production service.

## Implemented functionality

- **Accounts and access:** registration and login using ASP.NET Core Identity, JWT bearer authentication, and an Admin role for court administration. Roles and a configured administrator account are seeded at startup.
- **Court management:** create, list, update and disable courts. Regular users see active courts; administrators can see all courts.
- **Booking rules:** 60- or 90-minute bookings built from 30-minute slots, with validation of start times, opening hours and past dates.
- **Booking management:** create and list bookings, cancel bookings and reschedule their time. Services check ownership and apply a 24-hour restriction to cancellation and rescheduling by regular users. Administrators have broader access.
- **Availability:** generate available start times for a court and date.
- **Database integrity:** a unique index on `(CourtId, SlotStart)` prevents two persisted bookings from occupying the same slot. A check constraint enforces 30-minute slot boundaries. Booking `RowVersion` supports optimistic concurrency checks.
- **Transactions:** booking creation and rescheduling use SQL transactions. The hold-confirmation service distinguishes rollback from an uncertain commit outcome and can check persisted bookings by `HoldId` using a fresh context.
- **Redis holds:** service methods create, switch, validate and release temporary holds. Lua scripts make related Redis checks and updates atomic. Finalization uses lock tokens with complete and abort operations.
- **Caching:** booking and availability services contain Redis cache reads, writes and invalidation, including handling of Redis connection and timeout errors. See the configuration limitation below.
- **Background processing:** a hosted service updates completed bookings.

### API surface versus service implementation

The API currently exposes registration, login, court management, availability, booking management and `POST /CreateHold`. Swagger UI describes the exposed routes in the Development environment.

Hold switching, release, validation and `ConfirmBookingHold` are implemented at the service layer and exercised by tests, but do not currently have dedicated API endpoints. The direct booking endpoint creates a confirmed booking without payment. A complete hold-to-payment-to-confirmation API workflow remains to be integrated.

## Technical approach

SQL Server is the source of truth for persisted bookings. Each booking occupies two or three 30-minute slot rows. Application validation gives useful feedback, while database constraints provide protection when requests race.

Redis stores temporary holds and cached data. Lua scripts coordinate hold keys within Redis; they do not create a shared transaction with SQL Server. The finalization service therefore handles SQL outcomes and Redis cleanup separately. A unique booking `HoldId` index provides an additional integrity safeguard.

## Technology stack

| Area | Technologies |
| --- | --- |
| API | C#, .NET 8, ASP.NET Core Minimal APIs |
| Persistence | SQL Server, Entity Framework Core 8, EF migrations |
| Authentication | ASP.NET Core Identity, JWT bearer tokens |
| Holds and caching | Redis, StackExchange.Redis, Lua, `IDistributedCache` |
| API exploration | Swagger / OpenAPI via Swashbuckle |
| Tests | xUnit, Moq, Respawn, coverlet |
| Local infrastructure | Docker Compose: SQL Server 2022 and Redis 8 |
| Payment preparation | Stripe.net SDK and a webhook placeholder |

## Run locally

Run the commands below from the repository root. You need the .NET 8 SDK, Docker with Compose, and the EF Core CLI matching EF Core 8. The SQL Server container targets `linux/amd64`, so the Docker host must support that platform.

### 1. Start SQL Server and Redis

The Compose file expects an external SQL data volume and a SQL administrator password supplied through your environment:

```sh
export MSSQL_SA_PASSWORD='<choose-a-strong-local-password>'
docker volume create bookingsystem-sql-data
docker compose up -d
```

Wait for SQL Server to be ready before applying migrations.

### 2. Configure the API

Use .NET User Secrets for local credentials. Replace all example values before running:

```sh
dotnet user-secrets set 'ConnectionStrings:DefaultConnection' 'Server=localhost,1433;Database=BookingSystem;User Id=sa;Password=<your-sql-password>;TrustServerCertificate=True' --project BookingSystem.Api
dotnet user-secrets set 'ConnectionStrings:redis' 'localhost:6379' --project BookingSystem.Api
dotnet user-secrets set 'JWT:KEY' '<random-signing-key-at-least-32-bytes>' --project BookingSystem.Api
dotnet user-secrets set 'JWT:Issuer' 'BookingSystem.Api' --project BookingSystem.Api
dotnet user-secrets set 'JWT:Audience' 'BookingSystem.Local' --project BookingSystem.Api
dotnet user-secrets set 'JWT:ExpirationMinutes' '60' --project BookingSystem.Api
dotnet user-secrets set 'Admin:email' 'admin@example.test' --project BookingSystem.Api
dotnet user-secrets set 'Admin:UserName' 'localadmin' --project BookingSystem.Api
dotnet user-secrets set 'Admin:Password' '<strong-password-meeting-Identity-rules>' --project BookingSystem.Api
dotnet user-secrets set 'Stripe:SecretKey' '<your-stripe-test-secret-key>' --project BookingSystem.Api
```

Startup currently requires `Stripe:SecretKey`, even though payment processing is unfinished. Keep credentials out of committed configuration files.

### 3. Apply migrations and start the API

```sh
dotnet restore BookingSystem.sln
dotnet ef database update --project BookingSystem.Api --startup-project BookingSystem.Api
dotnet dev-certs https --trust
dotnet run --project BookingSystem.Api --launch-profile https
```

Open [Swagger UI](https://localhost:7030/swagger). Log in with the configured administrator to manage courts, or register a regular user. Use the returned JWT through Swagger's **Authorize** button to call authenticated endpoints. Stop the API with **Ctrl+C**.

### Current local-run limitation

In `Program.cs`, the `AddStackExchangeRedisCache` registration currently assigns the literal string `"redisConnectionString"` to `options.Configuration`, instead of the configured variable `redisConnectionString`. This can break operations using `IDistributedCache`; the separate connection multiplexer used by holds reads the configured connection string correctly. Correcting that registration is a pending code fix.

These instructions are derived from the source; a fresh local startup has not been verified as part of this documentation change.

## Run tests

The suite includes service tests and integration tests using real SQL Server and Redis. Test scenarios cover authentication, court and booking rules, availability caching, overlapping booking attempts, optimistic concurrency, hold expiration, concurrent hold operations, finalization and SQL commit failure scenarios.

**Use a dedicated disposable test database.** The database fixture applies migrations and uses Respawn to reset database tables. Redis integration helpers connect to `localhost:6379`; use a dedicated local Redis instance without valuable data.

The test project has its own User Secrets configuration. Configure its connection string separately from the API:

```sh
dotnet user-secrets set 'ConnectionStrings:DefaultConnection' 'Server=localhost,1433;Database=BookingSystemTests;User Id=sa;Password=<your-sql-password>;TrustServerCertificate=True' --project BookingSystem.Tests
dotnet test BookingSystem.sln
```

Keep SQL Server and Redis running while executing the suite. Test presence does not imply that the current checkout has passed: no test run or coverage result is claimed by this README.

## What I am learning

- Translating booking rules into validation, service logic and database constraints.
- Understanding why an overlap check alone cannot prevent a race condition.
- Choosing between SQL transactions, optimistic concurrency and atomic Redis operations.
- Coordinating temporary reservations and durable bookings when systems fail independently.
- Testing concurrent operations and failures before, during and after a database commit.
- Managing authentication, authorization, dependency injection and local configuration.
- Separating payment infrastructure setup from a complete business workflow.

## Development plan

| Status | Milestone | Scope |
| --- | --- | --- |
| Implemented | Core booking foundation | Court management, accounts, JWT, availability, booking creation, cancellation and rescheduling |
| Implemented | Database integrity | 30-minute slot constraints, unique indexes, transactions and booking row versions |
| Implemented | Hold service and tests | Redis/Lua hold lifecycle, finalization and tests for concurrency and failure scenarios; API exposure is partial |
| In progress | Payment groundwork | Payment models and migrations, Stripe client configuration and webhook connectivity placeholder |
| Planned — next phase | Payment integration | Define `IPaymentProvider`, implement `StripePaymentProvider` and `PaymentService`, and connect payments to booking state |
| Planned | Payment reliability | Verify webhook signatures, process events idempotently, and test successful, failed and repeated payment events |
| Planned | Complete booking workflow | Expose the required hold operations and connect hold confirmation to the payment flow |
| Planned | Local-run and quality improvements | Fix Redis cache registration, verify clean setup instructions, and extend tests as the payment flow develops |

The current `POST /api/payments/webhook` endpoint only logs receipt and returns HTTP 200. It does not validate signatures, process Stripe events or update bookings. Payment models and an installed SDK do not yet constitute working payment integration.

Milestones are tracked by completed behavior and tests; no fixed V1 release date is promised.

## Further development plans

The roadmap below extends the milestones above. These are planned improvements, with payment integration remaining the next development step.

### Phase 1 — Complete the booking workflow and strengthen the API

- **Payment flow:** connect holds, payment status and booking confirmation, including handling failed or interrupted payments and repeated webhook events.
- **Notifications and booking events:** introduce events such as `BookingCreated`, `BookingCancelled` and `BookingRescheduled`, with asynchronous booking confirmations and cancellation notifications.
- **Transactional outbox:** save booking changes and their notification events in the same SQL transaction. Process pending events through a background service, with retries and idempotent handling so delivery can resume after an application failure.
- **Rate limiting and resilience:** limit requests per client and concurrent requests, handle timeouts and overload, and improve error handling when dependencies fail.
- **Load and stress testing:** use a tool such as k6 to evaluate booking behavior under load and identify performance bottlenecks.
- **HTTP API integration tests:** extend the existing service and database tests with requests through the API, covering authentication, authorization, booking and payment flows, error responses, and interaction with SQL Server and Redis.

### Phase 2 — Advanced database development and change history

- **Database performance:** inspect SQL generated by EF Core and SQL Server execution plans, review indexing, and optimize slow queries. Use larger test datasets to compare performance before and after changes.
- **Transactions, concurrency and integrity:** build on the existing transactions and constraints by exploring isolation levels, locking, blocking and deadlocks. Extend tests for database constraints and evaluate how transaction choices affect concurrent booking requests.
- **Booking audit history:** introduce a dedicated `BookingAuditLog` recording who created, changed or cancelled a booking, when it happened, and relevant previous and new values. Keep audit entries in the same transaction as the booking change so rolled-back operations do not leave misleading history.

## Repository structure

```text
BookingSystem.Api/
  Program.cs                 API routes and dependency registration
  Database/                  EF Core context and database constraints
  Models/                    Entities, requests and response models
  Services/                  Booking, identity, availability and background logic
    Caching/Scripts/         Redis hold Lua scripts
  Migrations/                EF Core migrations
BookingSystem.Tests/         Service, database, concurrency and Redis tests
compose.yml                  Local SQL Server and Redis services
```
