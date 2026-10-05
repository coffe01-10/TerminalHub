using System.Text.Json;

namespace TerminalHub.Official.GitWorkbench;

public sealed record GitChange(string Path, string? OriginalPath, char Index, char Worktree)
{
    public bool Untracked => Index == '?' && Worktree == '?';
    public bool Conflict => Index == 'U' || Worktree == 'U' || (Index == 'A' && Worktree == 'A') || (Index == 'D' && Worktree == 'D');
    public bool Staged => !Untracked && !Conflict && Index != ' ';
    public bool Unstaged => !Untracked && !Conflict && Worktree != ' ';
}
public sealed record GitSnapshot(string Root, string Branch, bool HasHead, string[] Branches, string[] Remotes,
    string UpstreamRemote, string UpstreamBranch, string AheadBehind, IReadOnlyList<GitChange> Changes);
public sealed record GitCommit(string Id, string Subject)
{
    public override string ToString() => Id + "  " + Subject;
}
public sealed record GitHubItem(int Number, string Title, string Url, string State)
{
    public override string ToString() => "#" + Number + "  " + Title + "  [" + State + "]";
}
public sealed record GitHubCheck(string Name, string State, string Bucket, string Link)
{
    public override string ToString() => Name + "  [" + Bucket + ": " + State + "]";
}
public delegate Task<CommandResult> PluginCommandRunner(string executable, IEnumerable<string> arguments,
    string directory, CancellationToken ct, string? input);

