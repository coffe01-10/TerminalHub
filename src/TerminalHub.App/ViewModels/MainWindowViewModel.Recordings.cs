using CommunityToolkit.Mvvm.ComponentModel;
using TerminalHub.Core.Sessions;
using TerminalHub.Core.Ssh;
using TerminalHub.Core.Terminal;

namespace TerminalHub.App.ViewModels;

public partial class MainWindowViewModel
{
    private readonly Dictionary<Guid, TerminalRecorder> _recorders = [];
    [ObservableProperty] private string _recordingStatus = "录制当前终端的屏幕、输出和尺寸变化。录制文件可能包含终端中显示的敏感内容。";
    public bool IsRecordingCurrent => ActiveSession is { } session && _recorders.ContainsKey(session.Id);
    public void StartRecording(string path)
    {
        if (ActiveSession is not { } session || IsRecordingCurrent) return;
        try { _recorders.Add(session.Id, new TerminalRecorder(session.Emulator, path, session.Name)); RecordingStatus = "正在录制 · " + path; }
        catch (Exception ex) { RecordingStatus = "无法开始录制：" + ex.Message; }
        OnPropertyChanged(nameof(IsRecordingCurrent));
    }
    public async Task StopRecordingAsync()
    {
        if (ActiveSession is not { } session) return;
        await StopSessionRecordingAsync(session.Id);
    }
    private async Task StopSessionRecordingAsync(Guid id)
    {
        if (!_recorders.Remove(id, out var recorder)) return;
        try { await recorder.DisposeAsync(); RecordingStatus = "录制已保存 · " + recorder.Path; }
        catch (Exception ex) { RecordingStatus = "保存录制失败：" + ex.Message; }
        OnPropertyChanged(nameof(IsRecordingCurrent));
    }
    private void StopAllRecordings()
    {
        foreach (var recorder in _recorders.Values)
            try { recorder.DisposeAsync().AsTask().GetAwaiter().GetResult(); } catch (IOException ex) { RecordingStatus = ex.Message; }
        _recorders.Clear();
    }
    private Task<TerminalSessionModel?> OpenRemoteTerminalCoreAsync(SshHost host, string directory)
    {
        var quotedDirectory = "'" + directory.Replace("'", "'\\''") + "'";
        var command = $"cd -- {quotedDirectory} && exec ${{SHELL:-sh}} -l";
        return CreateSessionAsync(host.DisplayName, SessionTag.Ssh, "", shellCommand: "ssh", arguments: $"-t {host.SshArguments} {SshHost.QuoteArgument(command)}");
    }
    private async Task OpenRemoteTerminalAsync(SshHost host, string directory) => await OpenRemoteTerminalCoreAsync(host, directory);
}
