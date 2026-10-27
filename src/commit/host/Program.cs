using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Channels;

var options = HostOptions.Parse(args);

// Tools -> Commit may be clicked again while this project's visual host is
// intentionally still running. Reuse that live host instead of colliding on
// the fixed localhost port and silently exiting.
if (await TryReuseExistingHostAsync(options))
    return;

var builder = WebApplication.CreateBuilder();
builder.WebHost.UseUrls(options.Url);
var app = builder.Build();

var events = new EventHub();
var operationGate = new SemaphoreSlim(1, 1);
var state = new RuntimeState(options);

var html = ReadEmbedded("index.html")
    .Replace(
        "<head>",
        "<head><script>window.__LogixLiveHost=true;</script>",
        StringComparison.OrdinalIgnoreCase)
    .Replace(
        "</body>",
        "<script src=\"/live.js\"></script></body>",
        StringComparison.OrdinalIgnoreCase);
var liveJs = ReadEmbedded("live.js");

app.MapGet("/", () => Results.Content(html, "text/html; charset=utf-8"));
app.MapGet("/live.js", () => Results.Content(liveJs, "text/javascript; charset=utf-8"));
app.MapGet("/api/meta", BuildMetaAsync);

app.MapPost("/api/pick-folder", async (FolderPickerRequest request) =>
{
    try
    {
        var selected = await PickFolderAsync(request.InitialPath);
        return string.IsNullOrWhiteSpace(selected)
            ? Results.NoContent()
            : Results.Json(new { path = selected });
    }
    catch (Exception ex)
    {
        return Results.BadRequest(new { error = $"Folder picker failed: {ex.Message}" });
    }
});

app.MapGet("/events", async (HttpContext context) =>
{
    context.Response.ContentType = "text/event-stream";
    context.Response.Headers.CacheControl = "no-cache";
    context.Response.Headers.Connection = "keep-alive";

    var subscription = events.Subscribe();
    try
    {
        await foreach (var payload in subscription.Reader.ReadAllAsync(context.RequestAborted))
        {
            await context.Response.WriteAsync($"data: {payload}\n\n", context.RequestAborted);
            await context.Response.Body.FlushAsync(context.RequestAborted);
        }
    }
    catch (OperationCanceledException)
    {
        // Browser disconnected.
    }
    finally
    {
        events.Unsubscribe(subscription.Id);
        ScheduleIdleShutdown();
    }
});

app.MapPost("/api/setup", async (SetupRequest request) =>
{
    if (!operationGate.Wait(0))
        return Results.Conflict(new { error = "Another VCS operation is already running." });

    try
    {
        return await ConfigureProjectAsync(request);
    }
    finally
    {
        operationGate.Release();
        ScheduleIdleShutdown();
    }
});

app.MapPost("/api/commit", (CommitRequest request) =>
{
    if (!operationGate.Wait(0))
        return Results.Conflict(new { error = "Another VCS operation is already running." });

    _ = Task.Run(async () =>
    {
        try { await RunCommitAsync(request); }
        finally
        {
            operationGate.Release();
            ScheduleIdleShutdown();
        }
    });

    return Results.Accepted();
});

app.MapPost("/api/open-repo", async () =>
{
    var repoPath = state.ResolveRepoPath();
    if (repoPath is null)
        return Results.BadRequest(new { error = "This project is not connected to a Git repository yet." });

    var repoRoot = await GitCaptureAsync(repoPath, "rev-parse", "--show-toplevel");
    if (string.IsNullOrWhiteSpace(repoRoot))
        return Results.BadRequest(new { error = "Unable to determine the Git repository root." });

    try
    {
        Process.Start(new ProcessStartInfo(repoRoot)
        {
            UseShellExecute = true
        });
        return Results.Ok(new { path = repoRoot });
    }
    catch (Exception ex)
    {
        return Results.BadRequest(new { error = $"Unable to open the repository folder: {ex.Message}" });
    }
});

app.MapPost("/api/push", () =>
{
    if (!operationGate.Wait(0))
        return Results.Conflict(new { error = "Another VCS operation is already running." });

    _ = Task.Run(async () =>
    {
        try { await RunPushAsync(); }
        finally
        {
            operationGate.Release();
            ScheduleIdleShutdown();
        }
    });

    return Results.Accepted();
});

