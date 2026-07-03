# Visma Blogging API

Production-oriented implementation of the Visma/Yuki fake blogging API challenge using ASP.NET Core MVC, Clean/Hexagonal Architecture, CQRS-style use cases, MongoDB, RabbitMQ, Docker, idempotency, retry policies, global exception handling, MongoDB-backed logging, and a transactional outbox.

The API exposes:

- `POST /post`
- `GET /post/{id}`
- `GET /post/{id}?includeAuthor=true`

The API accepts and returns JSON and XML through ASP.NET Core content negotiation.

## Tutorial: Build, Run, Test, Inspect

This section is the fastest way to prove the whole system works.

### 1. Prerequisites

Install:

- .NET SDK 10
- Docker Desktop
- Git

Check them:

```powershell
dotnet --version
docker --version
git --version
```

### 2. Restore And Build

```powershell
dotnet restore
dotnet build
```

Expected result:

```text
Build succeeded.
```

### 3. Start The Full Docker Stack

```powershell
docker compose up -d --build
```

This starts:

- ASP.NET Core API: `http://localhost:8080`
- MongoDB: `localhost:27017`
- RabbitMQ AMQP: `localhost:5672`
- RabbitMQ Management UI: `http://localhost:15672`

RabbitMQ login:

```text
guest / guest
```

MongoDB is started as a single-node replica set because MongoDB transactions require a replica set, even locally.

### 4. Create A Post With JSON

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
  -Body $body

$created
```

Expected result: a `201 Created` response with fields like:

```json
{
  "id": "...",
  "authorId": "...",
  "title": "First post",
  "description": "A short technical note",
  "content": "The body of the post.",
  "createdAt": "...",
  "author": {
    "id": "...",
    "name": "Ada",
    "surname": "Lovelace"
  }
}
```

### 5. Get The Post Without Author

```powershell
Invoke-RestMethod "http://localhost:8080/post/$($created.id)"
```

Expected behavior: the post is returned and `author` is `null`.

### 6. Get The Post With Author

```powershell
Invoke-RestMethod "http://localhost:8080/post/$($created.id)?includeAuthor=true"
```

Expected behavior: the post is returned with embedded author information.

### 7. Test Idempotency

Use an `Idempotency-Key` when the client may retry a request after a timeout.

```powershell
$key = [guid]::NewGuid().ToString("N")

$first = Invoke-RestMethod `
  -Method Post `
  -Uri http://localhost:8080/post `
  -ContentType "application/json" `
  -Headers @{ "Idempotency-Key" = $key } `
  -Body $body

$second = Invoke-RestMethod `
  -Method Post `
  -Uri http://localhost:8080/post `
  -ContentType "application/json" `
  -Headers @{ "Idempotency-Key" = $key } `
  -Body $body

$first.id
$second.id
```

Expected behavior: both IDs are the same. The second request replays the original result instead of creating a duplicate post.

Rules:

- Same key + same body: replay the original `201 Created` response.
- Same key + different body: return `409 Conflict`.
- No key: every successful `POST /post` creates a new post.

### 8. Create A Post With XML

```powershell
$xml = @"
<CreatePostRequest>
  <Title>XML post</Title>
  <Description>Created from XML</Description>
  <Content>The body came from XML.</Content>
  <Author>
    <Name>Ada</Name>
    <Surname>Lovelace</Surname>
  </Author>
</CreatePostRequest>
"@

Invoke-RestMethod `
  -Method Post `
  -Uri http://localhost:8080/post `
  -ContentType "application/xml" `
  -Headers @{ Accept = "application/xml" } `
  -Body $xml
