# Visma Blogging API

Backend technical test implementation for a blogging system.

The API lets clients create and read blog posts. A post is created together with its author details, and the read endpoint can optionally include the author in the response.

## Features

- Create a blog post with author information.
- Read a blog post by ID.
- Optionally include author information in the read response.
- Accept JSON and XML request bodies through ASP.NET Core content negotiation.
- Return JSON or XML responses based on the client `Accept` header.
- Protect `POST /post` with idempotency keys for safe retries.
- Persist posts, logs, idempotency records, and outbox messages in MongoDB.
- Publish post-created integration events to RabbitMQ through a transactional outbox.
- Apply retry handling around transient MongoDB failures.
- Use global exception handling for consistent API error responses.

## Technology Stack

- C# / .NET 10
- ASP.NET Core MVC Controllers
- MongoDB
- RabbitMQ
- Docker Compose
- xUnit
- Coverlet

## Architecture

The solution follows Clean Architecture with a hexagonal style. The application core is isolated from delivery and infrastructure details, while adapters handle HTTP, MongoDB, RabbitMQ, and logging.

```text
src/
  Visma.Blogging.Api              HTTP controllers, contracts, middleware, composition root
  Visma.Blogging.Application      Commands, queries, handlers, validators, ports
  Visma.Blogging.Domain           Entities, value objects, domain rules
  Visma.Blogging.Infrastructure   MongoDB, RabbitMQ, logging, retries, background workers

tests/
  Visma.Blogging.UnitTests
  Visma.Blogging.IntegrationTests
```

The API project depends on the application layer and wires the system together. The application layer depends on domain concepts and interfaces. The infrastructure layer implements those interfaces. This keeps business rules independent from ASP.NET Core, MongoDB, and RabbitMQ.

Post creation uses a transactional outbox. The post and the outgoing `post.created.v1` message are committed in the same MongoDB transaction. A background worker later publishes the outbox message to RabbitMQ and marks it as published.

```text
POST /post
  -> PostsController
  -> CreatePostEndpointService
  -> CreatePostHandler
  -> MongoDB transaction
       -> save post
       -> save outbox message
  -> OutboxPublisherBackgroundService
  -> RabbitMQ
```

This avoids the common failure where a post is saved successfully but the integration event is lost before another system can consume it.

## Getting Started

### Prerequisites

- .NET SDK 10
- Docker Desktop
- Git

### Build

```powershell
dotnet restore
dotnet build
```

### Run With Docker

```powershell
docker compose up -d --build
```

The Docker stack starts:

- API: `http://localhost:8080`
- MongoDB: `localhost:27017`
- RabbitMQ Management UI: `http://localhost:15672`
- RabbitMQ credentials: `guest / guest`

MongoDB is started as a single-node replica set because MongoDB transactions require replica set support.

### Stop The Stack

```powershell
docker compose down
```

To remove local database and queue data as well:

```powershell
docker compose down -v
```

## API Usage

### Create A Post

```powershell
$body = @{
  title = "First post"
  description = "A short technical note"
  content = "The body of the post."
  author = @{
    name = "Ada"
    surname = "Lovelace"
  }
} | ConvertTo-Json -Depth 4

$created = Invoke-RestMethod `
  -Method Post `
  -Uri http://localhost:8080/post `
  -ContentType "application/json" `
  -Headers @{ "Idempotency-Key" = [guid]::NewGuid().ToString("N") } `
  -Body $body

$created
```

### Get A Post

```powershell
Invoke-RestMethod "http://localhost:8080/post/$($created.id)"
```

### Get A Post With Author

```powershell
Invoke-RestMethod "http://localhost:8080/post/$($created.id)?includeAuthor=true"
```

### Request XML

```powershell
Invoke-RestMethod `
  -Method Get `
  -Uri "http://localhost:8080/post/$($created.id)?includeAuthor=true" `
  -Headers @{ Accept = "application/xml" }
```

## Configuration

The main configuration sections are:

- `Mongo`: connection string, database name, collection names, retry settings.
- `RabbitMq`: host, credentials, exchange, queue, routing key, publisher settings.

Docker Compose provides production-like local values through environment variables. Local development defaults are also available in `src/Visma.Blogging.Api/appsettings.Development.json`.

## Tests

Start MongoDB before running the tests that exercise persistence:

```powershell
docker compose up -d mongodb
dotnet test Visma.Blogging.slnx
```

Run coverage:

```powershell
dotnet test Visma.Blogging.slnx --collect:"XPlat Code Coverage"
```

The test suite covers domain rules, application handlers, controller behavior, JSON/XML content negotiation, MongoDB persistence, transactional outbox behavior, idempotency, global exception handling, and MongoDB-backed logging.

## Local Operations

Inspect the latest post:

```powershell
docker exec visma-blogging-mongodb mongosh visma_blogging --quiet --eval "db.posts.find().sort({CreatedAtUtc:-1}).limit(1).pretty()"
```

Inspect the latest outbox message:

```powershell
docker exec visma-blogging-mongodb mongosh visma_blogging --quiet --eval "db.outbox.find().sort({OccurredAtUtc:-1}).limit(1).pretty()"
```

Inspect the RabbitMQ queue:

```powershell
docker exec visma-blogging-rabbitmq rabbitmqctl list_queues name messages_ready messages_unacknowledged
```

The post-created queue is:

```text
visma.blogging.post-created
```