app.Lifetime.ApplicationStarted.Register(() =>
{
    if (options.NoBrowser) return;
    try
    {
        Process.Start(new ProcessStartInfo(options.Url) { UseShellExecute = true });
    }
    catch
    {
        // Browser launch failure is non-fatal. The host remains available at the configured URL.
    }
});

await app.RunAsync();

void ScheduleIdleShutdown()
{
    _ = Task.Run(async () =>
    {
        // Give normal browser reloads enough time to reconnect before treating the host as abandoned.
        await Task.Delay(TimeSpan.FromSeconds(10));
        if (events.SubscriberCount == 0 && operationGate.CurrentCount > 0)
            app.Lifetime.StopApplication();
    });
}

async Task<object> BuildMetaAsync()
{
    var config = state.LoadConfig();
    var repoPath = state.ResolveRepoPath();
    var repoRoot = repoPath is null ? null : await GitCaptureAsync(repoPath, "rev-parse", "--show-toplevel");
    var branch = repoPath is null ? null : await GitCaptureAsync(repoPath, "rev-parse", "--abbrev-ref", "HEAD");
    if (string.IsNullOrWhiteSpace(branch) && repoPath is not null)
        branch = await GitCaptureAsync(repoPath, "symbolic-ref", "--short", "HEAD");

    // Count only commits that touched this project's configured exploded-content
    // directory. Repository setup/docs commits are not Logix project versions.
    var countText = repoPath is null ? null : await GitCaptureAsync(repoPath, "rev-list", "--count", "HEAD", "--", ".");
    _ = int.TryParse(countText, out var count);

    string? latestSha = null;
    string? latestMessage = null;
    string? latestCommitIso = null;
    var latestChangedFiles = 0;
    if (repoPath is not null && count > 0)
    {
        latestSha = await GitCaptureAsync(repoPath, "log", "-1", "--pretty=%h", "--", ".");
        latestMessage = await GitCaptureAsync(repoPath, "log", "-1", "--pretty=%s", "--", ".");
        latestCommitIso = await GitCaptureAsync(repoPath, "log", "-1", "--date=iso-strict", "--pretty=%cI", "--", ".");

        var changed = await RunGitAsync(repoPath, "diff-tree", "--root", "--no-commit-id", "--name-only", "-r", "HEAD", "--", ".");
        if (changed.ExitCode == 0)
            latestChangedFiles = changed.Output.Count(line => !string.IsNullOrWhiteSpace(line));
    }

    string? originUrl = null;
    string? upstream = null;
    var ahead = 0;
    var behind = 0;
    var published = false;

    if (repoPath is not null)
    {
        originUrl = await GitCaptureAsync(repoPath, "remote", "get-url", "origin");
        upstream = await GitCaptureAsync(repoPath, "rev-parse", "--abbrev-ref", "--symbolic-full-name", "@{u}");

        if (!string.IsNullOrWhiteSpace(upstream) && count > 0)
        {
            var counts = await GitCaptureAsync(repoPath, "rev-list", "--left-right", "--count", $"{upstream}...HEAD");
            var parts = counts?.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (parts is { Length: >= 2 })
            {
                _ = int.TryParse(parts[0], out behind);
                _ = int.TryParse(parts[1], out ahead);
            }
            published = ahead == 0 && behind == 0;
        }
    }

    var projectPath = options.ProjectPath;
    var projectName = string.IsNullOrWhiteSpace(projectPath) ? null : Path.GetFileNameWithoutExtension(projectPath);
    var projectFileName = string.IsNullOrWhiteSpace(projectPath) ? null : Path.GetFileName(projectPath);
    var projectDirectory = string.IsNullOrWhiteSpace(projectPath) ? null : Path.GetDirectoryName(projectPath);

    return new
    {
        live = true,
        projectPath,
        projectName,
        projectFileName,
        projectDirectory,
        configPath = state.ConfigPath,
        configPresent = config is not null,
        promptForCommitMessage = config?.PromptForCommitMessage ?? true,
        repoPath,
        repoRoot,
        branch = string.IsNullOrWhiteSpace(branch) ? null : branch,
        version = count > 0 ? $"V{count}" : null,
        latestSha,
        latestMessage,
        latestCommitIso,
        latestChangedFiles,
        remoteConfigured = !string.IsNullOrWhiteSpace(originUrl),
        originUrl,
        upstream,
        ahead,
        behind,
        published,
        projectLastWriteIso = !string.IsNullOrWhiteSpace(projectPath) && File.Exists(projectPath)
            ? File.GetLastWriteTime(projectPath).ToString("O")
            : null,
        l5xgitConfigured = !string.IsNullOrWhiteSpace(options.L5xGitPath) && File.Exists(options.L5xGitPath)
    };
}

