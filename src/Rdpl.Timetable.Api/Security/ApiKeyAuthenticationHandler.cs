using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using Rdpl.Timetable.Api.Options;

namespace Rdpl.Timetable.Api.Security;

/// <summary>
/// Authenticates callers by the <c>X-Api-Key</c> header.
/// The key is compared as SHA-256 digests with <see cref="CryptographicOperations.FixedTimeEquals"/> so neither the
/// key length nor its content leaks through timing. The key itself is never logged.
/// </summary>
public sealed class ApiKeyAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string SchemeName = "ApiKey";
    public const string HeaderName = "X-Api-Key";
    private const int MaxHeaderLength = 512;

    private readonly IOptionsMonitor<ApiSecurityOptions> _security;

    public ApiKeyAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        IOptionsMonitor<ApiSecurityOptions> security)
        : base(options, logger, encoder)
    {
        _security = security;
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(HeaderName, out var values))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        if (values.Count != 1 || string.IsNullOrEmpty(values[0]) || values[0]!.Length > MaxHeaderLength)
        {
            return Task.FromResult(AuthenticateResult.Fail("Malformed API key header."));
        }

        var expected = _security.CurrentValue.ApiKey;
        if (string.IsNullOrEmpty(expected))
        {
            // Fail closed: should be impossible because startup refuses to run without a key.
            return Task.FromResult(AuthenticateResult.Fail("API key is not configured on the server."));
        }

        if (!KeysMatch(values[0]!, expected))
        {
            Logger.LogWarning("Rejected request with an invalid API key from {RemoteIp}.", Context.Connection.RemoteIpAddress);
            return Task.FromResult(AuthenticateResult.Fail("Invalid API key."));
        }

        var identity = new ClaimsIdentity(new[] { new Claim(ClaimTypes.Name, "api-client") }, SchemeName);
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName)));
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = StatusCodes.Status401Unauthorized;
        Response.Headers.WWWAuthenticate = $"{SchemeName} header=\"{HeaderName}\"";
        return Task.CompletedTask;
    }

    public static bool KeysMatch(string provided, string expected)
    {
        var a = SHA256.HashData(Encoding.UTF8.GetBytes(provided));
        var b = SHA256.HashData(Encoding.UTF8.GetBytes(expected));
        return CryptographicOperations.FixedTimeEquals(a, b);
    }
}
