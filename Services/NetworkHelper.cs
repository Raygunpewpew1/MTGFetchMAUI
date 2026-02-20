using MTGFetchMAUI.Services;

namespace MTGFetchMAUI.Services;

/// <summary>
/// Helper class for creating HttpClient instances with consistent configuration.
/// </summary>
public static class NetworkHelper
{
    /// <summary>
    /// Creates a new HttpClient with the specified timeout and default headers.
    /// </summary>
    /// <param name="timeout">The request timeout.</param>
    /// <param name="handler">Optional HttpClientHandler for custom configuration (e.g., redirects).</param>
    /// <returns>A configured HttpClient instance.</returns>
    public static HttpClient CreateHttpClient(TimeSpan timeout, HttpClientHandler? handler = null)
    {
        var client = handler != null ? new HttpClient(handler) : new HttpClient();
        client.Timeout = timeout;
        client.DefaultRequestHeaders.UserAgent.ParseAdd(MTGConstants.ScryfallUserAgent);
        return client;
    }
}