async Task<string?> FindContainingGitRootAsync(string destinationPath)
{
    var probe = Path.GetFullPath(destinationPath);

    // The requested destination may not exist yet. Walk upward until an
    // existing directory is found, then ask Git whether that directory is
    // already part of a repository.
    while (!Directory.Exists(probe))
    {
        var parent = Directory.GetParent(probe);
        if (parent is null)
            return null;

        probe = parent.FullName;
    }

    var root = await GitCaptureAsync(probe, "rev-parse", "--show-toplevel");

    return string.IsNullOrWhiteSpace(root)
        ? null
        : Path.GetFullPath(root);
}

async Task<IResult> ConfigureProjectAsync(SetupRequest request)
{
    if (string.IsNullOrWhiteSpace(options.ProjectPath) || !File.Exists(options.ProjectPath))
        return Results.BadRequest(new { error = "Project path is missing or does not exist." });

    // RepositoryPath is retained in the API contract for compatibility, but
    // it now means the desired Logix-source destination_path.
    var requestedPath = request.RepositoryPath?.Trim().Trim('"');

    if (string.IsNullOrWhiteSpace(requestedPath))
        return Results.BadRequest(new
        {
            error = "Choose the folder where this project's exploded Logix source should live."
        });

    string requestedDestinationPath;

    try
    {
        requestedDestinationPath = Path.GetFullPath(requestedPath);
    }
    catch (Exception ex)
    {
        return Results.BadRequest(new
        {
            error = $"The Logix source folder is not a valid path: {ex.Message}"
        });
    }

    if (File.Exists(requestedDestinationPath))
        return Results.BadRequest(new
        {
            error = "The Logix source destination must be a folder, not a file."
        });

    string repoRoot;
    string destinationPath;

    var existingRoot = await FindContainingGitRootAsync(requestedDestinationPath);

    if (!string.IsNullOrWhiteSpace(existingRoot))
    {
        // Existing larger repositories are explicitly supported.
        // The exact selected folder becomes destination_path.
        repoRoot = Path.GetFullPath(existingRoot);

        var normalizedRoot = repoRoot.TrimEnd(
            Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar);

        var normalizedDestination = requestedDestinationPath.TrimEnd(
            Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar);

        if (string.Equals(
            normalizedRoot,
            normalizedDestination,
            StringComparison.OrdinalIgnoreCase))
        {
            return Results.BadRequest(new
            {
                error =
                    "Choose a project subfolder inside the Git repository, " +
                    "not the repository root itself. Example: 01-vcs\\exploded-content."
            });
        }

        var rootPrefix = normalizedRoot + Path.DirectorySeparatorChar;

        if (!normalizedDestination.StartsWith(
            rootPrefix,
            StringComparison.OrdinalIgnoreCase))
        {
            return Results.BadRequest(new
            {
                error =
                    $"The selected Logix source folder is not inside the detected Git repository '{repoRoot}'."
            });
        }

        destinationPath = requestedDestinationPath;
        Directory.CreateDirectory(destinationPath);
    }
    else
    {
        // Backward-compatible dedicated-repository mode.
        // If no containing Git repository exists, create one at the selected
        // location and put the Logix source in its exploded-content child.
        if (!request.InitializeRepoIfMissing)
        {
            return Results.BadRequest(new
            {
                error =
                    $"No containing Git repository was found for '{requestedDestinationPath}'. " +
                    "Choose a folder inside an existing Git repository or enable creation of a dedicated repository."
            });
        }

        Directory.CreateDirectory(requestedDestinationPath);

        var init = await RunGitInitAsync(requestedDestinationPath);

        if (init.ExitCode != 0)
        {
            return Results.BadRequest(new
            {
                error = init.Errors.LastOrDefault()
                    ?? $"git init exited with code {init.ExitCode}."
            });
        }

        repoRoot = requestedDestinationPath;
        destinationPath = Path.Combine(repoRoot, "exploded-content");

        Directory.CreateDirectory(destinationPath);
    }

    var config = new ConfigInfo(
        destinationPath,
        request.PromptForCommitMessage);

    state.SaveConfig(config);

    events.Publish(new
    {
        type = "raw",
        line = $"Project version-control configuration saved: {state.ConfigPath}"
    });

    events.Publish(new
    {
        type = "raw",
        line = $"Git repository root: {repoRoot}"
    });

    events.Publish(new
    {
        type = "raw",
        line = $"Logix source destination: {destinationPath}"
    });

    var meta = await BuildMetaAsync();

    events.Publish(new
    {
        type = "configured",
        meta
    });

    return Results.Json(meta);
}

