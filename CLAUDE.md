# Money Transfer Microservices

Portfolio project: event-driven money transfer system.

## Architecture
- TransferApi: ASP.NET Core Web API + PostgreSQL. Creates transfers, publishes TransferRequested. Consumes TransferApproved / TransferRejected, updates the status, publishes TransferStatusChanged.
- RiskWorker: consumes TransferRequested, publishes TransferApproved / TransferRejected.
- NotificationWorker: consumes TransferStatusChanged, stores notifications in its own database.
- Contracts: shared class library with event message records only.
- Services communicate only through RabbitMQ. Never share databases between services.

## Tech
- .NET 8, C#, EF Core code-first with Npgsql, snake_case naming (EFCore.NamingConventions)
- MassTransit v8 (do not upgrade to v9, it requires a commercial licence) with RabbitMQ
- Money is decimal / numeric(18,2). All dates are UTC.
- xUnit + Testcontainers for integration tests

## Working rules
- Work in small steps. Explain the plan before making large changes.
- Explain new concepts briefly; I'm learning PostgreSQL, RabbitMQ and Kubernetes.
- Don't add packages or features I didn't ask for.
- After changes, make sure the solution builds and tests pass.