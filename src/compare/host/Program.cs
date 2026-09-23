using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using System.Threading.Channels;

var options = HostOptions.Parse(args);

if (await TryReuseExistingHostAsync(options))
    return;

CleanupOldCompareSessions();

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls(options.Url);
var app = builder.Build();

var events = new EventHub();
var gate = new SemaphoreSlim(1, 1);
var html = ReadEmbedded("index.html");

app.MapGet("/", () => Results.Content(html, "text/html"));
app.MapGet("/api/meta", BuildMetaAsync);

app.MapGet("/events", async context =>
{
    context.Response.Headers.CacheControl = "no-cache";
    context.Response.Headers.Connection = "keep-alive";
    context.Response.ContentType = "text/event-stream";
    var subscription = events.Subscribe();
    try
    {
        await foreach (var payload in subscription.Reader.ReadAllAsync(context.RequestAborted))
        {
            await context.Response.WriteAsync($"data: {payload}\n\n", context.RequestAborted);
            await context.Response.Body.FlushAsync(context.RequestAborted);
        }
    }
    catch (OperationCanceledException) { }
    finally
    {
        events.Unsubscribe(subscription.Id);
    }
});

app.MapPost("/api/compare", (CompareRequest request) =>
{
    if (!gate.Wait(0))
        return Results.Conflict(new { error = "Another compare operation is already being prepared." });

    _ = Task.Run(async () =>
    {
        try { await RunCompareAsync(request); }
        finally { gate.Release(); }
    });

    return Results.Accepted();
});

app.Lifetime.ApplicationStarted.Register(() =>
{
    if (options.NoBrowser) return;
    try { Process.Start(new ProcessStartInfo(options.Url) { UseShellExecute = true }); }
    catch { }
});

await app.RunAsync();

async Task<object> BuildMetaAsync()
{
    var projectPath = options.ProjectPath;
    var configPath = GetConfigPath(projectPath);
    var config = ConfigInfo.Load(configPath);
    var destinationPath = config?.DestinationPath;
    var repoRoot = string.IsNullOrWhiteSpace(destinationPath)
        ? null
        : await GitCaptureAsync(destinationPath, "rev-parse", "--show-toplevel");

    var rslComparePath = FindRslCompare(options.RslComparePath);

    var versions = new List<object>();
    if (!string.IsNullOrWhiteSpace(destinationPath) && !string.IsNullOrWhiteSpace(repoRoot))
    {
        var totalText = await GitCaptureAsync(destinationPath, "rev-list", "--count", "HEAD", "--", ".");
        _ = int.TryParse(totalText, out var total);

        var log = await RunGitAsync(destinationPath,
            "log", "-20", "--date=iso-strict", "--pretty=%H%x09%h%x09%s%x09%cI", "--", ".");

        if (log.ExitCode == 0)
        {
            var index = 0;
            foreach (var line in log.Output)
            {
                var parts = line.Split('\t', 4);
                if (parts.Length < 4) continue;
                versions.Add(new
                {
                    version = $"V{Math.Max(total - index, 1)}",
                    sha = parts[0],
                    shortSha = parts[1],
                    message = parts[2],
                    commitIso = parts[3]
                });
                index++;
            }
        }
    }

    return new
    {
        live = true,
        projectPath,
        projectFileName = string.IsNullOrWhiteSpace(projectPath) ? null : Path.GetFileName(projectPath),
        configPath,
        configPresent = config is not null,
        destinationPath,
        repoRoot,
        l5xgitPath = options.L5xGitPath,
        l5xgitConfigured = !string.IsNullOrWhiteSpace(options.L5xGitPath) && File.Exists(options.L5xGitPath),
        rslComparePath,
        rslCompareConfigured = !string.IsNullOrWhiteSpace(rslComparePath) && File.Exists(rslComparePath),
        ready = !string.IsNullOrWhiteSpace(projectPath) && File.Exists(projectPath)
            && config is not null
            && !string.IsNullOrWhiteSpace(repoRoot)
            && !string.IsNullOrWhiteSpace(options.L5xGitPath) && File.Exists(options.L5xGitPath)
            && !string.IsNullOrWhiteSpace(rslComparePath) && File.Exists(rslComparePath),
        versions
    };
}

