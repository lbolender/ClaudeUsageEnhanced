using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ClaudeUsage;

/// <summary>
/// Usage snapshot from Claude API
/// </summary>
public record UsageSnapshot(
    double SessionLeftPercent,
    double WeeklyLeftPercent,
    DateTime SessionResetsAt,
    DateTime WeeklyResetsAt,
    List<LimitInfo> Limits,
    string SubscriptionType,
    DateTime FetchedAt);

public record LimitInfo(
    string Kind,
    double PercentUsed,
    DateTime ResetsAt,
    string? ModelDisplayName = null);

/// <summary>
/// Failure reason for usage fetch
/// </summary>
public enum UsageFailureReason
{
    Ok,
    NoCredentials,
    TokenExpired,
    HttpError,
    NetworkError,
    ParseError
}

/// <summary>
/// Result of a usage fetch operation. On failure, Snapshot holds the last good
/// (stale) snapshot if one exists, and RetryAt the time of the next allowed request.
/// </summary>
public record UsageResult(
    UsageFailureReason Reason,
    UsageSnapshot? Snapshot = null,
    int? HttpStatusCode = null,
    DateTime? RetryAt = null)
{
    public bool IsStale => Reason != UsageFailureReason.Ok && Snapshot != null;
}

/// <summary>
/// Fetches usage data from Claude API with caching and backoff.
/// The access token is only read, never refreshed — Claude Code owns the refresh.
/// </summary>
public class UsageClient
{
    private static readonly HttpClient HttpClient = new() { Timeout = TimeSpan.FromSeconds(10) };
    private readonly string _credentialsPath;
    private readonly SemaphoreSlim _fetchLock = new(1, 1);
    private UsageSnapshot? _lastGood;
    private UsageResult? _lastResult;
    private DateTime _nextAllowedFetch = DateTime.MinValue;
    private int _consecutiveFailures;
    private string? _rejectedToken;

    private const string ApiUrl = "https://api.anthropic.com/api/oauth/usage";
    private const string ApiVersion = "oauth-2025-04-20";
    private const string UserAgent = "claude-code/2.1.206";
    private const int CacheMaxAgeSeconds = 180; // at most one request every 3 minutes
    private const int ForcedMinAgeSeconds = 30; // a manual refresh still reuses data younger than this
    private const int BackoffBaseSeconds = 60;
    private const int BackoffMaxSeconds = 1800; // 30 minutes
    private const int ExpirySkewSeconds = 60;

    public UsageClient()
    {
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var userProfileEnv = Environment.GetEnvironmentVariable("USERPROFILE") ?? "(not set)";
        var baseDir = AppContext.BaseDirectory;

        DebugLog.WriteLine($"UsageClient construct: UserProfile={userProfile}, USERPROFILE env={userProfileEnv}, BaseDirectory={baseDir}");

        _credentialsPath = Path.Combine(userProfile, ".claude", ".credentials.json");
    }

    /// <summary>
    /// Raised with the result of every fetch attempt (not for cache or backoff hits), so the
    /// dock band shows a refresh triggered from the page without waiting for its next tick.
    /// Handlers must only display the result; calling GetAsync from them can loop.
    /// </summary>
    public event Action<UsageResult>? Updated;

    /// <summary>
    /// Get usage snapshot, using cache if fresher than CacheMaxAgeSeconds.
    /// force shortens the cache to ForcedMinAgeSeconds but never skips an active backoff.
    /// Returns result with failure reason (and last good snapshot) if unsuccessful.
    /// </summary>
    public async Task<UsageResult> GetAsync(bool force = false)
    {
        var maxAgeSeconds = force ? ForcedMinAgeSeconds : CacheMaxAgeSeconds;
        if (IsCacheFresh(maxAgeSeconds))
        {
            return new UsageResult(UsageFailureReason.Ok, _lastGood);
        }

        UsageResult result;
        await _fetchLock.WaitAsync();
        try
        {
            // Another caller may have fetched while we were waiting
            if (IsCacheFresh(maxAgeSeconds))
            {
                return new UsageResult(UsageFailureReason.Ok, _lastGood);
            }

            if (DateTime.UtcNow < _nextAllowedFetch && _lastResult != null)
            {
                DebugLog.WriteLine($"GetAsync: in backoff until {_nextAllowedFetch:O}, skipping request");
                return _lastResult;
            }

            result = _lastResult = await FetchAsync();
        }
        finally
        {
            _fetchLock.Release();
        }

        try
        {
            Updated?.Invoke(result);
        }
        catch (Exception ex)
        {
            DebugLog.WriteLine($"GetAsync: Updated handler failed: {ex}");
        }
        return result;
    }

    private bool IsCacheFresh(int maxAgeSeconds) =>
        _lastGood != null && (DateTime.UtcNow - _lastGood.FetchedAt).TotalSeconds < maxAgeSeconds;

