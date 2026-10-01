using TerminalHub.App.ViewModels;
using TerminalHub.Core.Files;
using Xunit;

namespace TerminalHub.Tests;

/// <summary>Files 面板：单击选中即预览，目录/无选中清空预览。</summary>
public class FilesPreviewTests : IDisposable
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "th-preview-" + Guid.NewGuid().ToString("N")[..8]);

    public FilesPreviewTests()
    {
        Directory.CreateDirectory(Path.Combine(_root, "sub"));
        File.WriteAllText(Path.Combine(_root, "a.txt"), "hello preview");
        File.WriteAllBytes(Path.Combine(_root, "blob.bin"), [0x89, 0x50, 0x00, 0x01]);
    }

    public void Dispose() => Directory.Delete(_root, true);

    [Fact]
    public void SelectingFile_AutoPreviews()
    {
        using var vm = new FilesViewModel();
        vm.NavigateTo(_root);
        vm.SelectedEntry = vm.Entries.First(e => !e.IsDirectory);

        Assert.True(vm.HasPreview);
        Assert.Equal("a.txt", vm.PreviewTitle);
        Assert.Contains("hello preview", vm.PreviewText);
    }

    [Fact]
    public void SelectingDirectory_ClearsPreview()
    {
        using var vm = new FilesViewModel();
        vm.NavigateTo(_root);
        vm.SelectedEntry = vm.Entries.First(e => !e.IsDirectory);
        Assert.True(vm.HasPreview);

        vm.SelectedEntry = vm.Entries.First(e => e.IsDirectory);
        Assert.False(vm.HasPreview);
    }

    [Fact]
    public void NavigatingAway_WithoutFileSelection_ClearsPreview()
    {
        using var vm = new FilesViewModel();
        vm.NavigateTo(_root);
        vm.SelectedEntry = vm.Entries.First(e => !e.IsDirectory);
        Assert.True(vm.HasPreview);

        var other = Path.Combine(_root, "sub");
        vm.NavigateTo(other); // empty dir → no selection restores → preview drops
        Assert.False(vm.HasPreview);
    }

    [Fact]
    public void SelectingBinary_ShowsNotice_InsteadOfText()
    {
        using var vm = new FilesViewModel();
        vm.NavigateTo(_root);
        vm.SelectedEntry = vm.Entries.First(e => e.Name == "blob.bin");

        Assert.True(vm.HasPreview);
        Assert.Contains("二进制", vm.PreviewText);
        Assert.Contains("二进制", vm.PreviewMeta);
    }

    [Fact]
    public void UnreadableFile_ClearsStalePreview()
    {
        using var vm = new FilesViewModel();
        vm.NavigateTo(_root);
        vm.SelectedEntry = vm.Entries.First(e => !e.IsDirectory);
        Assert.True(vm.HasPreview);

        // A file that disappears/cannot be read must not keep showing the
        // previous file's contents under the new selection.
        vm.SelectedEntry = new FileEntry
        {
            Name = "ghost.txt", FullPath = Path.Combine(_root, "ghost.txt"),
        };

        Assert.False(vm.HasPreview);
        Assert.Contains("无法读取", vm.StatusText);
    }

    [Fact]
    public void Refresh_RestoredSameFile_KeepsPreview()
    {
        using var vm = new FilesViewModel();
        vm.NavigateTo(_root);
        vm.SelectedEntry = vm.Entries.First(e => !e.IsDirectory);
        Assert.True(vm.HasPreview);

        vm.NavigateTo(_root); // refresh: same-path selection is restored
        Assert.True(vm.HasPreview);
        Assert.Equal("a.txt", vm.PreviewTitle);
        Assert.Equal("a.txt", vm.SelectedEntry?.Name);
    }
}