public sealed class GitWorkbenchClient(PluginCommandRunner? runner = null)
{
    private readonly PluginCommandRunner _runner = runner ?? PluginProcess.RunAsync;
    public event Action<CommandResult>? CommandCompleted;
    private async Task<CommandResult> Run(string executable, string root, IEnumerable<string> args, CancellationToken ct, string? input = null)
    {
        var result = await _runner(executable, args, root, ct, input);
        CommandCompleted?.Invoke(result); return result;
    }
    public Task<CommandResult> Git(string root, CancellationToken ct, params string[] args)
        => Run("git", root, new[] { "--no-pager", "--literal-pathspecs", "-c", "color.ui=false", "-c", "core.quotepath=false" }.Concat(args), ct);
    public Task<CommandResult> Gh(string root, CancellationToken ct, params string[] args) => Run("gh", root, args, ct);
    public static IReadOnlyList<GitChange> ParseStatus(string output)
    {
        var records = output.Split('\0'); var changes = new List<GitChange>();
        for (var i = 0; i < records.Length; i++)
        {
            var record = records[i]; if (record.Length < 4) continue;
            var index = record[0]; var worktree = record[1];
            var original = (index is 'R' or 'C' || worktree is 'R' or 'C') ? records[++i] : null;
            changes.Add(new(record[3..], original, index, worktree));
        }
        return changes;
    }
    public async Task<GitSnapshot> Read(string directory, CancellationToken ct)
    {
        var root = (await Git(directory, ct, "rev-parse", "--show-toplevel")).RequireSuccess().Output.TrimEnd('\r', '\n');
        var branchResult = await Git(root, ct, "symbolic-ref", "--quiet", "--short", "HEAD");
        var branch = branchResult.ExitCode == 0 ? branchResult.Output.Trim() : "";
        var hasHead = (await Git(root, ct, "rev-parse", "--verify", "HEAD")).ExitCode == 0;
        var branches = Lines((await Git(root, ct, "for-each-ref", "--format=%(refname:short)", "refs/heads")).RequireSuccess().Output);
        var remotes = Lines((await Git(root, ct, "remote")).RequireSuccess().Output);
        var upstream = branch.Length == 0 ? [] : (await Git(root, ct, "for-each-ref",
            "--format=%(upstream:remotename)%09%(upstream:remoteref)", "refs/heads/" + branch)).RequireSuccess().Output.TrimEnd('\r', '\n').Split('\t');
        var remote = upstream.Length > 0 ? upstream[0] : "";
        var target = upstream.Length > 1 ? upstream[1].Replace("refs/heads/", "") : "";
        var ahead = remote.Length > 0 && hasHead ? (await Git(root, ct, "rev-list", "--left-right", "--count", "HEAD...@{upstream}")).RequireSuccess().Output.Trim() : "";
        var changes = ParseStatus((await Git(root, ct, "status", "--porcelain=v1", "-z", "--untracked-files=all")).RequireSuccess().Output);
        return new(root, branch, hasHead, branches, remotes, remote, target, ahead, changes);
    }
    private static string[] Lines(string text) => text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
    public async Task Stage(string root, GitChange? change, CancellationToken ct)
    {
        var paths = change is null ? new[] { "." } : change.Worktree is 'R' or 'C' && change.OriginalPath is { } original
            ? new[] { change.Path, original } : [change.Path];
        (await Git(root, ct, new[] { "add", "--" }.Concat(paths).ToArray())).RequireSuccess();
    }
    public async Task Unstage(GitSnapshot state, GitChange? change, CancellationToken ct)
    {
        var paths = change is null ? new[] { "." } : change.OriginalPath is { } original ? new[] { change.Path, original } : [change.Path];
        var command = state.HasHead ? new[] { "restore", "--staged", "--" } : ["rm", "--cached", "-r", "--ignore-unmatch", "--"];
        (await Git(state.Root, ct, command.Concat(paths).ToArray())).RequireSuccess();
    }
    public async Task Commit(string root, string message, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(message)) throw new InvalidOperationException("Commit message is required.");
        (await Run("git", root, ["--no-pager", "commit", "--file=-"], ct, message)).RequireSuccess();
    }
    public async Task<string> Diff(string root, GitChange change, bool staged, CancellationToken ct)
    {
        if (change.Untracked)
        {
            using var reader = new StreamReader(System.IO.Path.Combine(root, change.Path));
            var chars = new char[128 * 1024]; var count = await reader.ReadBlockAsync(chars.AsMemory(), ct);
            var text = new string(chars, 0, count);
            return text.Contains('\0') ? "Binary file" : text + (count == chars.Length ? "\n[Preview limited to 128K characters]" : "");
        }
        var args = new List<string> { "diff", "--no-ext-diff", "--no-textconv" };
        if (staged) args.Add("--cached"); args.Add("--"); args.Add(change.Path);
        if (change.OriginalPath is { } original) args.Add(original);
        return (await Git(root, ct, args.ToArray())).RequireSuccess().Output;
    }
    public async Task<IReadOnlyList<GitCommit>> History(GitSnapshot state, CancellationToken ct, int limit = 20)
    {
        if (!state.HasHead) return [];
        return Lines((await Git(state.Root, ct, "log", "-" + limit, "--format=%h%x09%s")).RequireSuccess().Output)
            .Select(line => line.Split('\t', 2)).Select(parts => new GitCommit(parts[0], parts.Length > 1 ? parts[1] : "")).ToArray();
    }
    public async Task Fetch(string root, string remote, CancellationToken ct) => (await Git(root, ct, "fetch", "--", remote)).RequireSuccess();
    public async Task Pull(string root, string remote, string branch, CancellationToken ct)
        => (await Git(root, ct, "pull", "--no-edit", "--", remote, branch)).RequireSuccess();
    public async Task Push(GitSnapshot state, string remote, string target, CancellationToken ct)
    {
        if (state.Branch.Length == 0) throw new InvalidOperationException("Create or switch to a branch before pushing.");
        (await Git(state.Root, ct, "push", "--set-upstream", "--", remote, state.Branch + ":refs/heads/" + target)).RequireSuccess();
    }
    public async Task Switch(string root, string branch, bool create, CancellationToken ct)
        => (await Git(root, ct, create ? ["switch", "-c", branch] : ["switch", "--", branch])).RequireSuccess();
    public async Task Init(string directory, CancellationToken ct) => (await Git(directory, ct, "init")).RequireSuccess();

    public async Task<IReadOnlyList<GitHubItem>> ListGitHub(string root, bool issues, CancellationToken ct)
    {
        var result = (await Gh(root, ct, issues ? "issue" : "pr", "list", "--limit", "30", "--json", "number,title,url,state")).RequireSuccess();
        return ParseItems(result.Output);
    }
    public static IReadOnlyList<GitHubItem> ParseItems(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.EnumerateArray().Select(e => new GitHubItem(e.GetProperty("number").GetInt32(),
            e.GetProperty("title").GetString() ?? "", e.GetProperty("url").GetString() ?? "", e.GetProperty("state").GetString() ?? "")).ToArray();
    }
    public async Task<IReadOnlyList<GitHubCheck>> Checks(string root, string? number, CancellationToken ct)
    {
        var args = new List<string> { "pr", "checks" }; if (number is not null) args.Add(number);
        args.AddRange(["--json", "name,state,bucket,link"]);
        var result = await Gh(root, ct, args.ToArray());
        // gh uses nonzero exits for failed/pending checks while still returning a valid result array.
        if (!result.Output.TrimStart().StartsWith('[')) result.RequireSuccess();
        using var document = JsonDocument.Parse(result.Output);
        return document.RootElement.EnumerateArray().Select(e => new GitHubCheck(e.GetProperty("name").GetString() ?? "",
            e.GetProperty("state").GetString() ?? "", e.GetProperty("bucket").GetString() ?? "", e.GetProperty("link").GetString() ?? "")).ToArray();
    }
    public async Task<string> RepoUrl(string root, CancellationToken ct)
    {
        var result = (await Gh(root, ct, "repo", "view", "--json", "url")).RequireSuccess();
        using var document = JsonDocument.Parse(result.Output); return document.RootElement.GetProperty("url").GetString()!;
    }
    public async Task<string> CurrentPrUrl(string root, CancellationToken ct)
    {
        var result = (await Gh(root, ct, "pr", "view", "--json", "url")).RequireSuccess();
        using var document = JsonDocument.Parse(result.Output); return document.RootElement.GetProperty("url").GetString()!;
    }
    public async Task<string> CreateDraft(GitSnapshot state, string title, string body, string baseBranch, CancellationToken ct)
    {
        if (state.Branch.Length == 0 || string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(baseBranch))
            throw new InvalidOperationException("A branch, title and base branch are required.");
        return (await Run("gh", state.Root, ["pr", "create", "--draft", "--head", state.Branch, "--base", baseBranch,
            "--title", title, "--body-file", "-"], ct, body)).RequireSuccess().Output.Trim();
    }
    public async Task IssueBranch(string root, int issue, string name, string baseBranch, CancellationToken ct)
        => (await Gh(root, ct, "issue", "develop", issue.ToString(), "--name", name, "--base", baseBranch, "--checkout")).RequireSuccess();
}
