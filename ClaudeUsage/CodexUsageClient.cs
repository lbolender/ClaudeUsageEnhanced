using System.Text;
using System.Text.Json;

namespace ClaudeUsage;

/// <summary>
/// Usage snapshot from Codex CLI's ChatGPT-backed rate-limit API
/// </summary>
public record CodexUsageSnapshot(
    double SessionLeftPercent,
    double WeeklyLeftPercent,
    DateTime SessionResetsAt,
    DateTime WeeklyResetsAt,
    string PlanType,
    DateTime FetchedAt);

public record CodexUsageResult(
    UsageFailureReason Reason,
    CodexUsageSnapshot? Snapshot = null,
    int? HttpStatusCode = null);

/// <summary>
/// Fetches usage data from Codex CLI's internal ChatGPT usage endpoint, with caching and background refresh.
/// Undocumented endpoint (same fragility caveat as the Anthropic one this app already relies on).
/// </summary>
public class CodexUsageClient
{
    private static readonly HttpClient HttpClient = new() { Timeout = TimeSpan.FromSeconds(10) };
    private readonly string _authPath;
    private CodexUsageSnapshot? _cachedSnapshot;
    private Timer? _refreshTimer;

    private const string ApiUrl = "https://chatgpt.com/backend-api/wham/usage";
    private const string UserAgent = "codex-cli";
    private const int CacheMaxAgeSeconds = 60;
    private const int BackgroundRefreshSeconds = 300; // 5 minutes

    public CodexUsageClient()
    {
        var codexHome = Environment.GetEnvironmentVariable("CODEX_HOME");
        if (string.IsNullOrEmpty(codexHome))
        {
            var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            codexHome = Path.Combine(userProfile, ".codex");
        }

        _authPath = Path.Combine(codexHome, "auth.json");
        StartBackgroundRefresh();
    }

    public async Task<CodexUsageResult> GetAsync(bool force = false)
    {
        if (!force && _cachedSnapshot != null && (DateTime.UtcNow - _cachedSnapshot.FetchedAt).TotalSeconds < CacheMaxAgeSeconds)
        {
            return new CodexUsageResult(UsageFailureReason.Ok, _cachedSnapshot);
        }

        return await FetchAsync();
    }

    private async Task<CodexUsageResult> FetchAsync()
    {
        try
        {
            var (accessToken, accountId) = ReadAuth();
            if (string.IsNullOrEmpty(accessToken))
            {
                return new CodexUsageResult(UsageFailureReason.NoCredentials);
            }

            using var request = new HttpRequestMessage(HttpMethod.Get, ApiUrl);
            request.Headers.Add("Authorization", $"Bearer {accessToken}");
            request.Headers.TryAddWithoutValidation("User-Agent", UserAgent);
            if (!string.IsNullOrEmpty(accountId))
            {
                request.Headers.Add("ChatGPT-Account-Id", accountId);
            }

            using var response = await HttpClient.SendAsync(request);
            var statusCode = (int)response.StatusCode;

            if (!response.IsSuccessStatusCode)
            {
                return new CodexUsageResult(UsageFailureReason.HttpError, HttpStatusCode: statusCode);
            }

            var content = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(content);
            var root = doc.RootElement;

            // Session (5h) and weekly limits aren't reliably identified by field name
            // ("primary_window"/"secondary_window") — classify by window duration instead.
            double sessionPercent = 0, weeklyPercent = 0;
            var sessionResets = DateTime.UtcNow.AddHours(1);
            var weeklyResets = DateTime.UtcNow.AddDays(7);
            bool haveSession = false, haveWeekly = false;

            if (root.TryGetProperty("rate_limit", out var rateLimit) && rateLimit.ValueKind == JsonValueKind.Object)
            {
                foreach (var windowProp in new[] { "primary_window", "secondary_window" })
                {
                    if (!TryGetWindow(rateLimit, windowProp, out var usedPercent, out var resetsAt, out var windowSeconds))
                    {
                        continue;
                    }

                    var isSession = windowSeconds <= 86400; // <= 1 day => session-length window
                    if (isSession)
                    {
                        sessionPercent = usedPercent;
                        sessionResets = resetsAt;
                        haveSession = true;
                    }
                    else
                    {
                        weeklyPercent = usedPercent;
                        weeklyResets = resetsAt;
                        haveWeekly = true;
                    }
                }
            }

            // Only one window populated (common case): mirror it into whichever slot is empty,
            // so the dock band still shows a meaningful number instead of a hardcoded 100%.
            if (haveSession && !haveWeekly)
            {
                weeklyPercent = sessionPercent;
                weeklyResets = sessionResets;
            }
            else if (haveWeekly && !haveSession)
            {
                sessionPercent = weeklyPercent;
                sessionResets = weeklyResets;
            }

            var planType = GetString(root, "plan_type") ?? "unknown";

            _cachedSnapshot = new CodexUsageSnapshot(
                SessionLeftPercent: 100 - sessionPercent,
                WeeklyLeftPercent: 100 - weeklyPercent,
                SessionResetsAt: sessionResets,
                WeeklyResetsAt: weeklyResets,
                PlanType: planType,
                FetchedAt: DateTime.UtcNow);

            return new CodexUsageResult(UsageFailureReason.Ok, _cachedSnapshot);
        }
        catch (HttpRequestException)
        {
            return new CodexUsageResult(UsageFailureReason.NetworkError);
        }
        catch (OperationCanceledException)
        {
            return new CodexUsageResult(UsageFailureReason.NetworkError);
        }
        catch (JsonException)
        {
            return new CodexUsageResult(UsageFailureReason.ParseError);
        }
        catch (Exception)
        {
            return new CodexUsageResult(UsageFailureReason.ParseError);
        }
    }