async Task RunCompareAsync(CompareRequest request)
{
    try
    {
        var projectPath = options.ProjectPath;
        if (string.IsNullOrWhiteSpace(projectPath) || !File.Exists(projectPath))
            throw new InvalidOperationException("The current ACD path is missing or does not exist.");

        if (string.IsNullOrWhiteSpace(options.L5xGitPath) || !File.Exists(options.L5xGitPath))
            throw new InvalidOperationException("l5xgit.exe is not configured.");

        var configPath = GetConfigPath(projectPath);
        var config = ConfigInfo.Load(configPath)
            ?? throw new InvalidOperationException("This project is not connected to version control. Capture a version first.");

        var destinationPath = Path.GetFullPath(config.DestinationPath);
        var repoRoot = await GitCaptureAsync(destinationPath, "rev-parse", "--show-toplevel")
            ?? throw new InvalidOperationException("Unable to determine the Git repository root.");

        var compareExe = FindRslCompare(options.RslComparePath)
            ?? throw new InvalidOperationException("Logix Designer Compare Tool (RSLCompare.exe) was not found.");

        var commitCheck = await RunGitAsync(repoRoot, "cat-file", "-e", $"{request.Sha}^{{commit}}");
        if (commitCheck.ExitCode != 0)
            throw new InvalidOperationException($"Git version '{request.Sha}' could not be resolved.");

        var relativeDestination = Path.GetRelativePath(repoRoot, destinationPath);
        if (relativeDestination.StartsWith("..", StringComparison.Ordinal))
            throw new InvalidOperationException("The configured exploded-content directory is not inside the Git repository.");

        var sessionRoot = Path.Combine(
            Path.GetTempPath(),
            "LogixVcsTools",
            "LogixCompare",
            DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(sessionRoot);

        var currentAcdCopy = Path.Combine(sessionRoot, "CurrentSavedProject.ACD");
        var currentL5x = Path.Combine(sessionRoot, "CurrentSavedProject.L5X");
        var historicalL5x = Path.Combine(sessionRoot, $"{SanitizeFileName(request.Version ?? "Historical")}.L5X");
        var worktree = Path.Combine(sessionRoot, "worktree");
        var worktreeAdded = false;

        events.Publish(new { type = "stage", stage = "current", state = "active", label = "Exporting" });
        events.Publish(new { type = "raw", line = $"Copy saved ACD: {projectPath}" });

        File.Copy(projectPath, currentAcdCopy, true);

        var currentExport = await RunProcessAsync(
            options.L5xGitPath!,
            ["acd2l5x", "--acd", currentAcdCopy, "--l5x", currentL5x]);

        PublishProcessOutput(currentExport);

        if (currentExport.ExitCode != 0 || !File.Exists(currentL5x))
            throw new InvalidOperationException(currentExport.Errors.LastOrDefault()
                ?? "Could not convert the current saved ACD to temporary L5X.");

        events.Publish(new { type = "stage", stage = "current", state = "done", label = "Done" });
        events.Publish(new { type = "stage", stage = "history", state = "active", label = "Rebuilding" });

        try
        {
            var worktreeResult = await RunGitAsync(repoRoot, "worktree", "add", "--detach", worktree, request.Sha);
            foreach (var line in worktreeResult.Output)
                events.Publish(new { type = "raw", line });
            foreach (var line in worktreeResult.Errors)
                events.Publish(new
                {
                    type = "raw",
                    line = worktreeResult.ExitCode == 0 ? line : "ERROR: " + line
                });

            if (worktreeResult.ExitCode != 0)
                throw new InvalidOperationException(worktreeResult.Errors.LastOrDefault()
                    ?? "Could not create the temporary Git worktree.");
            worktreeAdded = true;

            var historicalSource = string.IsNullOrWhiteSpace(relativeDestination) || relativeDestination == "."
                ? worktree
                : Path.Combine(worktree, relativeDestination);

            if (!Directory.Exists(historicalSource))
                throw new InvalidOperationException("The selected version does not contain the expected exploded Logix source.");

            var implode = await RunProcessAsync(
                options.L5xGitPath!,
                ["implode", "--dir", historicalSource, "--l5x", historicalL5x, "--force"]);

            PublishProcessOutput(implode);

            if (implode.ExitCode != 0 || !File.Exists(historicalL5x))
                throw new InvalidOperationException(implode.Errors.LastOrDefault()
                    ?? "Could not rebuild the selected Git version as temporary L5X.");
        }
        finally
        {
            if (worktreeAdded)
            {
                var remove = await RunGitAsync(repoRoot, "worktree", "remove", "--force", worktree);
                PublishProcessOutput(remove);
            }
        }

        events.Publish(new { type = "stage", stage = "history", state = "done", label = "Done" });
        events.Publish(new { type = "stage", stage = "compare", state = "active", label = "Opening" });
        events.Publish(new { type = "raw", line = $"Historical: {historicalL5x}" });
        events.Publish(new { type = "raw", line = $"Current:    {currentL5x}" });
        events.Publish(new { type = "raw", line = $"Launching:  {compareExe}" });

        var psi = new ProcessStartInfo
        {
            FileName = compareExe,
            UseShellExecute = false,
            WorkingDirectory = Path.GetDirectoryName(compareExe) ?? Environment.CurrentDirectory
        };
        psi.ArgumentList.Add(historicalL5x);
        psi.ArgumentList.Add(currentL5x);
        psi.ArgumentList.Add("-PM");
        psi.ArgumentList.Add("FastestCompare");

        var compareProcess = Process.Start(psi);
        if (compareProcess is null)
            throw new InvalidOperationException("Windows could not start Logix Designer Compare Tool.");

        events.Publish(new { type = "stage", stage = "compare", state = "done", label = "Opened" });
        events.Publish(new
        {
            type = "compare_started",
            version = request.Version ?? request.Sha[..Math.Min(7, request.Sha.Length)],
            sessionRoot
        });

        // Deliberately keep the two generated L5X files for the life of the Compare Tool.
        // Stale compare sessions are cleaned on a later host launch.
    }
    catch (Exception ex)
    {
        events.Publish(new { type = "compare_failure", message = ex.Message });
        events.Publish(new { type = "raw", line = "ERROR: " + ex });
    }
}

void PublishProcessOutput(ProcessResult result)
{
    foreach (var line in result.Output)
        events.Publish(new { type = "raw", line });
    foreach (var line in result.Errors)
        events.Publish(new { type = "raw", line = "ERROR: " + line });
}

static string? GetConfigPath(string? projectPath)
{
    if (string.IsNullOrWhiteSpace(projectPath)) return null;
    var dir = Path.GetDirectoryName(projectPath) ?? string.Empty;
    return Path.Combine(dir, $"{Path.GetFileNameWithoutExtension(projectPath)}_L5xGit.yml");
}

static string SanitizeFileName(string value)
{
    foreach (var c in Path.GetInvalidFileNameChars())
        value = value.Replace(c, '_');
    return string.IsNullOrWhiteSpace(value) ? "Historical" : value;
}

static string? FindRslCompare(string? explicitPath)
{
    var candidates = new List<string?>
    {
        explicitPath,
        Environment.GetEnvironmentVariable("RSLCOMPARE_PATH"),
        @"C:\Program Files (x86)\Rockwell Software\Logix Designer Tools\Logix Designer Compare Tool\RSLCompare.exe",
        @"C:\Program Files\Rockwell Software\Logix Designer Tools\Logix Designer Compare Tool\RSLCompare.exe",
        @"C:\Program Files\Rockwell Software\RSLCompare\RSLCompare.exe",
        @"C:\Program Files (x86)\Rockwell Software\RSLCompare\RSLCompare.exe"
    };

    foreach (var candidate in candidates)
        if (!string.IsNullOrWhiteSpace(candidate) && File.Exists(candidate))
            return Path.GetFullPath(candidate);

    try
    {
        var where = RunProcessAsync("where.exe", ["RSLCompare.exe"]).GetAwaiter().GetResult();
        var first = where.Output.FirstOrDefault(File.Exists);
        if (!string.IsNullOrWhiteSpace(first)) return Path.GetFullPath(first);
    }
    catch { }

    return null;
}

static async Task<string?> GitCaptureAsync(string repoPath, params string[] args)
{
    var result = await RunGitAsync(repoPath, args);
    return result.ExitCode == 0 ? result.Output.FirstOrDefault()?.Trim() : null;
}

static async Task<ProcessResult> RunGitAsync(string repoPath, params string[] args)
{
    var all = new List<string> { "-C", repoPath };
    all.AddRange(args);
    return await RunProcessAsync("git", all);
}

static async Task<ProcessResult> RunProcessAsync(string fileName, IEnumerable<string> args)
{
    var psi = new ProcessStartInfo
    {
        FileName = fileName,
        UseShellExecute = false,
        CreateNoWindow = true,
        RedirectStandardOutput = true,
        RedirectStandardError = true
    };

    foreach (var arg in args) psi.ArgumentList.Add(arg);

    using var process = new Process { StartInfo = psi };
    if (!process.Start())
        return new ProcessResult(-1, [], [$"Could not start {fileName}."]);

    var outputTask = process.StandardOutput.ReadToEndAsync();
    var errorTask = process.StandardError.ReadToEndAsync();
    await process.WaitForExitAsync();

    var output = (await outputTask).Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries);
    var errors = (await errorTask).Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries);
    return new ProcessResult(process.ExitCode, output, errors);
}

