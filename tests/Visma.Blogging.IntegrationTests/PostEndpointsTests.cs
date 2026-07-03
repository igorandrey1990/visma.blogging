using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Xml.Linq;
using Microsoft.AspNetCore.Mvc.Testing;
using MongoDB.Bson;
using MongoDB.Driver;
using Visma.Blogging.Application.Blogging;

namespace Visma.Blogging.IntegrationTests;

public sealed class PostEndpointsTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public PostEndpointsTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Post_creates_post_and_get_returns_without_author_by_default()
    {
        using var client = _factory.CreateClient();

        var create = await client.PostAsJsonAsync("/post", ValidRequest());
        var created = await create.Content.ReadFromJsonAsync<PostResponse>();

        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        Assert.NotNull(create.Headers.Location);
        Assert.NotNull(created);

        var get = await client.GetAsync($"/post/{created!.Id:D}");
        var fetched = await get.Content.ReadFromJsonAsync<PostResponse>();

        Assert.Equal(HttpStatusCode.OK, get.StatusCode);
        Assert.Equal(created.Id, fetched!.Id);
        Assert.Null(fetched.Author);
    }

    [Fact]
    public async Task Get_returns_author_when_requested()
    {
        using var client = _factory.CreateClient();
        var create = await client.PostAsJsonAsync("/post", ValidRequest());
        var created = await create.Content.ReadFromJsonAsync<PostResponse>();

        var get = await client.GetAsync($"/post/{created!.Id:D}?includeAuthor=true");
        var fetched = await get.Content.ReadFromJsonAsync<PostResponse>();

        Assert.Equal(HttpStatusCode.OK, get.StatusCode);
        Assert.NotNull(fetched!.Author);
        Assert.Equal("Ada", fetched.Author!.Name);
    }

    [Fact]
    public async Task Post_returns_bad_request_for_invalid_body()
    {
        using var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/post", new
        {
            title = "",
            description = "Description",
            content = "Content",
            author = new { name = "", surname = "Lovelace" }
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Post_accepts_xml_and_can_return_xml()
    {
        using var client = _factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/post");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/xml"));
        request.Content = new StringContent(
            """
            <CreatePostRequest>
              <Title>XML post</Title>
              <Description>Created from XML</Description>
              <Content>The body came from XML.</Content>
              <Author>
                <Name>Ada</Name>
                <Surname>Lovelace</Surname>
              </Author>
            </CreatePostRequest>
            """,
            Encoding.UTF8,
            "application/xml");

        var create = await client.SendAsync(request);
        var createXml = XDocument.Parse(await create.Content.ReadAsStringAsync());
        var id = Guid.Parse(createXml.Root!.Element("Id")!.Value);

        using var getRequest = new HttpRequestMessage(HttpMethod.Get, $"/post/{id:D}?includeAuthor=true");
        getRequest.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/xml"));
        var get = await client.SendAsync(getRequest);
        var getXml = XDocument.Parse(await get.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        Assert.Equal("application/xml", create.Content.Headers.ContentType!.MediaType);
        Assert.Equal(HttpStatusCode.OK, get.StatusCode);
        Assert.Equal("XML post", getXml.Root!.Element("Title")!.Value);
        Assert.Equal("Ada", getXml.Root.Element("Author")!.Element("Name")!.Value);
    }

    [Fact]
    public async Task Post_with_same_idempotency_key_replays_created_response()
    {
        using var client = _factory.CreateClient();
        var key = Guid.NewGuid().ToString("N");
        var request = ValidRequest($"Idempotent post {key}");

        var first = await SendPostWithIdempotencyKeyAsync(client, key, request);
        var firstPost = await first.Content.ReadFromJsonAsync<PostResponse>();
        var second = await SendPostWithIdempotencyKeyAsync(client, key, request);
        var secondPost = await second.Content.ReadFromJsonAsync<PostResponse>();

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.Created, second.StatusCode);
        Assert.Equal(firstPost!.Id, secondPost!.Id);
        Assert.True(second.Headers.TryGetValues("Idempotency-Replayed", out var values));
        Assert.Contains("true", values);
    }

    [Fact]
    public async Task Post_with_same_idempotency_key_and_different_body_returns_conflict()
    {
        using var client = _factory.CreateClient();
        var key = Guid.NewGuid().ToString("N");

        var first = await SendPostWithIdempotencyKeyAsync(client, key, ValidRequest("Original idempotent post"));
        var second = await SendPostWithIdempotencyKeyAsync(client, key, ValidRequest("Changed idempotent post"));

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    [Fact]
    public async Task Get_returns_not_found_for_missing_post()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync($"/post/{Guid.NewGuid():D}?includeAuthor=true");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Logs_are_persisted_to_mongodb()
    {
        using var client = _factory.CreateClient();
        var title = $"Logged post {Guid.NewGuid():N}";

        var response = await client.PostAsJsonAsync("/post", ValidRequest(title));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.True(await LogExistsAsync(title));
    }

    private static async Task<bool> LogExistsAsync(string title)
    {
        var mongo = new MongoClient("mongodb://localhost:27017/?directConnection=true");
        var logs = mongo.GetDatabase("visma_blogging_dev").GetCollection<BsonDocument>("logs");
        var filter = Builders<BsonDocument>.Filter.Regex("Message", new BsonRegularExpression(title));

        for (var attempt = 0; attempt < 20; attempt++)
        {
            if (await logs.CountDocumentsAsync(filter) > 0)
            {
                return true;
            }

            await Task.Delay(100);
        }

        return false;
    }

    private static async Task<HttpResponseMessage> SendPostWithIdempotencyKeyAsync(
        HttpClient client,
        string key,
        object request)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, "/post")
        {
            Content = JsonContent.Create(request)
        };
        message.Headers.Add("Idempotency-Key", key);

        return await client.SendAsync(message);
    }

    private static object ValidRequest(string title = "First post")
    {
        return new
        {
            title,
            description = "A short technical note",
            content = "The body of the post.",
            author = new
            {
                name = "Ada",
                surname = "Lovelace"
            }
        };
    }
}
