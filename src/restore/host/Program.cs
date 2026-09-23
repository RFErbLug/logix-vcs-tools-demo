using System.Diagnostics;
using System.Reflection;
using System.Text.Json;

var options = HostOptions.Parse(args);

if (await TryReuseExistingHostAsync(options))
    return;

CleanupOldRestoreState();

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls(options.Url);
var app = builder.Build();

var sync = new object();
Process? activeProcess = null;
string? activeStatePath = null;
string? activeLogPath = null;

var html = ReadEmbedded("index.html");

app.MapGet("/", () => Results.Content(html, "text/html"));
app.MapGet("/api/meta", BuildMetaAsync);

app.MapGet("/api/restore-state", () =>
{
    Process? process;
    string? statePath;
    string? logPath;

    lock (sync)
    {
        process = activeProcess;
        statePath = activeStatePath;
        logPath = activeLogPath;
    }

    var active = process is { HasExited: false };
    var log = string.Empty;
    if (!string.IsNullOrWhiteSpace(logPath) && File.Exists(logPath))
    {
        try { log = File.ReadAllText(logPath); }
        catch { }
    }

    if (string.IsNullOrWhiteSpace(statePath) || !File.Exists(statePath))
    {
        return Results.Json(new
        {
            active,
            stage = active ? "starting" : null,
            status = active ? "running" : null,
            message = active ? "Starting restore..." : null,
            detail = (string?)null,
            backup_path = (string?)null,
            log
        });
    }

    try
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(statePath));
        var root = doc.RootElement;

        string? ReadString(string name) =>
            root.TryGetProperty(name, out var value) && value.ValueKind != JsonValueKind.Null
                ? value.GetString()
                : null;

        return Results.Json(new
        {
            active,
            stage = ReadString("stage"),
            status = ReadString("status"),
            message = ReadString("message"),
            detail = ReadString("detail"),
            timestamp = ReadString("timestamp"),
            project_path = ReadString("project_path"),
            commit_sha = ReadString("commit_sha"),
            version = ReadString("version"),
            backup_path = ReadString("backup_path"),
            log
        });
    }
    catch (Exception ex)
    {
        return Results.Json(new
        {
            active,
            stage = active ? "starting" : "failed",
            status = active ? "running" : "failed",
            message = active ? "Restore is starting..." : "Restore state could not be read.",
            detail = ex.Message,
            backup_path = (string?)null,
            log
        });
    }
});