```

### 9. Inspect MongoDB

Latest post:

```powershell
docker exec visma-blogging-mongodb mongosh visma_blogging --quiet --eval "db.posts.find().sort({CreatedAtUtc:-1}).limit(1).pretty()"
```

Latest log:

```powershell
docker exec visma-blogging-mongodb mongosh visma_blogging --quiet --eval "db.logs.find().sort({TimestampUtc:-1}).limit(1).pretty()"
```

Latest outbox message:

```powershell
docker exec visma-blogging-mongodb mongosh visma_blogging --quiet --eval "db.outbox.find().sort({OccurredAtUtc:-1}).limit(1).pretty()"
```

Expected outbox status after RabbitMQ publishing:

```text
Status: 'published'
Type: 'post.created.v1'
```

### 10. Inspect RabbitMQ

List queues:

```powershell
docker exec visma-blogging-rabbitmq rabbitmqctl list_queues name messages_ready messages_unacknowledged
```

Expected queue:

```text
visma.blogging.post-created
```

You can also open:

```text
http://localhost:15672
```

Then go to:

```text
Queues and Streams -> visma.blogging.post-created
```

### 11. Run Tests

Start MongoDB first because integration and infrastructure tests use it:

```powershell
docker compose up -d mongodb
dotnet test Visma.Blogging.slnx
```

Run coverage:

```powershell
docker compose up -d mongodb
dotnet test Visma.Blogging.slnx --collect:"XPlat Code Coverage"
```

### 12. Stop Everything

Stop containers but keep data:

```powershell
docker compose down
```

Stop containers and remove MongoDB/RabbitMQ volumes:

```powershell
docker compose down -v
```

## Conceptual Model

The challenge describes:

```text
Author
  id
  name
  surname

Post
  id
  author_id
  title
  description
  content
```

One author can have many posts. In this implementation, the `POST /post` request includes author details because the challenge does not define a separate author endpoint.

## Request And Response Shape

JSON create request:

```json
{
  "title": "First post",
  "description": "A short technical note",
  "content": "The body of the post.",
  "author": {
    "name": "Ada",
    "surname": "Lovelace"
  }
}
```

Creation response:

```json
{
  "id": "...",
  "authorId": "...",
  "title": "First post",
  "description": "A short technical note",
  "content": "The body of the post.",
  "createdAt": "...",
  "author": {
    "id": "...",
    "name": "Ada",
    "surname": "Lovelace"
  }
}
```

`GET /post/{id}` omits author details by default.

`GET /post/{id}?includeAuthor=true` includes author details.

## Architecture Overview

The solution follows Clean/Hexagonal Architecture. The core idea is:

> Business rules should not depend on HTTP, MongoDB, RabbitMQ, JSON, XML, Docker, or any external technology.

Project layout:

```text
src/
  Visma.Blogging.Api/
    HTTP adapter: controllers, API contracts, response mapping, middleware, API workflow service

  Visma.Blogging.Application/
    Use cases: commands, queries, handlers, validators, result types, ports, integration event contracts

  Visma.Blogging.Domain/
    Business model: Post, Author, strongly typed IDs, domain validation

  Visma.Blogging.Infrastructure/
    External adapters: MongoDB stores, RabbitMQ publisher, background workers, retry policy, clock, ID generator

tests/
  Visma.Blogging.UnitTests/
  Visma.Blogging.IntegrationTests/
