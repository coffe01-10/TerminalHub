using TerminalHub.Core.Sessions;
using TerminalHub.Core.Settings;

namespace TerminalHub.App.ViewModels;

public sealed class PaneNode
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public TerminalSessionModel? Session { get; set; }
    public bool Vertical { get; set; }
    public double Ratio { get; set; } = .5;
    public PaneNode? First { get; set; }
    public PaneNode? Second { get; set; }
    public bool IsLeaf => First is null && Second is null;
    public IEnumerable<PaneNode> Leaves => IsLeaf ? [this] : (First?.Leaves ?? []).Concat(Second?.Leaves ?? []);
    public PaneNode Clone() => new() { Id = Id, Session = Session, Vertical = Vertical, Ratio = Ratio,
        First = First?.Clone(), Second = Second?.Clone() };
    public PaneLayout Save(Func<TerminalSessionModel?, int> index) => new() { Id = Id, SessionIndex = index(Session),
        Vertical = Vertical, Ratio = Ratio, First = First?.Save(index), Second = Second?.Save(index) };
    public static PaneNode? Load(PaneLayout? layout, Func<int, TerminalSessionModel?> session)
    {
        if (layout is null) return null;
        var node = new PaneNode { Id = layout.Id, Vertical = layout.Vertical, Ratio = Math.Clamp(layout.Ratio, .1, .9),
            Session = session(layout.SessionIndex), First = Load(layout.First, session), Second = Load(layout.Second, session) };
        return node.IsLeaf ? node.Session is null ? null : node : node.First is null ? node.Second : node.Second is null ? node.First : node;
    }
    public static PaneNode? Prune(PaneNode? node, ISet<TerminalSessionModel> available)
    {
        if (node is null) return null;
        if (node.IsLeaf) return node.Session is not null && available.Remove(node.Session) ? node : null;
        node.First = Prune(node.First, available); node.Second = Prune(node.Second, available);
        return node.First is null ? node.Second : node.Second is null ? node.First : node;
    }
}
