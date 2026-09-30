namespace TerminalHub.Core.Terminal;

/// <summary>One command delimited by OSC 133. Without that protocol, nothing is recorded.</summary>
public sealed class CommandRecord
{
    public int Id { get; init; }
    public string Command { get; init; } = "";
    public bool HasCommandText { get; init; }
    public string WorkingDirectory { get; init; } = "";
    public bool Running { get; private set; } = true;
    public bool CompletionKnown { get; private set; }
    public int? ExitCode { get; private set; }
    public TimeSpan? Duration { get; private set; }
    public BufferAnchor Start { get; init; } = null!;
    public BufferAnchor? End { get; private set; }
    public int EndColumn { get; private set; }

    internal void Finish(int? exitCode, TimeSpan duration, BufferAnchor end, int endColumn)
    {
        Running = false;
        CompletionKnown = true;
        ExitCode = exitCode;
        Duration = duration;
        End = end;
        EndColumn = endColumn;
    }

    public int? LocateLine(ScreenBuffer buffer)
    {
        var line = buffer.ResolveAnchor(Start);
        return line;
    }

    /// <summary>Command text plus the following output. Null when the marked lines were trimmed,
    /// so the caller does not copy a different command.</summary>
    public string? CopyText(ScreenBuffer buffer)
    {
        var start = buffer.ResolveAnchor(Start);
        if (start is null) return null;
        int endLine;
        int endColumn;
        if (End is null)
        {
            endLine = buffer.TotalLines - 1;
            endColumn = buffer.Columns;
        }
        else
        {
            var end = buffer.ResolveAnchor(End);
            if (end is null) return null;
            endLine = end.Value;
            endColumn = End.Column;
        }
        if (endLine < start.Value) return null;
        var outputEnd = endColumn <= 0 ? endLine - 1 : endLine;
        var outputCol = endColumn <= 0 ? int.MaxValue : endColumn - 1;
        if (!HasCommandText)
        {
            if (outputEnd < start.Value) return "";
            return buffer.ExtractText(start.Value, 0, outputEnd, outputCol);
        }
        // The command text is already known. Output starts at the C marker:
        // a marker at the end of the command line must not repeat that line,
        // and a marker at column 0 includes output that begins on the same line.
        // A column-0 marker on a blank line is the newline before that output.
        string output;
        if (Start.Column <= 0)
        {
            output = outputEnd < start.Value ? "" : buffer.ExtractText(start.Value, 0, outputEnd, outputCol);
            if (output.StartsWith('\n')) output = output[1..];
        }
        else if (outputEnd <= start.Value)
            output = outputEnd < start.Value || outputCol < Start.Column
                ? ""
                : buffer.ExtractText(start.Value, Start.Column, start.Value, outputCol);
        else
        {
            var tail = buffer.ExtractText(start.Value, Start.Column, start.Value, int.MaxValue).TrimEnd('\r', '\n');
            var rest = buffer.ExtractText(start.Value + 1, 0, outputEnd, outputCol);
            output = tail.Length == 0 ? rest : rest.Length == 0 ? tail : tail + "\n" + rest;
        }
        if (output.Length == 0) return Command;
        return Command.Length == 0 ? output : Command + "\n" + output;
    }
}

/// <summary>Applies Final Term / VS Code OSC 133 marks. Must run while the buffer lock is held.</summary>
public sealed class CommandJournal
{
    public const int Limit = 100;
    private readonly List<CommandRecord> _records = [];
    private int _nextId = 1;
    private long _started;
    private string? _pendingText;
    private (int Line, int Column, long Removed, bool Set) _input;
    public IReadOnlyList<CommandRecord> Records => _records;

    public void Apply(IReadOnlyList<(char Marker, int? ExitCode, string? Text, int Line, int Column, long Removed)> markers, ScreenBuffer buffer)
    {
        foreach (var (marker, code, text, markedLine, markedColumn, markedRemoved) in markers)
        {
            var line = markedLine - (int)(buffer.RemovedLineCount - markedRemoved);
            var column = markedColumn;
            if (marker == 'E')
            {
                _pendingText = text ?? "";
                continue;
            }
            if (marker == 'B')
            {
                _input = (line, column, buffer.RemovedLineCount, true);
                continue;
            }
            if (marker == 'A')
            {
                _input = default;
                continue;
            }
            if (marker == 'C')
            {
                _started = System.Diagnostics.Stopwatch.GetTimestamp();
                var hasText = false;
                var command = "";
                var startLine = line;
                if (_pendingText is not null)
                {
                    hasText = true;
                    command = _pendingText.TrimEnd('\r', '\n');
                    _pendingText = null;
                }
                else if (_input.Set)
                {
                    var adjusted = _input.Line - (int)(buffer.RemovedLineCount - _input.Removed);
                    if (adjusted >= 0 && adjusted <= line)
                    {
                        hasText = true;
                        startLine = adjusted;
                        var endLine = column == 0 && line > startLine ? line - 1 : line;
                        var endCol = column == 0 && line > startLine ? int.MaxValue : Math.Max(0, column - 1);
                        if (endLine >= startLine)
                            command = buffer.ExtractText(startLine, _input.Column, endLine, endCol).TrimEnd('\r', '\n');
                    }
                }
                _input = default;
                var anchor = buffer.CreateAnchor(line, column);
                _records.Add(new CommandRecord
                {
                    Id = _nextId++,
                    Command = command,
                    HasCommandText = hasText,
                    WorkingDirectory = buffer.Cwd ?? "",
                    Start = anchor,
                });
                while (_records.Count > Limit)
                {
                    var old = _records[0];
                    _records.RemoveAt(0);
                    buffer.Anchors.Remove(old.Start);
                    if (old.End is not null) buffer.Anchors.Remove(old.End);
                }
                continue;
            }
            if (marker != 'D') continue;
            var open = _records.LastOrDefault(record => record.Running);
            if (open is null) continue;
            open.Finish(code, System.Diagnostics.Stopwatch.GetElapsedTime(_started), buffer.CreateAnchor(line, column), column);
        }
    }
}