```

Dependency direction:

```text
Api -> Application
Api -> Infrastructure
Infrastructure -> Application
Infrastructure -> Domain
Application -> Domain
Domain -> no solution project dependencies
```

This means the domain is the most protected layer.

## Why Hexagonal Architecture?

Hexagonal Architecture separates the application core from outside tools.

Ports are interfaces owned by the application:

- `IPostCreationStore`
- `IPostQueryStore`
- `ICreatePostIdempotencyStore`
- `IOutboxWriter`

Adapters implement those interfaces:

- `MongoBlogStore`
- `MongoCreatePostIdempotencyStore`
- `MongoOutboxStore`
- `RabbitMqMessagePublisher`

This lets the same use case be called from different entry points:

```text
HTTP controller -> CreatePostCommandHandler
RabbitMQ consumer in the future -> CreatePostCommandHandler
Background job in the future -> CreatePostCommandHandler
```

The application does not care where the command came from.

## Why Controllers Are Thin

`PostsController` is an HTTP adapter. It should not contain business rules, MongoDB code, RabbitMQ code, retry logic, or idempotency orchestration.

The controller only:

- reads route/query/body/header values
- calls an application-facing service or handler
- maps the result to HTTP

Create-post API workflow is handled by `CreatePostEndpointService`, because idempotency is an API workflow concern. The actual business use case is still `CreatePostCommandHandler`.

## CQRS Explained

CQRS means Command Query Responsibility Segregation.

Simple version:

- Commands change state.
- Queries read state.

In this project:

- `CreatePostCommandHandler` creates a post.
- `GetPostByIdQueryHandler` reads a post.

They are separate because writes and reads often evolve differently.

For example:

- The write side needs validation, domain creation, idempotency, transactions, and outbox messages.
- The read side needs efficient lookup and optional author projection.

This keeps each handler focused.

## Domain Layer

The domain project contains the business concepts:

- `Post`
- `Author`
- `PostId`
- `AuthorId`
- `Guard`
- `DomainValidationException`

Domain factories such as `Post.Create(...)` and `Author.Create(...)` protect invariants. That means invalid domain objects are hard to create, even if a future caller bypasses the HTTP API.

Examples of defensive rules:

- title cannot be empty
- content cannot be empty
- author name cannot be empty
- text fields have maximum lengths

## Application Layer

The application layer coordinates use cases.

Important pieces:

- Commands: intent to change the system
- Queries: intent to read from the system
- Handlers: execute commands and queries
- Validators: return field-level validation errors
- Results: represent success/failure without throwing for expected cases
- Ports: interfaces for persistence, idempotency, and outbox behavior

The application layer knows about domain concepts, but it does not know about ASP.NET Core, MongoDB, or RabbitMQ.

## API Layer

The API layer contains:

- MVC controllers
- request DTOs
- response mapping
- XML/JSON content negotiation
- global exception middleware
- API workflow services

DTOs use parameterless constructors and settable properties because XML serializers are stricter than JSON serializers. This keeps JSON and XML support flexible without changing the application use-case models.

## Infrastructure Layer

The infrastructure layer contains adapters for external systems:

- MongoDB post store
- MongoDB idempotency store
- MongoDB outbox store
- RabbitMQ publisher
- outbox publisher background service
- MongoDB logging provider
- retry policy
- system clock
- GUID generator

Infrastructure depends inward on application interfaces and domain types. The application does not depend outward on infrastructure.

## MongoDB Persistence

Posts are stored as MongoDB documents with an embedded author snapshot.

Why embed the author snapshot?

- The API creates a post and author together.
- There is no separate author endpoint in the challenge.
- Reading one post can be done with one MongoDB lookup.
- It avoids multi-document author/post complexity for this scope.

The post document also stores a small event-history-style list. This is not full event sourcing. It is only a lightweight trace of facts recorded inside the document.

## Transactional Outbox

When a post is created, the system must do two things:

```text
1. Save the post.
2. Publish a post-created message.
```

Publishing directly to RabbitMQ inside the request is risky:

```text
Save post succeeds.
RabbitMQ publish fails.
Now the post exists, but no message was published.
```

The outbox pattern fixes this:

```text
MongoDB transaction starts.
  Insert post document.
  Insert outbox message document.
MongoDB transaction commits.

Background worker publishes outbox message to RabbitMQ.
```

If the app crashes after the transaction commits, the outbox message is still stored and can be published later.

If the transaction fails, neither the post nor the outbox message is committed.

That is why this project uses `IPostCreationStore`:

```text
CreatePostCommandHandler
  -> IPostCreationStore.SaveAsync(post, author, outboxMessage)
  -> MongoBlogStore uses MongoDB transaction
```

MongoDB transactions require a replica set, so Docker Compose starts MongoDB with:

```text
mongod --replSet rs0 --bind_ip_all
```

## RabbitMQ: What It Does Here

RabbitMQ is used for outgoing integration events.

That means:

```text
Our API creates a post.
Our API publishes post.created.v1.
Other systems can consume that message.
```

RabbitMQ is not currently used to receive create-post commands from outside systems. That would be a separate consumer adapter and is listed as a future extension.

Why publish an event?

Other systems can react without being inside the API request:

- notification service
- email service
- search indexing service
- analytics service
- audit service

Without RabbitMQ, the API might have to call all those systems directly. That would make `POST /post` slower and more fragile.

With RabbitMQ:

```text
POST /post
  -> save post
  -> save outbox message
  -> return response

Background worker
  -> publish post.created.v1

Other services
  -> consume message when ready
