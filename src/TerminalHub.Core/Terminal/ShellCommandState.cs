namespace TerminalHub.Core.Terminal;

public sealed record ShellCommandState(bool Running, int? ExitCode, TimeSpan Duration);
