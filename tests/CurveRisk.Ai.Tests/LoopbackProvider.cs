using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;

namespace CurveRisk.Ai.Tests;

/// <summary>
/// A stand-in model endpoint on the loopback interface. It answers one HTTP request with a fixed JSON
/// body and hands back the raw request, so a client that builds its own transport can be checked
/// without leaving the machine.
/// </summary>
internal sealed partial class LoopbackProvider : IDisposable
{
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);

    public LoopbackProvider() => _listener.Start();

    public Uri BaseUrl => new($"http://127.0.0.1:{((IPEndPoint)_listener.LocalEndpoint).Port}/proxy");

    /// <summary>Returns the request as text: request line, headers and body.</summary>
    public async Task<string> ServeOnceAsync(string responseJson, CancellationToken cancellationToken)
    {
        using var connection = await _listener.AcceptTcpClientAsync(cancellationToken);
        var stream = connection.GetStream();
        var request = await ReadRequestAsync(stream, cancellationToken);

        var body = Encoding.UTF8.GetBytes(responseJson);
        var head = $"HTTP/1.1 200 OK\r\nContent-Type: application/json\r\nContent-Length: {body.Length}\r\nConnection: close\r\n\r\n";
        await stream.WriteAsync(Encoding.ASCII.GetBytes(head), cancellationToken);
        await stream.WriteAsync(body, cancellationToken);
        return request;
    }

    public void Dispose() => _listener.Dispose();

    private static async Task<string> ReadRequestAsync(NetworkStream stream, CancellationToken cancellationToken)
    {
        var buffer = new byte[16384];
        var request = new StringBuilder();
        var read = 1;
        while (read > 0 && !IsComplete(request.ToString()))
        {
            read = await stream.ReadAsync(buffer, cancellationToken);
            request.Append(Encoding.UTF8.GetString(buffer, 0, read));
        }

        return request.ToString();
    }

    private static bool IsComplete(string request)
    {
        var bodyStart = request.IndexOf("\r\n\r\n", StringComparison.Ordinal) + 4;
        if (bodyStart < 4)
        {
            return false;
        }

        var length = ContentLengthPattern().Match(request);
        return length.Success
            ? Encoding.UTF8.GetByteCount(request.AsSpan(bodyStart)) >= int.Parse(length.Groups[1].Value, CultureInfo.InvariantCulture)
            : request.EndsWith("0\r\n\r\n", StringComparison.Ordinal);
    }

    [GeneratedRegex(@"^Content-Length: ([0-9]+)", RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.CultureInvariant)]
    private static partial Regex ContentLengthPattern();
}