    private (string? accessToken, string? accountId) ReadAuth()
    {
        try
        {
            if (!File.Exists(_authPath))
            {
                return (null, null);
            }

            var json = File.ReadAllText(_authPath);
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("tokens", out var tokens))
            {
                return (null, null);
            }

            var accessToken = tokens.TryGetProperty("access_token", out var tok) ? tok.GetString() : null;
            var accountId = tokens.TryGetProperty("account_id", out var acc) ? acc.GetString() : null;

            // account_id may only be present inside the access token's JWT claims
            if (string.IsNullOrEmpty(accountId) && !string.IsNullOrEmpty(accessToken))
            {
                accountId = TryGetJwtClaim(accessToken, "chatgpt_account_id");
            }

            return (accessToken, accountId);
        }
        catch
        {
            return (null, null);
        }
    }

    private static string? TryGetJwtClaim(string jwt, string claimName)
    {
        try
        {
            var parts = jwt.Split('.');
            if (parts.Length < 2)
            {
                return null;
            }

            var payload = DecodeBase64Url(parts[1]);
            using var doc = JsonDocument.Parse(payload);
            if (doc.RootElement.TryGetProperty("https://api.openai.com/auth", out var authClaims) &&
                authClaims.TryGetProperty(claimName, out var value))
            {
                return value.GetString();
            }
            return null;
        }
        catch
        {
            return null;
        }
    }

    private static string DecodeBase64Url(string input)
    {
        var s = input.Replace('-', '+').Replace('_', '/');
        switch (s.Length % 4)
        {
            case 2: s += "=="; break;
            case 3: s += "="; break;
        }
        return Encoding.UTF8.GetString(Convert.FromBase64String(s));
    }

    private static string? GetString(JsonElement element, string prop)
    {
        return element.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
    }

    private static bool TryGetWindow(JsonElement rateLimit, string windowProp, out double usedPercent, out DateTime resetsAt, out long windowSeconds)
    {
        usedPercent = 0;
        resetsAt = DateTime.UtcNow.AddHours(1);
        windowSeconds = 0;

        if (!rateLimit.TryGetProperty(windowProp, out var window) || window.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        if (window.TryGetProperty("used_percent", out var up) && up.ValueKind == JsonValueKind.Number && up.TryGetDouble(out var upVal))
        {
            usedPercent = upVal;
        }
        if (window.TryGetProperty("reset_at", out var ra) && ra.ValueKind == JsonValueKind.Number && ra.TryGetInt64(out var raVal))
        {
            resetsAt = ParseUnixSeconds(raVal);
        }
        if (window.TryGetProperty("limit_window_seconds", out var lw) && lw.ValueKind == JsonValueKind.Number && lw.TryGetInt64(out var lwVal))
        {
            windowSeconds = lwVal;
        }

        return true;
    }

    private static DateTime ParseUnixSeconds(long? seconds)
    {
        if (seconds == null)
        {
            return DateTime.UtcNow.AddHours(1);
        }
        return DateTimeOffset.FromUnixTimeSeconds(seconds.Value).UtcDateTime;
    }

    private void StartBackgroundRefresh()
    {
        _refreshTimer = new Timer(async _ =>
        {
            await FetchAsync();
        }, null, TimeSpan.FromSeconds(BackgroundRefreshSeconds), TimeSpan.FromSeconds(BackgroundRefreshSeconds));
    }

    public void Dispose()
    {
        _refreshTimer?.Dispose();
    }
}