static string ReadEmbedded(string fileName)
{
    var assembly = Assembly.GetExecutingAssembly();
    var resource = assembly.GetManifestResourceNames()
        .Single(name => name.EndsWith(fileName, StringComparison.OrdinalIgnoreCase));
    using var stream = assembly.GetManifestResourceStream(resource)
        ?? throw new InvalidOperationException($"Embedded resource '{fileName}' was not found.");
    using var reader = new StreamReader(stream);
    return reader.ReadToEnd();
}

static void CleanupOldCompareSessions()
{
    try
    {
        var root = Path.Combine(Path.GetTempPath(), "LogixVcsTools", "LogixCompare");
        if (!Directory.Exists(root)) return;

        foreach (var dir in Directory.GetDirectories(root))
        {
            try
            {
                if (Directory.GetLastWriteTime(dir) < DateTime.Now.AddDays(-1))
                    Directory.Delete(dir, true);
            }
            catch { }
        }
    }
    catch { }
}

static async Task<bool> TryReuseExistingHostAsync(HostOptions options)
{
    try
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromMilliseconds(800) };
        var response = await client.GetAsync(options.Url.TrimEnd('/') + "/api/meta");
        if (!response.IsSuccessStatusCode) return false;

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = doc.RootElement;
        if (!root.TryGetProperty("projectPath", out var projectProp)) return false;
        var existingProject = projectProp.GetString();

        if (!string.Equals(
            Path.GetFullPath(existingProject ?? string.Empty),
            Path.GetFullPath(options.ProjectPath ?? string.Empty),
            StringComparison.OrdinalIgnoreCase))
            return false;

        if (!options.NoBrowser)
            Process.Start(new ProcessStartInfo(options.Url) { UseShellExecute = true });

        return true;
    }
    catch { return false; }
}

