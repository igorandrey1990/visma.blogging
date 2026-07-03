using Microsoft.Extensions.Logging.Abstractions;
using Visma.Blogging.Api;
using Visma.Blogging.Application.Abstractions;
using Visma.Blogging.Application.Blogging;
using Visma.Blogging.Application.Messaging;
using Visma.Blogging.Domain;

namespace Visma.Blogging.IntegrationTests;

/// <summary>
/// Tests the API workflow service below the controller.
/// The service owns API-specific idempotency orchestration, so these tests keep that
/// behavior focused without sending real HTTP requests for every edge case.
/// </summary>
public sealed class CreatePostEndpointServiceTests
{
    [Fact]
    public async Task CreateAsync_without_idempotency_key_creates_post()
    {
        // Without an idempotency key, the service behaves like a normal POST:
        // execute the create command and return a Created endpoint result.
        var service = CreateService(new MemoryIdempotencyStore());

        var result = await service.CreateAsync(ValidRequest(), idempotencyKey: null, CancellationToken.None);

        Assert.Equal(CreatePostEndpointResultKind.Created, result.Kind);
        Assert.NotEqual(Guid.Empty, result.Response!.Id);
    }

    [Fact]
    public async Task CreateAsync_with_same_idempotency_key_replays_response()
    {
        // The first call stores the response for the key. The second call should not
        // execute a new create operation; it should replay the stored response.
        var service = CreateService(new MemoryIdempotencyStore());
        var request = ValidRequest();
        var key = Guid.NewGuid().ToString("N");

        var first = await service.CreateAsync(request, key, CancellationToken.None);
        var second = await service.CreateAsync(request, key, CancellationToken.None);

        Assert.Equal(CreatePostEndpointResultKind.Created, first.Kind);
        Assert.Equal(CreatePostEndpointResultKind.Replayed, second.Kind);
        Assert.Equal(first.Response!.Id, second.Response!.Id);
    }

    [Fact]
    public async Task CreateAsync_with_same_key_and_different_body_returns_conflict_result()
    {
        // The service hashes the request body. If a key is reused with a different hash,
        // the correct API-level outcome is conflict.
        var service = CreateService(new MemoryIdempotencyStore());
        var key = Guid.NewGuid().ToString("N");

        _ = await service.CreateAsync(ValidRequest("Original"), key, CancellationToken.None);
        var second = await service.CreateAsync(ValidRequest("Changed"), key, CancellationToken.None);

        Assert.Equal(CreatePostEndpointResultKind.Conflict, second.Kind);
        Assert.Equal("idempotency_key_reused", second.ProblemType);
    }

    private static CreatePostEndpointService CreateService(ICreatePostIdempotencyStore idempotencyStore)
    {
        // This builds only the slice of the application needed by the endpoint service.
        // It keeps the test fast while still exercising the real create-post handler.
        return new CreatePostEndpointService(
            new CreatePostCommandHandler(
                new CreatePostCommandValidator(),
                new CapturingStore(),
                new SequentialIdGenerator(),
                new FixedClock()),
            idempotencyStore,
            NullLogger<CreatePostEndpointService>.Instance);
    }

    private static CreatePostRequest ValidRequest(string title = "Title")
    {
        return new CreatePostRequest
        {
            Title = title,
            Description = "Description",
            Content = "Content",
            Author = new AuthorRequest
            {
                Name = "Ada",
                Surname = "Lovelace"
            }
        };
    }

    private sealed class MemoryIdempotencyStore : ICreatePostIdempotencyStore
    {
        // A small in-memory idempotency implementation makes the service tests deterministic.
        // MongoDB idempotency persistence is tested separately in InfrastructureTests.
        private readonly Dictionary<string, Entry> _entries = [];

        public Task<CreatePostIdempotencyStartResult> TryStartAsync(
            string key,
            string requestHash,
            CancellationToken cancellationToken)
        {
            if (!_entries.TryGetValue(key, out var entry))
            {
                _entries[key] = new Entry(requestHash, null, null);
                return Task.FromResult(CreatePostIdempotencyStartResult.Started());
            }

            if (entry.RequestHash != requestHash)
            {
                return Task.FromResult(CreatePostIdempotencyStartResult.RequestMismatch());
            }

            return entry.Response is null || entry.Location is null
                ? Task.FromResult(CreatePostIdempotencyStartResult.InProgress())
                : Task.FromResult(CreatePostIdempotencyStartResult.Completed(entry.Response, entry.Location));
        }

        public Task CompleteAsync(
            string key,
            string requestHash,
            PostResponse response,
            string location,
            CancellationToken cancellationToken)
        {
            _entries[key] = new Entry(requestHash, response, location);
            return Task.CompletedTask;
        }

        public Task RemoveAsync(string key, string requestHash, CancellationToken cancellationToken)
        {
            if (_entries.TryGetValue(key, out var entry) && entry.RequestHash == requestHash)
            {
                _entries.Remove(key);
            }

            return Task.CompletedTask;
        }

        private sealed record Entry(string RequestHash, PostResponse? Response, string? Location);
    }

    private sealed class CapturingStore : IPostCreationStore
    {
        // The endpoint service tests are not about MongoDB transactions, so this fake
        // accepts the atomic save call and lets the handler complete successfully.
        public Task SaveAsync(Post post, Author author, OutboxMessage outboxMessage, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }

    private sealed class SequentialIdGenerator : IIdGenerator
    {
        // Predictable IDs make replay assertions straightforward.
        private int _value;

        public Guid NewId()
        {
            _value++;
            return Guid.Parse($"00000000-0000-0000-0000-{_value:000000000000}");
        }
    }

    private sealed class FixedClock : IClock
    {
        // Predictable time keeps event payloads stable.
        public DateTimeOffset UtcNow { get; } = new(2026, 7, 1, 12, 0, 0, TimeSpan.Zero);
    }
}