async Task RunCommitAsync(CommitRequest request)
{
    events.Publish(new { type = "reset" });

    if (string.IsNullOrWhiteSpace(options.ProjectPath) || !File.Exists(options.ProjectPath))
    {
        events.Publish(new { type = "commit_failure", message = "Project path is missing or does not exist." });
        return;
    }

    if (string.IsNullOrWhiteSpace(options.L5xGitPath) || !File.Exists(options.L5xGitPath))
    {
        events.Publish(new { type = "commit_failure", message = "l5xgit.exe path is not configured or does not exist." });
        return;
    }

    var config = state.LoadConfig();
    if (config is null)
    {
        events.Publish(new
        {
            type = "commit_failure",
            message = "This project has not been connected to a Git repository yet. Complete the one-time project setup shown above, then capture the version."
        });
        return;
    }

    state.RepoPath ??= config.DestinationPath;
    var repoPath = state.ResolveRepoPath();
    if (repoPath is null)
    {
        events.Publish(new { type = "commit_failure", message = "Unable to determine the Git repository path." });
        return;
    }

    var branch = await GitCaptureAsync(repoPath, "rev-parse", "--abbrev-ref", "HEAD");
    if (string.IsNullOrWhiteSpace(branch))
        branch = await GitCaptureAsync(repoPath, "symbolic-ref", "--short", "HEAD");
    if (!string.IsNullOrWhiteSpace(branch))
        events.Publish(new { type = "meta", branch });

    string? successfulSha = null;
    string? lastError = null;
    var noChanges = false;
    var projectName = Path.GetFileNameWithoutExtension(options.ProjectPath);
    var commitMessage = config.PromptForCommitMessage
        ? (string.IsNullOrWhiteSpace(request.Message) ? $"Capture {projectName} changes" : request.Message.Trim())
        : $"l5xcommit User: {Environment.UserName} Workstation: {Environment.MachineName}";

    var psi = new ProcessStartInfo
    {
        FileName = options.L5xGitPath,
        UseShellExecute = false,
        CreateNoWindow = true,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        RedirectStandardInput = true,
        WorkingDirectory = Path.GetDirectoryName(options.L5xGitPath) ?? Environment.CurrentDirectory
    };
    psi.ArgumentList.Add("commit");
    psi.ArgumentList.Add("--acd");
    psi.ArgumentList.Add(options.ProjectPath);

    using var process = new Process { StartInfo = psi };
    if (!process.Start())
    {
        events.Publish(new { type = "commit_failure", message = "Unable to start l5xgit.exe." });
        return;
    }

    // The upstream CLI currently has no --message option. If the saved project config
    // requests a commit message, feed it through redirected stdin before reading output.
    if (config.PromptForCommitMessage)
    {
        await process.StandardInput.WriteLineAsync(commitMessage);
        await process.StandardInput.WriteLineAsync(string.Empty);
        await process.StandardInput.FlushAsync();
    }
    process.StandardInput.Close(); // Unexpected later prompts fail instead of hanging invisibly.

    var stdout = PumpAsync(process.StandardOutput, line =>
    {
        events.Publish(new { type = "raw", line });

        if (line.Contains("Copying ACD to temp path", StringComparison.OrdinalIgnoreCase))
            events.Publish(new { type = "stage", stage = "copy", state = "active", detail = line });
        else if (line.Contains("Converting to", StringComparison.OrdinalIgnoreCase))
        {
            events.Publish(new { type = "stage", stage = "copy", state = "done" });
            events.Publish(new { type = "stage", stage = "export", state = "active", detail = line });
        }
        else if (line.Contains("l5xploding L5X", StringComparison.OrdinalIgnoreCase))
        {
            events.Publish(new { type = "stage", stage = "export", state = "done" });
            events.Publish(new { type = "stage", stage = "explode", state = "active", detail = line });
        }
        else if (line.Contains("Committing to Git repository", StringComparison.OrdinalIgnoreCase))
        {
            events.Publish(new { type = "stage", stage = "explode", state = "done" });
            events.Publish(new { type = "stage", stage = "stage", state = "active", detail = line });
        }
        else if (line.Contains("Changes committed successfully with commit ID:", StringComparison.OrdinalIgnoreCase))
        {
            var match = Regex.Match(line, @"commit ID:\s*([0-9a-fA-F]+)");
            if (match.Success) successfulSha = match.Groups[1].Value;
            events.Publish(new { type = "stage", stage = "stage", state = "done" });
            events.Publish(new { type = "stage", stage = "commit", state = "active" });
        }
        else if (line.Contains("No changes to commit", StringComparison.OrdinalIgnoreCase))
        {
            lastError = "There are no new changes to capture.";
        }
    });

    var stderrLines = new List<string>();
    var stderr = PumpAsync(process.StandardError, line =>
    {
        stderrLines.Add(line);
        if (line.Contains("EmptyCommitException", StringComparison.OrdinalIgnoreCase) ||
            line.Contains("No changes; nothing to commit", StringComparison.OrdinalIgnoreCase))
        {
            noChanges = true;
        }
        else if (!string.IsNullOrWhiteSpace(line))
        {
            lastError = line;
        }
    });

    await process.WaitForExitAsync();
    await Task.WhenAll(stdout, stderr);

    if (noChanges)
    {
        events.Publish(new
        {
            type = "raw",
            line = "No engineering changes found. Git history was not changed."
        });
        events.Publish(new
        {
            type = "commit_no_changes",
            message = "Already up to date. The saved ACD already matches the current Git version. No new version was needed."
        });
        return;
    }

    foreach (var line in stderrLines)
        events.Publish(new { type = "raw", line = "ERROR: " + line });

    if (process.ExitCode != 0 || string.IsNullOrWhiteSpace(successfulSha))
    {
        events.Publish(new
        {
            type = "commit_failure",
            message = lastError ?? $"l5xgit exited with code {process.ExitCode}. See technical details for the raw output."
        });
        return;
    }

    var countText = await GitCaptureAsync(repoPath, "rev-list", "--count", "HEAD", "--", ".");
    _ = int.TryParse(countText, out var count);
    var version = count > 0 ? $"V{count}" : "New commit";
    state.LastCommitSha = successfulSha;
    state.LastVersion = version;

    var changedFilesResult = await RunGitAsync(repoPath, "diff-tree", "--root", "--no-commit-id", "--name-only", "-r", successfulSha, "--", ".");
    var changedFiles = changedFilesResult.ExitCode == 0
        ? changedFilesResult.Output.Count(line => !string.IsNullOrWhiteSpace(line))
        : 0;

    events.Publish(new { type = "stage", stage = "commit", state = "done" });
    events.Publish(new
    {
        type = "commit_success",
        sha = successfulSha,
        version,
        branch,
        message = commitMessage,
        changedFiles
    });
}

