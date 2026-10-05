using System.Diagnostics;
using System.Text;

namespace TerminalHub.Official;

public sealed record CommandResult(string Executable, IReadOnlyList<string> Arguments, string Directory,
    int ExitCode, string Output, string Error)
{
    public string Display => Executable + " " + string.Join(" ", Arguments.Select(a => a.Contains(' ') ? "\"" + a + "\"" : a))
        + "\n" + Directory + "\n" + Output.Replace('\0', '\n') + Error + "\nExit code: " + ExitCode;
    public CommandResult RequireSuccess()
    {
        if (ExitCode != 0) throw new InvalidOperationException(Error.Length > 0 ? Error.Trim() : Output.Length > 0 ? Output.Trim() : Display);
        return this;
    }
}

public static class PluginProcess
{
    public static async Task<CommandResult> RunAsync(string executable, IEnumerable<string> arguments,
        string directory, CancellationToken ct, string? input = null)
    {
        if (!System.IO.Directory.Exists(directory)) throw new DirectoryNotFoundException("Choose an existing working directory: " + directory);
        var args = arguments.ToArray();
        var info = new ProcessStartInfo(executable)
        {
            WorkingDirectory = directory, UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true, RedirectStandardInput = true,
            StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8,
            StandardInputEncoding = new UTF8Encoding(false)
        };
        foreach (var arg in args) info.ArgumentList.Add(arg);
        info.Environment["GIT_TERMINAL_PROMPT"] = "0";
        info.Environment["GCM_INTERACTIVE"] = "never";
        info.Environment["GH_PROMPT_DISABLED"] = "1";
        info.Environment["GH_PAGER"] = "";
        info.Environment["GIT_PAGER"] = "";
        ct.ThrowIfCancellationRequested();
        using var process = Process.Start(info) ?? throw new InvalidOperationException("Cannot start " + executable);
        using var registration = ct.Register(() =>
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
            catch (InvalidOperationException) { }
        });
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        if (input is not null) await process.StandardInput.WriteAsync(input.AsMemory(), ct);
        process.StandardInput.Close();
        await process.WaitForExitAsync(CancellationToken.None);
        var output = await stdout; var error = await stderr;
        ct.ThrowIfCancellationRequested();
        return new(executable, args, directory, process.ExitCode, output, error);
    }
}