    private async Task<UsageResult> FetchAsync()
    {
        string? token = null;
        try
        {
            var credentialsExist = File.Exists(_credentialsPath);
            DebugLog.WriteLine($"FetchAsync: credentialsPath={_credentialsPath}, exists={credentialsExist}");

            (token, var expiresAt) = ReadCredentials();
            if (string.IsNullOrEmpty(token))
            {
                DebugLog.WriteLine($"FetchAsync: token is null/empty, returning NoCredentials");
                return new UsageResult(UsageFailureReason.NoCredentials, _lastGood);
            }

            DebugLog.WriteLine($"FetchAsync: token length={token.Length}, expiresAt={expiresAt?.ToString("O") ?? "(N/A)"}");

            // No request with a token that cannot work — wait for Claude Code to write a fresh one
            if (expiresAt != null && expiresAt.Value <= DateTime.UtcNow.AddSeconds(ExpirySkewSeconds))
            {
                DebugLog.WriteLine($"FetchAsync: skipped, token expired at {expiresAt:O}");
                return new UsageResult(UsageFailureReason.TokenExpired, _lastGood);
            }
            if (token == _rejectedToken)
            {
                DebugLog.WriteLine($"FetchAsync: skipped, token was already rejected with 401/403");
                return new UsageResult(UsageFailureReason.TokenExpired, _lastGood);
            }

            DebugLog.WriteLine($"FetchAsync: sending GET {ApiUrl} with Authorization Bearer, anthropic-beta={ApiVersion}, User-Agent={UserAgent}");

            using var request = new HttpRequestMessage(HttpMethod.Get, ApiUrl);
            request.Headers.Add("Authorization", $"Bearer {token}");
            request.Headers.Add("anthropic-beta", ApiVersion);
            request.Headers.TryAddWithoutValidation("User-Agent", UserAgent);

            using var response = await HttpClient.SendAsync(request);
            var statusCode = (int)response.StatusCode;
            DebugLog.WriteLine($"FetchAsync: received status {statusCode}");

            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync();
                var errorPreview = errorBody.Length > 200 ? errorBody.Substring(0, 200) : errorBody;
                DebugLog.WriteLine($"FetchAsync: error response body (first 200 chars): {errorPreview}");

                if (statusCode is 401 or 403)
                {
                    _rejectedToken = token;
                    return new UsageResult(UsageFailureReason.TokenExpired, _lastGood, statusCode);
                }

                TimeSpan? retryAfter = response.Headers.RetryAfter?.Delta
                    ?? response.Headers.RetryAfter?.Date - DateTimeOffset.UtcNow;
                DebugLog.WriteLine($"FetchAsync: Retry-After={retryAfter?.ToString() ?? "(none)"}");
                return Failure(UsageFailureReason.HttpError, statusCode, retryAfter);
            }

            var content = await response.Content.ReadAsStringAsync();
            DebugLog.WriteLine($"FetchAsync: response body length={content.Length}, first 500 chars: {(content.Length > 500 ? content.Substring(0, 500) : content)}");

            using var doc = JsonDocument.Parse(content);
            var root = doc.RootElement;

            var sessionUtil = GetDouble(root, "five_hour.utilization") ?? 0;
            var weeklyUtil = GetDouble(root, "seven_day.utilization") ?? 0;
            var sessionResets = ParseDateTime(GetString(root, "five_hour.resets_at"));
            var weeklyResets = ParseDateTime(GetString(root, "seven_day.resets_at"));

            var limits = new List<LimitInfo>();
            if (root.TryGetProperty("limits", out var limitsArray) && limitsArray.ValueKind == JsonValueKind.Array)
            {
                foreach (var limit in limitsArray.EnumerateArray())
                {
                    var kind = GetString(limit, "kind") ?? "unknown";
                    var percent = GetDouble(limit, "percent") ?? 0;
                    var resetsAt = ParseDateTime(GetString(limit, "resets_at"));
                    var modelName = GetString(limit, "scope.model.display_name");

                    limits.Add(new LimitInfo(kind, percent, resetsAt, modelName));
                }
            }

            var subType = GetString(root, "subscription_type") ?? "unknown";

            _lastGood = new UsageSnapshot(
                SessionLeftPercent: 100 - sessionUtil,
                WeeklyLeftPercent: 100 - weeklyUtil,
                SessionResetsAt: sessionResets,
                WeeklyResetsAt: weeklyResets,
                Limits: limits,
                SubscriptionType: subType,
                FetchedAt: DateTime.UtcNow);

            _consecutiveFailures = 0;
            _rejectedToken = null;
            DebugLog.WriteLine($"FetchAsync: OK session={_lastGood.SessionLeftPercent:F1}% weekly={_lastGood.WeeklyLeftPercent:F1}%");
            return new UsageResult(UsageFailureReason.Ok, _lastGood);
        }
        catch (HttpRequestException ex)
        {
            DebugLog.WriteLine($"FetchAsync: HttpRequestException: {ex}");
            return Failure(UsageFailureReason.NetworkError);
        }
        catch (OperationCanceledException ex)
        {
            DebugLog.WriteLine($"FetchAsync: OperationCanceledException: {ex}");
            return Failure(UsageFailureReason.NetworkError);
        }
        catch (JsonException ex)
        {
            DebugLog.WriteLine($"FetchAsync: JsonException: {ex}");
            return Failure(UsageFailureReason.ParseError);
        }
        catch (Exception ex)
        {
            DebugLog.WriteLine($"FetchAsync: Unexpected exception: {ex}");
            return Failure(UsageFailureReason.ParseError);
        }
    }

    /// <summary>
    /// Records a failed request and schedules the next one with exponential backoff
    /// (60s, 120s, 240s … capped at 30 min), or later if the server asked for it.
    /// </summary>
    private UsageResult Failure(UsageFailureReason reason, int? statusCode = null, TimeSpan? retryAfter = null)
    {
        _consecutiveFailures++;
        var exponent = Math.Min(_consecutiveFailures - 1, 10);
        var backoff = TimeSpan.FromSeconds(Math.Min(BackoffBaseSeconds * Math.Pow(2, exponent), BackoffMaxSeconds));
        if (retryAfter != null && retryAfter.Value > backoff)
        {
            backoff = retryAfter.Value;
        }

        _nextAllowedFetch = DateTime.UtcNow + backoff;
        DebugLog.WriteLine($"FetchAsync: failure #{_consecutiveFailures} ({reason} {statusCode}), backoff until {_nextAllowedFetch:O}");
        return new UsageResult(reason, _lastGood, statusCode, _nextAllowedFetch);
    }

    private (string? Token, DateTime? ExpiresAt) ReadCredentials()
    {
        try
        {
            if (!File.Exists(_credentialsPath))
            {
                DebugLog.WriteLine($"ReadCredentials: credentials file does not exist at {_credentialsPath}");
                return (null, null);
            }

            var json = File.ReadAllText(_credentialsPath);
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("claudeAiOauth", out var oauth))
            {
                DebugLog.WriteLine($"ReadCredentials: credentials file exists but claudeAiOauth not found");
                return (null, null);
            }

            var token = oauth.TryGetProperty("accessToken", out var tok) && tok.ValueKind == JsonValueKind.String
                ? tok.GetString()
                : null;

            // expiresAt is a Unix timestamp in milliseconds
            DateTime? expiresAt = oauth.TryGetProperty("expiresAt", out var exp) && exp.ValueKind == JsonValueKind.Number && exp.TryGetInt64(out var ms)
                ? DateTimeOffset.FromUnixTimeMilliseconds(ms).UtcDateTime
                : null;

            if (token != null)
            {
                DebugLog.WriteLine($"ReadCredentials: successfully read token, length={token.Length}");
            }
            else
            {
                DebugLog.WriteLine($"ReadCredentials: credentials file exists but claudeAiOauth.accessToken not found");
            }

            return (token, expiresAt);
        }
        catch (Exception ex)
        {
            DebugLog.WriteLine($"ReadCredentials: exception reading credentials: {ex}");
            return (null, null);
        }
    }

    private string? GetString(JsonElement element, string path)
    {
        var parts = path.Split('.');
        var current = element;
        foreach (var part in parts)
        {
            if (!current.TryGetProperty(part, out var next))
            {
                return null;
            }
            current = next;
            // Handle null values in the path
            if (current.ValueKind == JsonValueKind.Null)
            {
                return null;
            }
        }

        // Only return string if the element is actually a string
        if (current.ValueKind != JsonValueKind.String)
        {
            return null;
        }
        return current.GetString();
    }

    private double? GetDouble(JsonElement element, string path)
    {
        var parts = path.Split('.');
        var current = element;
        foreach (var part in parts)
        {
            if (!current.TryGetProperty(part, out var next))
            {
                return null;
            }
            current = next;
            // Handle null values in the path
            if (current.ValueKind == JsonValueKind.Null)
            {
                return null;
            }
        }

        // If it's a number, get it directly
        if (current.ValueKind == JsonValueKind.Number)
        {
            if (current.TryGetDouble(out var doubleValue))
            {
                return doubleValue;
            }
        }
        // If it's a string, try parsing it
        else if (current.ValueKind == JsonValueKind.String)
        {
            var str = current.GetString();
            if (str != null && double.TryParse(str, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsedValue))
            {
                return parsedValue;
            }
        }
        return null;
    }

    private DateTime ParseDateTime(string? isoString)
    {
        if (string.IsNullOrEmpty(isoString))
        {
            return DateTime.UtcNow.AddHours(1);
        }
        if (DateTime.TryParse(isoString, out var dt))
        {
            return dt.ToUniversalTime();
        }
        return DateTime.UtcNow.AddHours(1);
    }

    public void Dispose()
    {
        _fetchLock.Dispose();
    }
}
