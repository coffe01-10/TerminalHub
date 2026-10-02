using System.Runtime.CompilerServices;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Metadata;

namespace TerminalHub.App.Controls;

/// <summary>Keep a session's thumbnail when switching workspaces; closed sessions can be collected.</summary>
public sealed class CachedShelfTemplate : IDataTemplate
{
    private readonly ConditionalWeakTable<object, Control> _views = new();
    [Content] public IDataTemplate Template { get; set; } = null!;

    public bool Match(object? data) => Template.Match(data);
    public Control? Build(object? data)
    {
        if (data is null) return null;
        if (_views.TryGetValue(data, out var cached) && cached.Parent is null) return cached;
        // Pin/group reordering replaces slots before removing the old slot.
        // A visual still owned by that presenter cannot have a second parent.
        var view = Template.Build(data)!;
        _views.Remove(data);
        _views.Add(data, view);
        return view;
    }
}