async Task RunPushAsync()
{
    var repoPath = state.ResolveRepoPath();
    if (repoPath is null)
    {
        events.Publish(new { type = "push_failure", message = "Unable to determine the Git repository path." });
        return;
    }

    var branch = await GitCaptureAsync(repoPath, "rev-parse", "--abbrev-ref", "HEAD");
    var sha = await GitCaptureAsync(repoPath, "rev-parse", "HEAD");
    var originUrl = await GitCaptureAsync(repoPath, "remote", "get-url", "origin");
    if (string.IsNullOrWhiteSpace(originUrl))
    {
        events.Publish(new { type = "push_failure", message = "No shared Git remote named 'origin' is configured for this repository." });
        return;
    }

    events.Publish(new { type = "push_start", branch, sha, version = state.LastVersion });

    var upstream = await GitCaptureAsync(repoPath, "rev-parse", "--abbrev-ref", "--symbolic-full-name", "@{u}");
    var result = string.IsNullOrWhiteSpace(upstream) && !string.IsNullOrWhiteSpace(branch)
        ? await RunGitAsync(repoPath, "push", "-u", "origin", branch)
        : await RunGitAsync(repoPath, "push");

    foreach (var line in result.Output)
        events.Publish(new { type = "raw", line });
    foreach (var line in result.Errors)
        events.Publish(new { type = "raw", line = "ERROR: " + line });

    if (result.ExitCode != 0)
    {
        events.Publish(new
        {
            type = "push_failure",
            message = result.Errors.LastOrDefault() ?? $"git push exited with code {result.ExitCode}."
        });
        return;
    }

    var countText = await GitCaptureAsync(repoPath, "rev-list", "--count", "HEAD", "--", ".");
    _ = int.TryParse(countText, out var count);
    var version = state.LastVersion ?? (count > 0 ? $"V{count}" : "Published commit");
    events.Publish(new { type = "push_success", version, branch, sha });
}

