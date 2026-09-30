namespace TerminalHub.Core.Terminal;

/// <summary>Rewrap primary-screen logical lines without splitting wide glyphs.</summary>
internal static class ScreenReflow
{
    internal sealed record Result(List<TerminalCell[]> Lines, List<bool> Wrapped,
        Position Cursor, Position SavedCursor);

    internal sealed class Position(int line, int column, bool pendingWrap)
    {
        public int SourceLine { get; } = line;
        public int SourceColumn { get; } = column;
        public bool SourcePending { get; } = pendingWrap;
        public int Line { get; set; }
        public int Column { get; set; }
        public bool Pending { get; set; }
    }

    public static Result Rewrap(List<TerminalCell[]> source, List<bool> wrapped,
        int columns, Position cursor, Position savedCursor, IReadOnlyList<Position>? extra = null)
    {
        var lines = new List<TerminalCell[]>();
        var flags = new List<bool>();
        var row = Blank(columns);
        var col = 0;
        Position[] positions;
        if (extra is null || extra.Count == 0) positions = [cursor, savedCursor];
        else
        {
            positions = new Position[2 + extra.Count];
            positions[0] = cursor;
            positions[1] = savedCursor;
            for (var i = 0; i < extra.Count; i++) positions[i + 2] = extra[i];
        }

        void Map(Position position, int column, bool pending = false)
        {
            position.Line = lines.Count;
            position.Column = Math.Min(column, columns - 1);
            position.Pending = pending;
        }

        void Next(bool soft)
        {
            lines.Add(row);
            flags.Add(soft);
            row = Blank(columns);
            col = 0;
        }

        for (var r = 0; r < source.Count; r++)
        {
            var cells = source[r];
            var length = cells.Length;
            // Hard-line padding is not text. Preserve cursor-positioning spaces.
            if (!wrapped[r])
                while (length > 0 && IsPadding(cells[length - 1])) length--;
            foreach (var position in positions)
                if (r == position.SourceLine)
                    length = Math.Max(length, Math.Min(cells.Length, position.SourceColumn + (position.SourcePending ? 1 : 0)));
            for (var c = 0; c < length; c++)
            {
                var cell = cells[c];
                if (cell.IsWideContinuation) continue;
                // Autowrap leaves a blank at the right edge when the next glyph
                // is wide. Do not turn that gap into a literal space on reflow.
                if (wrapped[r] && c == cells.Length - 1 && IsPadding(cell)
                    && r + 1 < source.Count && source[r + 1][0].IsWide) continue;
                var width = cell.IsWide ? 2 : 1;
                if (col + width > columns) Next(true);
                foreach (var position in positions)
                    if (r == position.SourceLine && position.SourceColumn >= c && position.SourceColumn < c + width)
                        Map(position, col + position.SourceColumn - c);
                row[col] = cell;
                if (width == 2 && col + 1 < columns)
                {
                    row[col + 1] = c + 1 < cells.Length ? cells[c + 1] : cell;
                    row[col + 1].Char = '\0';
                    row[col + 1].Tail = null;
                    row[col + 1].IsWide = false;
                    row[col + 1].IsWideContinuation = true;
                }
                col += width;
                foreach (var position in positions)
                    if (r == position.SourceLine && position.SourcePending && c + width >= cells.Length)
                        Map(position, col, col >= columns);
            }
            foreach (var position in positions)
                if (r == position.SourceLine && !position.SourcePending && position.SourceColumn >= length)
                    Map(position, col, col >= columns);
            if (!wrapped[r] || r == source.Count - 1) Next(false);
        }
        return new(lines, flags, cursor, savedCursor);
    }

    internal static bool IsPadding(TerminalCell cell) => cell.Char is ' ' or '\0'
        && !cell.IsWideContinuation && cell.Tail is null && cell.Attrs == CellAttrs.None
        && cell.Fg.IsDefault && cell.Bg.IsDefault;

    internal static TerminalCell[] Blank(int columns)
    {
        var cells = new TerminalCell[columns];
        Array.Fill(cells, TerminalCell.Blank(TerminalColor.Default));
        return cells;
    }
}
