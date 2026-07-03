using Visma.Blogging.Domain;

namespace Visma.Blogging.UnitTests;

public sealed class DomainTests
{
    [Fact]
    public void Author_Create_trims_names()
    {
        var author = Author.Create(new AuthorId(Guid.NewGuid()), " Ada ", " Lovelace ");

        Assert.Equal("Ada", author.Name);
        Assert.Equal("Lovelace", author.Surname);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Author_Create_rejects_missing_name(string value)
    {
        var exception = Assert.Throws<DomainValidationException>(() =>
            Author.Create(new AuthorId(Guid.NewGuid()), value, "Lovelace"));

        Assert.Equal(nameof(Author.Name), exception.Field);
    }

    [Fact]
    public void Post_Create_rejects_empty_identifier()
    {
        Assert.Throws<DomainValidationException>(() => new PostId(Guid.Empty));
    }

    [Fact]
    public void Post_Create_rejects_default_created_at()
    {
        var authorId = new AuthorId(Guid.NewGuid());

        var exception = Assert.Throws<DomainValidationException>(() =>
            Post.Create(new PostId(Guid.NewGuid()), authorId, "Title", "Description", "Content", default));

        Assert.Equal(nameof(Post.CreatedAt), exception.Field);
    }

    [Fact]
    public void Post_Create_normalizes_created_at_to_utc()
    {
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