static async Task PumpAsync(StreamReader reader, Action<string> onLine)
{
    while (await reader.ReadLineAsync() is { } line)
        onLine(line);
}

static async Task<string?> GitCaptureAsync(string repoPath, params string[] gitArgs)
{
    var result = await RunGitAsync(repoPath, gitArgs);
    if (result.ExitCode != 0) return null;
    return result.Output.FirstOrDefault()?.Trim();
}

static async Task<ProcessResult> RunGitAsync(string repoPath, params string[] gitArgs)
{
    var psi = new ProcessStartInfo
    {
        FileName = "git",
        UseShellExecute = false,
        CreateNoWindow = true,
        RedirectStandardOutput = true,
        RedirectStandardError = true
    };
    psi.ArgumentList.Add("-C");
    psi.ArgumentList.Add(repoPath);
    foreach (var arg in gitArgs) psi.ArgumentList.Add(arg);

    using var process = new Process { StartInfo = psi };
    process.Start();
    var outputTask = process.StandardOutput.ReadToEndAsync();
    var errorTask = process.StandardError.ReadToEndAsync();
    await process.WaitForExitAsync();
    var output = (await outputTask).Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
    var errors = (await errorTask).Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
    return new ProcessResult(process.ExitCode, output, errors);
}

static async Task<ProcessResult> RunGitInitAsync(string destinationPath)
{
    var psi = new ProcessStartInfo
    {
        FileName = "git",
        UseShellExecute = false,
        CreateNoWindow = true,
        RedirectStandardOutput = true,
        RedirectStandardError = true
    };
    psi.ArgumentList.Add("init");
    psi.ArgumentList.Add("-b");
    psi.ArgumentList.Add("main");
    psi.ArgumentList.Add(destinationPath);

    using var process = new Process { StartInfo = psi };
    process.Start();
    var outputTask = process.StandardOutput.ReadToEndAsync();
    var errorTask = process.StandardError.ReadToEndAsync();
    await process.WaitForExitAsync();
    var output = (await outputTask).Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
    var errors = (await errorTask).Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
    return new ProcessResult(process.ExitCode, output, errors);
}

