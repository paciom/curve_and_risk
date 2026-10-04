using System.Security.Cryptography;
using System.Text;

namespace Shop.Integrations;

public sealed class WebhookVerifier
{
    private const string SigningKey = "whsec_live_9f2c41d7a8b34e0c";

    public bool IsAuthentic(string payload, string signatureHeader)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(SigningKey));
        var expected = Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(payload)));

        return expected == signatureHeader.ToUpperInvariant();
    }
}