```

RabbitMQ topology:

- exchange: `visma.blogging`
- queue: `visma.blogging.post-created`
- routing key: `post.created`
- dead-letter exchange: `visma.blogging.dlx`

Message payload:

```json
{
  "postId": "00000000-0000-0000-0000-000000000001",
  "authorId": "00000000-0000-0000-0000-000000000002",
  "title": "First post",
  "createdAt": "2026-07-03T00:00:00+00:00"
}
```

## Idempotency

`POST /post` is not naturally idempotent. If a client retries the same request after a timeout, the API could create duplicate posts.

The optional `Idempotency-Key` header solves this.

Flow:

```text
Client sends POST /post with Idempotency-Key.
API hashes the request body.
API reserves the key in MongoDB.
API creates the post.
API stores the response for that key.
Retry with same key and same body returns the original response.
Retry with same key and different body returns 409 Conflict.
```

This is useful for mobile clients, unreliable networks, and payment-style retry behavior.

## Retry Policy

MongoDB operations use a small retry policy for transient failures such as timeouts or retryable MongoDB errors.

Retry logic is in infrastructure because it is a technical adapter concern. The application layer should not know how MongoDB failures are classified.

## Global Exception Handling

Unexpected exceptions are caught by `GlobalExceptionHandlingMiddleware`.

Instead of leaking stack traces, the API returns a consistent ProblemDetails response:

```json
{
  "title": "Unexpected server error",
  "status": 500,
  "traceId": "..."
}
```

The exception is also logged through `ILogger`, and the MongoDB logging provider stores it in the `logs` collection.

## Logging And Traceability

The custom MongoDB logger stores:

- timestamp
- category
- log level
- message
- structured properties
- exception details
- trace id
- span id

This gives basic request traceability. A production version would usually add OpenTelemetry for full distributed tracing.

## JSON And XML Support

The API supports JSON and XML with the same endpoints.

This is why API contracts are DTO-style classes with:

- parameterless constructors
- settable properties

JSON handles immutable records well, but XML serializers often prefer DTO-style objects. The important design choice is that only the API contract shape needs to change for serialization concerns. The application command/query models can stay stable.

## Tests

The suite covers:

- domain validation and normalization
- application command/query success and failure paths
- MongoDB persistence
- duplicate handling
- concurrent writes
- retry policy behavior
- idempotency persistence
- transactional post + outbox writes
- transaction rollback behavior
- outbox claiming and publishing state
- HTTP JSON create/get
- HTTP XML create/get
- invalid input
- missing post
- MongoDB log persistence
- idempotency replay/conflict behavior
- global exception middleware

## Main Design Decisions

### ASP.NET Core MVC Controllers

Controllers were chosen instead of Minimal APIs because they make the HTTP adapter explicit and familiar for enterprise .NET projects.

### Clean/Hexagonal Architecture

Chosen to keep business rules independent from frameworks and infrastructure.

### CQRS-Style Handlers

Chosen to separate write behavior from read behavior without adding unnecessary framework complexity.

### MongoDB

Chosen as a Docker-friendly persistence adapter for the challenge. It stores posts, idempotency records, logs, and outbox records.

### RabbitMQ

Chosen to demonstrate production-style asynchronous integration. The current implementation publishes outgoing events after post creation.

### Transactional Outbox

Chosen because it prevents the saved-post-without-message problem.

### Idempotency

Chosen because clients often retry POST requests after timeouts. Idempotency prevents accidental duplicate posts.

### Global Exception Middleware

Chosen to keep error responses consistent and avoid leaking internal exception details.

### MongoDB Logging Provider

Chosen so logs can be inspected from the same Dockerized environment without requiring an external logging platform.

## Current Limitations And Future Improvements

Good future additions:

- health check endpoints for API, MongoDB, and RabbitMQ
- OpenTelemetry tracing
- authentication and authorization
- TTL indexes for old logs, idempotency records, and outbox records
- a RabbitMQ consumer adapter for incoming create-post commands
- richer read models behind `IPostQueryStore`
- CI pipeline running build, tests, and coverage
- production secrets management instead of plain Docker Compose credentials

## Interview Summary

Short version:

> This is a Clean Architecture ASP.NET Core API for a fake blogging system. The API layer exposes MVC controllers and supports JSON/XML. The application layer contains CQRS-style handlers and ports. The domain layer protects business invariants. Infrastructure implements MongoDB persistence, idempotency, transactional outbox, RabbitMQ publishing, retry policies, and MongoDB-backed logging. Post creation commits the post and outbox event atomically in MongoDB, then a background worker publishes `post.created.v1` to RabbitMQ. This keeps HTTP fast, avoids duplicate posts on retries, and prevents losing integration messages when RabbitMQ is temporarily unavailable.

## Simpler Alternative

A minimal challenge-only version could put both endpoints, validation, and dictionaries directly in one controller or `Program.cs`.

That would be faster to write, but it would couple HTTP, validation, persistence, messaging, and business rules.

This version is intentionally more structured because the challenge values architecture, testability, CQRS, Docker, and future flexibility.