sealed record CompareRequest(string Sha, string? Version);
sealed record ProcessResult(int ExitCode, string[] Output, string[] Errors);
sealed record Subscription(Guid Id, ChannelReader<string> Reader);

sealed class EventHub
{
    private readonly ConcurrentDictionary<Guid, Channel<string>> subscribers = new();

    public Subscription Subscribe()
    {
        var id = Guid.NewGuid();
        var channel = Channel.CreateUnbounded<string>();
        subscribers[id] = channel;
        return new Subscription(id, channel.Reader);
    }

    public void Unsubscribe(Guid id)
    {
        if (subscribers.TryRemove(id, out var channel)) channel.Writer.TryComplete();
    }

    public void Publish(object value)
    {
        var payload = JsonSerializer.Serialize(value);
        foreach (var channel in subscribers.Values)
            channel.Writer.TryWrite(payload);
    }
}

sealed record ConfigInfo(string DestinationPath)
{
    public static ConfigInfo? Load(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return null;

        foreach (var raw in File.ReadLines(path))
        {
            var line = raw.Trim();
            if (!line.StartsWith("destination_path:", StringComparison.OrdinalIgnoreCase)) continue;
            var value = line[(line.IndexOf(':') + 1)..].Trim().Trim('"', '\'');
            return string.IsNullOrWhiteSpace(value) ? null : new ConfigInfo(value);
        }

        return null;
    }
}

sealed record HostOptions(
    string Url,
    string? ProjectPath,
    string? L5xGitPath,
    string? RslComparePath,
    bool NoBrowser)
{
    public static HostOptions Parse(string[] args)
    {
        string? Value(string name)
        {
            var index = Array.FindIndex(args, a => a.Equals(name, StringComparison.OrdinalIgnoreCase));
            return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
        }

        return new HostOptions(
            Value("--url") ?? "http://127.0.0.1:5191",
            Value("--project-path"),
            Value("--l5xgit-path") ?? Environment.GetEnvironmentVariable("L5XGIT_PATH"),
            Value("--rslcompare-path") ?? Environment.GetEnvironmentVariable("RSLCOMPARE_PATH"),
            args.Any(a => a.Equals("--no-browser", StringComparison.OrdinalIgnoreCase)));
    }
}
