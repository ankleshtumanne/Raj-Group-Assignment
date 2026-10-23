using Rdpl.Timetable.Api.Contracts;
using Rdpl.Timetable.Api.Options;
using Rdpl.Timetable.Api.Security;
using Rdpl.Timetable.Configuration;

namespace Rdpl.Timetable.Tests.Api;

public sealed class ApiSecurityTests
{
    private static readonly TimetableApiOptions Options = new() { MaxTimeLimitSeconds = 300, MaxWorkers = 8 };

    [Fact]
    public void Api_key_comparison_matches_only_the_exact_key()
    {
        const string key = "0123456789abcdef0123456789abcdef";
        Assert.True(ApiKeyAuthenticationHandler.KeysMatch(key, key));
        Assert.False(ApiKeyAuthenticationHandler.KeysMatch(key + "x", key));
        Assert.False(ApiKeyAuthenticationHandler.KeysMatch(key.ToUpperInvariant(), key));
        Assert.False(ApiKeyAuthenticationHandler.KeysMatch(string.Empty, key));
    }

    [Fact]
    public void Defaults_are_strict_and_bounded()
    {
        var (settings, errors) = GenerateRequestValidator.Validate(null, Options);
        Assert.Empty(errors);
        Assert.Equal("strict", settings!.Rules.Profile);
        Assert.Equal(ZeroWorkloadPolicy.Exclude, settings.Rules.ZeroWorkloadPolicy);
        Assert.Equal(Options.DefaultTimeLimitSeconds, settings.Solver.TimeLimitSeconds);
    }

    [Theory]
    [InlineData("evil", null, null, null, "Profile")]
    [InlineData("strict", 100000, null, null, "TimeLimitSeconds")]
    [InlineData("strict", 0, null, null, "TimeLimitSeconds")]
    [InlineData("strict", null, 999, null, "Workers")]
    [InlineData("strict", null, null, "DropTable", "ZeroWorkloadPolicy")]
    public void Out_of_range_or_unknown_values_are_rejected(string profile, int? timeLimit, int? workers, string? zeroPolicy, string field)
    {
        var (settings, errors) = GenerateRequestValidator.Validate(
            new GenerateRequest { Profile = profile, TimeLimitSeconds = timeLimit, Workers = workers, ZeroWorkloadPolicy = zeroPolicy }, Options);
        Assert.Null(settings);
        Assert.Contains(field, errors.Keys);
    }

    [Fact]
    public void Pragmatic_request_maps_to_the_pragmatic_rule_profile()
    {
        var (settings, errors) = GenerateRequestValidator.Validate(new GenerateRequest { Profile = "Pragmatic", TimeLimitSeconds = 60, UnstaffedPolicy = "exclude" }, Options);
        Assert.Empty(errors);
        Assert.True(settings!.Rules.BlockSinglesSoft);
        Assert.Equal(UnstaffedPolicy.Exclude, settings.Rules.UnstaffedPolicy);
        Assert.Equal(60, settings.Solver.TimeLimitSeconds);
    }
}
