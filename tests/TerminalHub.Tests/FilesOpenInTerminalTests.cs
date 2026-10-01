using System.Reflection;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using TerminalHub.App;
using TerminalHub.App.Views;
using TerminalHub.Core.Files;
using TerminalHub.Pty;
using Xunit;

namespace TerminalHub.Tests;

/// <summary>Files 面板「在此打开终端 / 复制路径 / 在文件管理器中显示 / 拖出到终端」。</summary>
public class FilesOpenInTerminalTests
{
    private static readonly string DirA =
        Path.Combine(Path.GetTempPath(), "th-open", "sub dir");
    private static readonly string FileInDir = Path.Combine(DirA, "note.txt");

    /// <summary>StorageProvider stub: resolves every path to the BCL-backed item,
    /// same as a real desktop provider (and the picker proxies in OutputToolsTests).</summary>
    private class FileResolveProxy : DispatchProxy
    {
        private static readonly Type BclFile =
            typeof(IStorageFile).Assembly.GetType("Avalonia.Platform.Storage.FileIO.BclStorageFile")!;
        private static readonly Type BclFolder =
            typeof(IStorageFile).Assembly.GetType("Avalonia.Platform.Storage.FileIO.BclStorageFolder")!;

        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            var uri = (Uri)args![0]!;
            return method?.Name switch
            {
                nameof(IStorageProvider.TryGetFileFromPathAsync) => Task.FromResult(
                    (IStorageFile?)Activator.CreateInstance(
                        BclFile, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                        null, [new FileInfo(uri.LocalPath)], null)),
                nameof(IStorageProvider.TryGetFolderFromPathAsync) => Task.FromResult(
                    (IStorageFolder?)Activator.CreateInstance(
                        BclFolder, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                        null, [new DirectoryInfo(uri.LocalPath)], null)),
                _ => throw new NotSupportedException(method?.Name),
            };
        }
    }

    // ---------- VM level (callbacks injected, no window) ----------

    [Fact]
    public void OpenInTerminal_Directory_CdsIntoIt()
    {
        var cds = new List<string>();
        var vm = new TerminalHub.App.ViewModels.FilesViewModel(
            openTerminalAt: cds.Add, hasActiveSession: () => true);
        var dir = new FileEntry { Name = "sub dir", FullPath = DirA, IsDirectory = true };

        Assert.True(vm.OpenInTerminalCommand.CanExecute(dir));
        vm.OpenInTerminalCommand.Execute(dir);

        Assert.Equal(new[] { DirA }, cds);
        Assert.Contains("cd", vm.StatusText);
        Assert.Contains(DirA, vm.StatusText);
    }

    [Fact]
    public void OpenInTerminal_File_CdsToParent()
    {
        var cds = new List<string>();
        var vm = new TerminalHub.App.ViewModels.FilesViewModel(
            openTerminalAt: cds.Add, hasActiveSession: () => true);
        var file = new FileEntry { Name = "note.txt", FullPath = FileInDir };

        vm.OpenInTerminalCommand.Execute(file);

        Assert.Equal(new[] { DirA }, cds); // file → parent dir, never the file itself
    }

    [Fact]
    public void OpenInTerminal_Disabled_WithoutSelection_OrSession()
    {
        var vm = new TerminalHub.App.ViewModels.FilesViewModel(
            openTerminalAt: _ => { }, hasActiveSession: () => false);
        var dir = new FileEntry { Name = "d", FullPath = DirA, IsDirectory = true };

        // No selection at all → disabled.
        Assert.False(vm.OpenInTerminalCommand.CanExecute(null));
        Assert.False(vm.CopyPathCommand.CanExecute(null));

        // Selection exists but no live session → terminal button off, copy still on.
        vm.SelectedEntry = dir;
        Assert.False(vm.OpenInTerminalCommand.CanExecute(null));
        Assert.True(vm.CopyPathCommand.CanExecute(null));
    }

    [Fact]
    public async Task CopyPath_PassesAbsolutePath()
    {
        var copied = new List<string>();
        var vm = new TerminalHub.App.ViewModels.FilesViewModel(
            copyTextAsync: p => { copied.Add(p); return Task.CompletedTask; });
        var file = new FileEntry { Name = "note.txt", FullPath = FileInDir };

        await vm.CopyPathCommand.ExecuteAsync(file);

        Assert.Equal(new[] { FileInDir }, copied);
        Assert.Contains("已复制", vm.StatusText);
        Assert.False(vm.StatusIsError); // info copy, not a failure
    }

    [Fact]
    public void RevealInFileManager_PassesSelectedPath()
    {
        var shown = new List<string>();
        var vm = new TerminalHub.App.ViewModels.FilesViewModel(revealInFileManager: shown.Add);
        var file = new FileEntry { Name = "note.txt", FullPath = FileInDir };

        Assert.False(vm.RevealInFileManagerCommand.CanExecute(null));
        vm.RevealInFileManagerCommand.Execute(file);

        Assert.Equal(new[] { FileInDir }, shown);
        Assert.False(vm.StatusIsError);
        Assert.Contains("文件管理器", vm.StatusText);
    }

    [Fact]
    public void RevealInFileManager_Failure_MarksError()
    {
        var vm = new TerminalHub.App.ViewModels.FilesViewModel(
            revealInFileManager: _ => throw new InvalidOperationException("no fm"));
        var file = new FileEntry { Name = "note.txt", FullPath = FileInDir };

        vm.RevealInFileManagerCommand.Execute(file);

        Assert.True(vm.StatusIsError);
        Assert.Contains("无法打开文件管理器", vm.StatusText);
    }

    [Fact]
    public void OpenExternally_FileOnly_PassesPath()
    {
        var opened = new List<string>();
        var vm = new TerminalHub.App.ViewModels.FilesViewModel(openExternal: opened.Add);
        var file = new FileEntry { Name = "note.txt", FullPath = FileInDir };
        var dir = new FileEntry { Name = "sub dir", FullPath = DirA, IsDirectory = true };

        Assert.False(vm.OpenExternallyCommand.CanExecute(dir));
        vm.OpenExternallyCommand.Execute(file);

        Assert.Equal(new[] { FileInDir }, opened);
        Assert.False(vm.StatusIsError);
    }

    [Fact]
    public void Open_File_OpensExternally_Dir_Navigates()
    {
        var opened = new List<string>();
        var vm = new TerminalHub.App.ViewModels.FilesViewModel(openExternal: opened.Add);
        // Double-click/Enter on a file escalates to the default app; single
        // click already previews, so re-previewing would be a no-op.
        vm.Open(new FileEntry { Name = "note.txt", FullPath = FileInDir });
        Assert.Equal(new[] { FileInDir }, opened);
    }

    [Fact]
    public async Task DragData_File_PackagesLocalPath()
    {
        var provider = DispatchProxy.Create<IStorageProvider, FileResolveProxy>();
        var data = await FilesDragData.CreateAsync(provider,
            new FileEntry { Name = "note.txt", FullPath = FileInDir });

        var files = data!.GetFiles()!.ToList();
        Assert.Single(files);
        Assert.Equal(FileInDir, files[0].TryGetLocalPath());
    }

    [Fact]
    public async Task DragData_Directory_UsesFolderHandle()
    {
        Directory.CreateDirectory(DirA);
        try
        {
            var provider = DispatchProxy.Create<IStorageProvider, FileResolveProxy>();
            var data = await FilesDragData.CreateAsync(provider,
                new FileEntry { Name = "sub dir", FullPath = DirA, IsDirectory = true });

            var files = data!.GetFiles()!.ToList();
            Assert.Single(files);
            Assert.Equal(DirA, Path.TrimEndingDirectorySeparator(files[0].TryGetLocalPath()!));
        }
        finally { Directory.Delete(Path.Combine(Path.GetTempPath(), "th-open"), true); }
    }

    // ---------- end-to-end through the real window ----------

    [AvaloniaFact]
    public async Task Files_OpenInTerminal_SendsRealCd_AndSyncsChrome()
    {
        PtySessionFactory.UseMock = true;
        Directory.CreateDirectory(DirA);
        try
        {
            var window = new MainWindow { Width = 1200, Height = 800 };
            window.Show();
            await Task.Delay(400);
            var vm = (TerminalHub.App.ViewModels.MainWindowViewModel)window.DataContext!;

            vm.SelectedRightTab = 1; // Files tab → lands on session cwd
            await Task.Delay(200);
            var parent = Path.GetDirectoryName(DirA)!;
            vm.Files.NavigateTo(parent);
            var dir = vm.Files.Entries.First(e => e.IsDirectory && e.FullPath == DirA);
            vm.Files.SelectedEntry = dir;
            Assert.True(vm.Files.OpenInTerminalCommand.CanExecute(null));

            vm.Files.OpenInTerminalCommand.Execute(null);
            await Task.Delay(200);

            // The mock PTY echoes the typed command straight back into the buffer.
            // Quoting follows the session shell — same helper the VM itself uses.
            var emu = vm.ActiveSession!.Emulator;
            var expectedCd = "cd " + TerminalHub.Core.Pty.ShellPathInput.Format(new[] { DirA }, vm.ActiveSession.Shell);
            Assert.Contains(expectedCd, emu.Buffer.TailText(30));
            // CWD chrome sync: path bar + Files both followed, history pushed (Back armed).
            Assert.Equal(DirA, vm.ActiveSession.WorkingDirectory);
            Assert.Equal(DirA, vm.Files.CurrentPath);
            Assert.True(vm.CanCwdBack);
            window.Close();
        }
        finally { Directory.Delete(Path.Combine(Path.GetTempPath(), "th-open"), true); }
    }
}