app.MapPost("/api/restore", async (RestoreRequest request) =>
{
    lock (sync)
    {
        if (activeProcess is { HasExited: false })
            return Results.Conflict(new { error = "Another restore is already running." });
    }

    if (string.IsNullOrWhiteSpace(options.ProjectPath) || !File.Exists(options.ProjectPath))
        return Results.BadRequest(new { error = "The current ACD path is missing or does not exist." });

    if (string.IsNullOrWhiteSpace(options.L5xGitPath) || !File.Exists(options.L5xGitPath))
        return Results.BadRequest(new { error = "l5xgit.exe is not configured or does not exist." });

    var configPath = GetConfigPath(options.ProjectPath);
    var config = ConfigInfo.Load(configPath);
    if (config is null)
        return Results.BadRequest(new { error = "This project is not connected to version control. Capture a version first." });

    var destinationPath = Path.GetFullPath(config.DestinationPath);
    var repoRoot = await GitCaptureAsync(destinationPath, "rev-parse", "--show-toplevel");
    if (string.IsNullOrWhiteSpace(repoRoot))
        return Results.BadRequest(new { error = "Unable to determine the Git repository root." });

    var verify = await RunGitAsync(repoRoot, "cat-file", "-e", $"{request.Sha}^{{commit}}");
    if (verify.ExitCode != 0)
        return Results.BadRequest(new { error = $"Git version '{request.Sha}' could not be resolved." });

    var workerPath = Path.Combine(AppContext.BaseDirectory, "restore-worker.ps1");
    var prepareOpenPath = Path.Combine(AppContext.BaseDirectory, "prepare-restored-open.ps1");

    if (!File.Exists(workerPath))
        return Results.BadRequest(new { error = $"Restore worker was not found: {workerPath}" });

    var stateRoot = Path.Combine(Path.GetTempPath(), "LogixVcsTools", "RestoreState");
    Directory.CreateDirectory(stateRoot);
    var id = DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N")[..8];
    var statePath = Path.Combine(stateRoot, $"restore-{id}.json");
    var logPath = Path.ChangeExtension(statePath, ".log");

    var psi = new ProcessStartInfo
    {
        FileName = "powershell.exe",
        UseShellExecute = false,
        CreateNoWindow = true,
        WorkingDirectory = AppContext.BaseDirectory
    };

    foreach (var arg in new[]
    {
        "-NoProfile",
        "-ExecutionPolicy", "Bypass",
        "-File", workerPath,
        "-ProjectPath", options.ProjectPath,
        "-L5xGitPath", options.L5xGitPath,
        "-RepoRoot", repoRoot,
        "-DestinationPath", destinationPath,
        "-CommitSha", request.Sha,
        "-StatePath", statePath,
        "-Version", string.IsNullOrWhiteSpace(request.Version) ? request.Sha[..Math.Min(7, request.Sha.Length)] : request.Version,
        "-PrepareOpenPath", prepareOpenPath
    })
    {
        psi.ArgumentList.Add(arg);
    }

    Process process;
    try
    {
        process = Process.Start(psi)
            ?? throw new InvalidOperationException("Windows could not start the restore worker.");
    }
    catch (Exception ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }

    lock (sync)
    {
        activeProcess = process;
        activeStatePath = statePath;
        activeLogPath = logPath;
    }

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

    string? destinationPath = null;
    string? repoRoot = null;
    var versions = new List<object>();
    var total = 0;

    if (config is not null)
    {
        destinationPath = Path.GetFullPath(config.DestinationPath);
        if (Directory.Exists(destinationPath))
            repoRoot = await GitCaptureAsync(destinationPath, "rev-parse", "--show-toplevel");
    }

    if (!string.IsNullOrWhiteSpace(destinationPath) && !string.IsNullOrWhiteSpace(repoRoot))
    {
        var totalText = await GitCaptureAsync(destinationPath, "rev-list", "--count", "HEAD", "--", ".");
        _ = int.TryParse(totalText, out total);

        var logResult = await RunGitAsync(
            destinationPath,
            "log", "-20", "--date=iso-strict", "--pretty=%H%x09%h%x09%s%x09%cI", "--", ".");

        if (logResult.ExitCode == 0)
        {
            var index = 0;
            foreach (var line in logResult.Output)
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

    var workerPath = Path.Combine(AppContext.BaseDirectory, "restore-worker.ps1");

    string? problem = null;
    if (string.IsNullOrWhiteSpace(projectPath) || !File.Exists(projectPath))
        problem = "The current ACD path is missing or does not exist.";
    else if (config is null)
        problem = "This project is not connected to version control. Capture a version first.";
    else if (string.IsNullOrWhiteSpace(repoRoot))
        problem = "The Git repository could not be resolved.";
    else if (string.IsNullOrWhiteSpace(options.L5xGitPath) || !File.Exists(options.L5xGitPath))
        problem = "l5xgit.exe is not configured or does not exist.";
    else if (!File.Exists(workerPath))
        problem = "The restore worker is missing from the installed host.";

    return new
    {
        live = true,
        projectPath,
        projectFileName = string.IsNullOrWhiteSpace(projectPath) ? null : Path.GetFileName(projectPath),
        configPath,
        configPresent = config is not null,
        destinationPath,
        repoRoot,
        version = total > 0 ? $"V{total}" : null,
        l5xgitPath = options.L5xGitPath,
        ready = problem is null,
        problem,
        versions
    };
}

static string? GetConfigPath(string? projectPath)
{
    if (string.IsNullOrWhiteSpace(projectPath)) return null;
    var directory = Path.GetDirectoryName(projectPath) ?? string.Empty;
    return Path.Combine(directory, $"{Path.GetFileNameWithoutExtension(projectPath)}_L5xGit.yml");
}

static async Task<string?> GitCaptureAsync(string repoPath, params string[] args)
{
    var result = await RunGitAsync(repoPath, args);
    return result.ExitCode == 0 ? result.Output.FirstOrDefault()?.Trim() : null;
}

static async Task<ProcessResult> RunGitAsync(string repoPath, params string[] args)
{
    var allArgs = new List<string> { "-C", repoPath };
    allArgs.AddRange(args);

    var psi = new ProcessStartInfo
    {
        FileName = "git",
        UseShellExecute = false,
        CreateNoWindow = true,
        RedirectStandardOutput = true,
        RedirectStandardError = true
    };

    foreach (var arg in allArgs) psi.ArgumentList.Add(arg);

    using var process = new Process { StartInfo = psi };
    if (!process.Start())
        return new ProcessResult(-1, [], ["Could not start git."]);

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

static void CleanupOldRestoreState()
{
    try
    {
        var root = Path.Combine(Path.GetTempPath(), "LogixVcsTools", "RestoreState");
        if (!Directory.Exists(root)) return;

        foreach (var file in Directory.GetFiles(root))
        {
            try
            {
                if (File.GetLastWriteTime(file) < DateTime.Now.AddDays(-2))
                    File.Delete(file);
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
        if (!doc.RootElement.TryGetProperty("projectPath", out var prop)) return false;

        var existingProject = prop.GetString();
        if (!string.Equals(
                Path.GetFullPath(existingProject ?? string.Empty),
                Path.GetFullPath(options.ProjectPath ?? string.Empty),
                StringComparison.OrdinalIgnoreCase))
            return false;

        if (!options.NoBrowser)
            Process.Start(new ProcessStartInfo(options.Url) { UseShellExecute = true });

        return true;
    }
    catch
    {
        return false;
    }
}

sealed record RestoreRequest(string Sha, string? Version);
sealed record ProcessResult(int ExitCode, string[] Output, string[] Errors);

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

sealed record HostOptions(string Url, string? ProjectPath, string? L5xGitPath, bool NoBrowser)
{
    public static HostOptions Parse(string[] args)
    {
        string? Value(string name)
        {
            var index = Array.FindIndex(args, a => a.Equals(name, StringComparison.OrdinalIgnoreCase));
            return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
        }

        return new HostOptions(
            Value("--url") ?? "http://127.0.0.1:5192",
            Value("--project-path"),
            Value("--l5xgit-path") ?? Environment.GetEnvironmentVariable("L5XGIT_PATH"),
            args.Any(a => a.Equals("--no-browser", StringComparison.OrdinalIgnoreCase)));
    }
}
