using Visma.Blogging.Domain;

namespace Visma.Blogging.UnitTests;

/// <summary>
/// Tests the domain model directly.
/// Domain tests do not use handlers, controllers, databases, or serializers because
/// domain rules should be enforceable without any framework around them.
/// </summary>
public sealed class DomainTests
{
    [Fact]
    public void Author_Create_trims_names()
    {
        // Normalization is a domain responsibility because every entry point should
        // get the same clean Author object, whether input came from HTTP, XML, or a queue.
        var author = Author.Create(new AuthorId(Guid.NewGuid()), " Ada ", " Lovelace ");

        Assert.Equal("Ada", author.Name);
        Assert.Equal("Lovelace", author.Surname);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Author_Create_rejects_missing_name(string value)
    {
        // The domain throws for invariant violations. Application validation catches
        // common input errors earlier, but the domain remains the final safety net.
        var exception = Assert.Throws<DomainValidationException>(() =>
            Author.Create(new AuthorId(Guid.NewGuid()), value, "Lovelace"));

        Assert.Equal(nameof(Author.Name), exception.Field);
    }

    [Fact]
    public void Post_Create_rejects_empty_identifier()
    {
        // Strongly typed IDs prevent accidentally passing raw Guid values around
        // and allow the domain to reject Guid.Empty at construction time.
        Assert.Throws<DomainValidationException>(() => new PostId(Guid.Empty));
    }

    [Fact]
    public void Post_Create_rejects_default_created_at()
    {
        // A post must have a real creation instant. Rejecting default avoids storing
        // meaningless timestamps such as 0001-01-01.
        var authorId = new AuthorId(Guid.NewGuid());

        var exception = Assert.Throws<DomainValidationException>(() =>
            Post.Create(new PostId(Guid.NewGuid()), authorId, "Title", "Description", "Content", default));

        Assert.Equal(nameof(Post.CreatedAt), exception.Field);
    }

    [Fact]
    public void Post_Create_normalizes_created_at_to_utc()
    {
        // UTC normalization keeps persisted dates comparable and avoids timezone bugs
        // when logs, outbox events, and API responses are inspected together.
        var createdAt = new DateTimeOffset(2026, 7, 1, 10, 0, 0, TimeSpan.FromHours(-3));

        var post = Post.Create(
            new PostId(Guid.NewGuid()),
            new AuthorId(Guid.NewGuid()),
            " Title ",
            " Description ",
            " Content ",
            createdAt);

        Assert.Equal("Title", post.Title);
        Assert.Equal(TimeSpan.Zero, post.CreatedAt.Offset);
    }
}