static async Task<string?> PickFolderAsync(string? initialPath)
{
    var psi = new ProcessStartInfo
    {
        FileName = "powershell.exe",
        UseShellExecute = false,
        CreateNoWindow = true,
        RedirectStandardOutput = true,
        RedirectStandardError = true
    };

    psi.ArgumentList.Add("-NoProfile");
    psi.ArgumentList.Add("-STA");
    psi.ArgumentList.Add("-ExecutionPolicy");
    psi.ArgumentList.Add("Bypass");
    psi.ArgumentList.Add("-Command");
    psi.ArgumentList.Add(
        "Add-Type -AssemblyName System.Windows.Forms; " +
        "Add-Type -AssemblyName System.Drawing; " +
        "[System.Windows.Forms.Application]::EnableVisualStyles(); " +
        "$owner = New-Object System.Windows.Forms.Form; " +
        "$owner.Text = 'Choose repository folder'; " +
        "$owner.ShowInTaskbar = $false; " +
        "$owner.TopMost = $true; " +
        "$owner.StartPosition = 'CenterScreen'; " +
        "$owner.FormBorderStyle = [System.Windows.Forms.FormBorderStyle]::FixedToolWindow; " +
        "$owner.Size = New-Object System.Drawing.Size(1,1); " +
        "$owner.Opacity = 0.01; " +
        "$owner.Show(); " +
        "$owner.Activate(); " +
        "$d = New-Object System.Windows.Forms.FolderBrowserDialog; " +
        "$d.Description = 'Choose version-control repository folder'; " +
        "$d.ShowNewFolderButton = $true; " +
        "$initial = $env:VCS_PICKER_INITIAL; " +
        "if ($initial) { " +
        "  if (Test-Path -LiteralPath $initial -PathType Container) { $d.SelectedPath = $initial } " +
        "  else { $parent = Split-Path -Parent $initial; if ($parent -and (Test-Path -LiteralPath $parent -PathType Container)) { $d.SelectedPath = $parent } } " +
        "} " +
        "$result = $d.ShowDialog($owner); " +
        "$selected = $d.SelectedPath; " +
        "$owner.Close(); " +
        "$owner.Dispose(); " +
        "if ($result -eq [System.Windows.Forms.DialogResult]::OK) { [Console]::Out.Write($selected) }"
    );

    if (!string.IsNullOrWhiteSpace(initialPath))
        psi.Environment["VCS_PICKER_INITIAL"] = initialPath;

    using var process = new Process { StartInfo = psi };
    if (!process.Start())
        throw new InvalidOperationException("Unable to start the Windows folder picker.");

    var outputTask = process.StandardOutput.ReadToEndAsync();
    var errorTask = process.StandardError.ReadToEndAsync();
    await process.WaitForExitAsync();

    var output = (await outputTask).Trim();
    var error = (await errorTask).Trim();

    if (process.ExitCode != 0)
        throw new InvalidOperationException(string.IsNullOrWhiteSpace(error) ? $"Folder picker exited with code {process.ExitCode}." : error);

    return string.IsNullOrWhiteSpace(output) ? null : Path.GetFullPath(output);
}

static async Task<bool> TryReuseExistingHostAsync(HostOptions options)
{
    try
    {
        using var client = new HttpClient
        {
            Timeout = TimeSpan.FromMilliseconds(700)
        };

        using var response = await client.GetAsync(options.Url.TrimEnd('/') + "/api/meta");
        if (!response.IsSuccessStatusCode)
            return false;

        using var payload = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        if (!payload.RootElement.TryGetProperty("live", out var live) || !live.GetBoolean())
            return false;

        var existingProject = payload.RootElement.TryGetProperty("projectPath", out var project)
            ? project.GetString()
            : null;

        var requestedProject = string.IsNullOrWhiteSpace(options.ProjectPath)
            ? null
            : Path.GetFullPath(options.ProjectPath);

        var existingFullPath = string.IsNullOrWhiteSpace(existingProject)
            ? null
            : Path.GetFullPath(existingProject);

        if (!string.Equals(existingFullPath, requestedProject, StringComparison.OrdinalIgnoreCase))
            return false;

        if (!options.NoBrowser)
        {
            try
            {
                Process.Start(new ProcessStartInfo(options.Url) { UseShellExecute = true });
            }
            catch
            {
                // Existing host is still reusable even if the browser cannot be launched.
            }
        }

        return true;
    }
    catch
    {
        // Nothing usable is listening at the configured URL.
        return false;
    }
}

static string ReadEmbedded(string suffix)
{
    var assembly = Assembly.GetExecutingAssembly();
    var name = assembly.GetManifestResourceNames()
        .Single(n => n.EndsWith(suffix, StringComparison.OrdinalIgnoreCase));
    using var stream = assembly.GetManifestResourceStream(name)
        ?? throw new InvalidOperationException($"Embedded resource '{suffix}' not found.");
    using var reader = new StreamReader(stream);
    return reader.ReadToEnd();
}

