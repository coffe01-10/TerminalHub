using CommunityToolkit.Mvvm.ComponentModel;
using TerminalHub.Core.Settings;

namespace TerminalHub.App.ViewModels;

public sealed partial class WorkspaceSessionEditor : ObservableObject
{
    private readonly WorkspaceSession _original;
    public bool CanRunStartupCommand => _original.Tag != "SSH" && !Path.GetFileNameWithoutExtension(Shell).Equals("ssh", StringComparison.OrdinalIgnoreCase);
    partial void OnShellChanged(string value) => OnPropertyChanged(nameof(CanRunStartupCommand));
    [ObservableProperty] private string _name;
    [ObservableProperty] private string _workingDirectory;
    [ObservableProperty] private string _shell;
    [ObservableProperty] private string _arguments;
    [ObservableProperty] private string _startupCommand;
    [ObservableProperty] private bool _runStartupCommand;
    public WorkspaceSessionEditor(WorkspaceSession session)
    {
        _original = session;
        _name = session.Name; _workingDirectory = session.WorkingDirectory;
        _shell = session.Shell; _arguments = session.Arguments;
        _startupCommand = session.StartupCommand; _runStartupCommand = session.RunStartupCommand;
    }
    public WorkspaceSession Snapshot() => _original with
    {
        Name = Name, WorkingDirectory = WorkingDirectory, Shell = Shell, Arguments = Arguments,
        StartupCommand = StartupCommand, RunStartupCommand = RunStartupCommand
    };
}
