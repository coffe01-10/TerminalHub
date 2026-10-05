using System.Text.Json;

namespace TerminalHub.Official.TaskRunner;

public enum TaskSourceKind { PackageJson, Makefile, Justfile, VsCode }

/// <summary>One runnable row parsed from a project task file.</summary>
public sealed record ProjectTask(TaskSourceKind Source, string Name, string WorkingDirectory, string Command);

/// <summary>Pure readers for the four task files. Input is file text; nothing here touches the host or the filesystem.</summary>
public static class TaskSources
{
    public static IReadOnlyList<ProjectTask> ParsePackageJson(string text, string projectDirectory)
    {
        using var doc = JsonDocument.Parse(text);
        if (!doc.RootElement.TryGetProperty("scripts", out var scripts) || scripts.ValueKind != JsonValueKind.Object)
            return [];
        var tasks = new List<ProjectTask>();
        foreach (var script in scripts.EnumerateObject())
        {
            if (script.Value.ValueKind != JsonValueKind.String) continue;
            tasks.Add(new(TaskSourceKind.PackageJson, script.Name, projectDirectory, NpmRun(script.Name, script.Value.GetString())));
        }
        return tasks;
    }

    public static IReadOnlyList<ProjectTask> ParseMakefile(string text, string projectDirectory)
    {
        var tasks = new List<ProjectTask>();
        foreach (var raw in Lines(text))
        {
            var line = raw.TrimEnd();
            if (line.Length == 0 || line[0] is ' ' or '\t' or '#' or '.') continue;
            var colon = line.IndexOf(':');
            if (colon <= 0) continue;
            var name = line[..colon].Trim();
            if (name.Length == 0 || name.Contains('%') || name.Contains('=')) continue;
            tasks.Add(new(TaskSourceKind.Makefile, name, projectDirectory, Make(name)));
        }
        return tasks;
    }

    public static IReadOnlyList<ProjectTask> ParseJustfile(string text, string projectDirectory)
    {
        var tasks = new List<ProjectTask>();
        foreach (var raw in Lines(text))
        {
            var line = raw.TrimEnd();
            if (line.Length == 0 || line[0] is ' ' or '\t' || line[0] == '#' || line.StartsWith('.')) continue;
            var name = line.Split([' ', '\t', ':', '='], 2)[0];
            if (name.Length == 0 || !IsRecipeName(name)) continue;
            tasks.Add(new(TaskSourceKind.Justfile, name, projectDirectory, Just(name)));
        }
        return tasks;
    }

    public static IReadOnlyList<ProjectTask> ParseVsCodeTasks(string text, string projectDirectory)
    {
        using var doc = JsonDocument.Parse(text);
        if (!doc.RootElement.TryGetProperty("tasks", out var tasks) || tasks.ValueKind != JsonValueKind.Array)
            return [];
        var result = new List<ProjectTask>();
        foreach (var task in tasks.EnumerateArray())
        {
            if (task.ValueKind != JsonValueKind.Object) continue;
            if (!task.TryGetProperty("type", out var type) || type.ValueKind != JsonValueKind.String || type.GetString() != "shell") continue;
            if (!task.TryGetProperty("command", out var command) || command.ValueKind != JsonValueKind.String) continue;
            var commandText = command.GetString() ?? "";
            if (commandText.Length == 0) continue;
            var args = new List<string>();
            if (task.TryGetProperty("args", out var argList) && argList.ValueKind == JsonValueKind.Array)
            {
                foreach (var arg in argList.EnumerateArray())
                    if (arg.ValueKind == JsonValueKind.String && arg.GetString() is { Length: > 0 } value) args.Add(value);
            }
            var cwd = projectDirectory;
            if (task.TryGetProperty("options", out var options) && options.ValueKind == JsonValueKind.Object
                && options.TryGetProperty("cwd", out var cwdValue) && cwdValue.ValueKind == JsonValueKind.String
                && cwdValue.GetString() is { Length: > 0 } cwdText)
                cwd = ResolveDirectory(cwdText, projectDirectory);
            var label = task.TryGetProperty("label", out var labelValue) && labelValue.ValueKind == JsonValueKind.String
                ? labelValue.GetString() ?? "" : "";
            if (label.Length == 0) label = commandText;
            result.Add(new(TaskSourceKind.VsCode, label, cwd, Expand(commandText, projectDirectory, args)));
        }
        return result;
    }

    /// <summary>What a click actually runs. npm/make/just are tool invocations, not the script body.</summary>
    public static string CommandFor(ProjectTask task) => task.Source switch
    {
        TaskSourceKind.PackageJson => task.Command,
        TaskSourceKind.Makefile => Make(task.Name),
        TaskSourceKind.Justfile => Just(task.Name),
        _ => task.Command
    };

    /// <summary>Newlines become one space. PasteText turns a raw newline into Enter, which would split one task into several commands.</summary>
    public static string FoldNewlines(string command)
        => string.Join(' ', command.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n', StringSplitOptions.RemoveEmptyEntries)).Trim();

    public static (string Shell, string Arguments) OneShotSession(string command, bool windows)
    {
        var folded = FoldNewlines(command);
        // No surrounding quotes: cmd /s would strip them and a folded command that itself
        // contains quotes (npm run "test app") would be left unbalanced.
        return windows
            ? ("cmd.exe", "/d /s /c " + folded)
            : ("sh", "-c '" + folded.Replace("'", "'\"'\"'") + "'");
    }

    public static string SourceLabel(TaskSourceKind source) => source switch
    {
        TaskSourceKind.PackageJson => "package.json",
        TaskSourceKind.Makefile => "Makefile",
        TaskSourceKind.Justfile => "justfile",
        _ => ".vscode/tasks.json"
    };

    /// <summary>The first script line is the body npm itself runs. Later lines are extra arguments on <c>npm run</c>, so a folded paste still carries flags such as --watch.</summary>
    private static string NpmRun(string name, string? body = null)
    {
        var command = "npm run " + Quote(name);
        if (string.IsNullOrEmpty(body)) return command;
        var lines = body.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n')
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return lines.Length <= 1 ? command : command + " " + string.Join(' ', lines.Skip(1));
    }
    private static string Make(string name) => "make " + Quote(name);
    private static string Just(string name) => "just " + Quote(name);

    private static string Quote(string name) => name.Any(char.IsWhiteSpace) ? "\"" + name + "\"" : name;

    private static string Expand(string command, string projectDirectory, IReadOnlyList<string> args)
    {
        var parts = new List<string> { Substitute(command, projectDirectory) };
        parts.AddRange(args.Select(arg => Substitute(arg, projectDirectory)));
        return string.Join(' ', parts);
    }

    private static string Substitute(string value, string projectDirectory)
        => value.Replace("${workspaceFolder}", projectDirectory, StringComparison.Ordinal);

    private static string ResolveDirectory(string cwd, string projectDirectory)
    {
        var expanded = Substitute(cwd, projectDirectory);
        if (Path.IsPathRooted(expanded)) return expanded;
        return Path.GetFullPath(Path.Combine(projectDirectory, expanded));
    }

    private static bool IsRecipeName(string name)
    {
        if (name is "set" or "export" or "import" or "mod" or "alias") return false;
        return name.All(c => char.IsLetterOrDigit(c) || c is '_' or '-');
    }

    private static string[] Lines(string text) => text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
}