sealed record SetupRequest(string? RepositoryPath, bool PromptForCommitMessage, bool InitializeRepoIfMissing);
sealed record FolderPickerRequest(string? InitialPath);
sealed record CommitRequest(string? Message);
sealed record ProcessResult(int ExitCode, string[] Output, string[] Errors);
sealed record Subscription(Guid Id, ChannelReader<string> Reader);

sealed class EventHub
{
    private readonly ConcurrentDictionary<Guid, Channel<string>> subscribers = new();

    public int SubscriberCount => subscribers.Count;

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
        foreach (var channel in subscribers.Values) channel.Writer.TryWrite(payload);
    }
}

sealed class RuntimeState
{
    private readonly HostOptions options;
    public string? RepoPath { get; set; }
    public string? LastCommitSha { get; set; }
    public string? LastVersion { get; set; }

    public RuntimeState(HostOptions options)
    {
        this.options = options;
        RepoPath = options.RepoPath;
    }

    public string? ConfigPath
    {
        get
        {
            if (string.IsNullOrWhiteSpace(options.ProjectPath)) return null;
            var directory = Path.GetDirectoryName(options.ProjectPath) ?? string.Empty;
            return Path.Combine(directory, $"{Path.GetFileNameWithoutExtension(options.ProjectPath)}_L5xGit.yml");
        }
    }

    public ConfigInfo? LoadConfig()
    {
        var file = ConfigPath;
        return string.IsNullOrWhiteSpace(file) ? null : ConfigInfo.Load(file);
    }

    public void SaveConfig(ConfigInfo config)
    {
        var file = ConfigPath ?? throw new InvalidOperationException("Unable to determine the L5xGit config path for this project.");
        var escapedPath = config.DestinationPath.Replace("'", "''");
        var yaml =
            $"destination_path: '{escapedPath}'{Environment.NewLine}" +
            $"prompt_for_commit_message: {config.PromptForCommitMessage.ToString().ToLowerInvariant()}{Environment.NewLine}";
        File.WriteAllText(file, yaml);
        RepoPath = config.DestinationPath;
    }

    public string? ResolveRepoPath()
    {
        if (!string.IsNullOrWhiteSpace(RepoPath)) return RepoPath;
        var config = LoadConfig();
        if (config is null) return null;
        RepoPath = config.DestinationPath;
        return RepoPath;
    }
}

sealed record ConfigInfo(string DestinationPath, bool PromptForCommitMessage)
{
    public static ConfigInfo? Load(string file)
    {
        if (!File.Exists(file)) return null;
        string? destination = null;
        var prompt = false;

        foreach (var raw in File.ReadLines(file))
        {
            var line = raw.Trim();
            if (line.StartsWith("destination_path:", StringComparison.OrdinalIgnoreCase))
                destination = CleanValue(line[(line.IndexOf(':') + 1)..]);
            else if (line.StartsWith("prompt_for_commit_message:", StringComparison.OrdinalIgnoreCase))
                _ = bool.TryParse(CleanValue(line[(line.IndexOf(':') + 1)..]), out prompt);
        }

        return string.IsNullOrWhiteSpace(destination) ? null : new ConfigInfo(destination, prompt);
    }

    private static string CleanValue(string value) => value.Trim().Trim('"', '\'');
}

sealed record HostOptions(string Url, string? ProjectPath, string? L5xGitPath, string? RepoPath, bool NoBrowser)
{
    public static HostOptions Parse(string[] args)
    {
        string? Value(string name)
        {
            var index = Array.FindIndex(args, a => a.Equals(name, StringComparison.OrdinalIgnoreCase));
            return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
        }

        var url = Value("--url") ?? "http://127.0.0.1:5187";
        var projectPath = Value("--project-path");
        var l5xgitPath = Value("--l5xgit-path") ?? Environment.GetEnvironmentVariable("L5XGIT_PATH");
        var repoPath = Value("--repo-path");
        var noBrowser = args.Any(a => a.Equals("--no-browser", StringComparison.OrdinalIgnoreCase));
        return new HostOptions(url, projectPath, l5xgitPath, repoPath, noBrowser);
    }
}
