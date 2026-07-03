using System.Security.Cryptography;
using System.Text;

namespace Visma.Blogging.Api;

internal static class IdempotencyRequestHasher
{
    public static string Hash(CreatePostRequest request)
    {
        var payload = string.Join(
            '\u001f',
            request.Title ?? string.Empty,
            request.Description ?? string.Empty,
            request.Content ?? string.Empty,
            request.Author?.Name ?? string.Empty,
            request.Author?.Surname ?? string.Empty);

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload)));
    }
}
