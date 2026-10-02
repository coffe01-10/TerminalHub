using Avalonia.Headless.XUnit;
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
        Assert.True(vm.StatusIsError);
        Assert.Contains("无法读取", vm.StatusText);
    }

    [Fact]
    public void MissingDir_MarksStatusAsError()
    {
        using var vm = new FilesViewModel();
        vm.NavigateTo(Path.Combine(_root, "nope"));

        Assert.True(vm.StatusIsError);
        Assert.Contains("无法打开目录", vm.StatusText);
    }

    [Fact]
    public void ShowHidden_Toggle_RefiltersListing()
    {
        File.WriteAllText(Path.Combine(_root, ".hideme"), "x");
        using var vm = new FilesViewModel(showHidden: false);
        vm.NavigateTo(_root);
        Assert.DoesNotContain(vm.Entries, e => e.Name == ".hideme");

        vm.ShowHidden = true;
        Assert.Contains(vm.Entries, e => e.Name == ".hideme");

        vm.ShowHidden = false;
        Assert.DoesNotContain(vm.Entries, e => e.Name == ".hideme");
    }

    [AvaloniaFact]
    public void SelectingPng_ShowsImage_NotBinaryNotice()
    {
        // Smallest valid 1×1 PNG — Bitmap must actually decode it.
        File.WriteAllBytes(Path.Combine(_root, "px.png"),
            Convert.FromBase64String(
                "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg=="));
        using var vm = new FilesViewModel();
        vm.NavigateTo(_root);
        vm.SelectedEntry = vm.Entries.First(e => e.Name == "px.png");

        Assert.True(vm.HasPreview);
        Assert.True(vm.PreviewIsImage);
        Assert.NotNull(vm.PreviewImage);
        Assert.Contains("图片", vm.PreviewMeta);
    }

    [AvaloniaFact]
    public void CorruptPng_FallsBackToBinaryNotice()
    {
        File.WriteAllBytes(Path.Combine(_root, "bad.png"), [0x89, 0x50, 0x00, 0x01]);
        using var vm = new FilesViewModel();
        vm.NavigateTo(_root);
        vm.SelectedEntry = vm.Entries.First(e => e.Name == "bad.png");

        Assert.True(vm.HasPreview);
        Assert.False(vm.PreviewIsImage);
        Assert.Contains("二进制", vm.PreviewText);
    }

    [Fact]
    public void NavigateTo_ShowsItemCount_AndPreviewPath()
    {
        using var vm = new FilesViewModel();
        vm.NavigateTo(_root); // 3 entries: sub/, a.txt, blob.bin
        Assert.Equal("3 项", vm.StatusText);
        Assert.False(vm.StatusIsError);

        vm.SelectedEntry = vm.Entries.First(e => e.Name == "a.txt");
        Assert.Equal(Path.Combine(_root, "a.txt"), vm.PreviewPath);
    }

    [AvaloniaFact]
    public void NewFolder_AutoName_AndSelects()
    {
        using var vm = new FilesViewModel();
        vm.NavigateTo(_root);
        vm.NewFolder(null);
        vm.NewFolder(null);

        Assert.True(Directory.Exists(Path.Combine(_root, "新建文件夹")));
        Assert.True(Directory.Exists(Path.Combine(_root, "新建文件夹 2")));
        Assert.Equal("新建文件夹 2", vm.SelectedEntry?.Name);
        Assert.True(vm.SelectedEntry?.IsDirectory);
        Assert.False(vm.StatusIsError);
    }

    [AvaloniaFact]
    public void NewTextFile_Named_AutoExt_AndSelects()
    {
        using var vm = new FilesViewModel();
        vm.NavigateTo(_root);
        vm.NewTextFile("notes");           // no ext → .txt
        vm.NewTextFile("script.sh");       // explicit ext kept

        Assert.True(File.Exists(Path.Combine(_root, "notes.txt")));
        Assert.True(File.Exists(Path.Combine(_root, "script.sh")));
        Assert.Equal("script.sh", vm.SelectedEntry?.Name);
        Assert.False(vm.SelectedEntry?.IsDirectory);
        Assert.False(vm.StatusIsError);
        Assert.Contains("已创建", vm.StatusText);
    }

    [AvaloniaFact]
    public void NewFolder_BadName_ReportsError()
    {
        using var vm = new FilesViewModel();
        vm.NavigateTo(_root);
        vm.NewFolder("a/b\0c"); // separators/NUL rejected before touching the fs

        Assert.True(vm.StatusIsError);
        Assert.Contains("名称无效", vm.StatusText);
    }

    [AvaloniaFact]
    public void Rename_File_AndDir_MoveAndReselect()
    {
        using var vm = new FilesViewModel();
        vm.NavigateTo(_root);
        var file = vm.Entries.First(e => e.Name == "a.txt");
        vm.Rename(file, "renamed.txt");
        Assert.True(File.Exists(Path.Combine(_root, "renamed.txt")));
        Assert.Equal("renamed.txt", vm.SelectedEntry?.Name);

        var dir = vm.Entries.First(e => e.Name == "sub");
        vm.Rename(dir, "sub-renamed");
        Assert.True(Directory.Exists(Path.Combine(_root, "sub-renamed")));
        Assert.Equal("sub-renamed", vm.SelectedEntry?.Name);
        Assert.False(vm.StatusIsError);
    }

    [AvaloniaFact]
    public void Rename_ToExisting_ReportsError()
    {
        File.WriteAllText(Path.Combine(_root, "taken.txt"), "x");
        using var vm = new FilesViewModel();
        vm.NavigateTo(_root);
        vm.Rename(vm.Entries.First(e => e.Name == "a.txt"), "taken.txt");

        Assert.True(vm.StatusIsError);
        Assert.Contains("无法重命名", vm.StatusText);
        Assert.True(File.Exists(Path.Combine(_root, "a.txt")));
    }

    [AvaloniaFact]
    public async Task ImportPaths_CopiesFileAndDir_UniqueNames_SkipsInPlace()
    {
        var outside = Path.Combine(_root, "..", $"ext-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(outside, "pkg", "nested"));
        File.WriteAllText(Path.Combine(outside, "pkg", "nested", "x.txt"), "deep");
        File.WriteAllText(Path.Combine(outside, "incoming.txt"), "hi");
        try
        {
            using var vm = new FilesViewModel();
            vm.NavigateTo(_root);
            var imported = await vm.ImportPathsAsync(new[]
            {
                Path.Combine(outside, "incoming.txt"),
                Path.Combine(outside, "pkg"),
                Path.Combine(_root, "a.txt"),     // already inside → skipped
            });
            Assert.Equal(2, imported);
            Assert.True(File.Exists(Path.Combine(_root, "incoming.txt")));
            Assert.True(File.Exists(Path.Combine(_root, "pkg", "nested", "x.txt")));
            Assert.Equal("已导入 2 项，跳过 1 项", vm.StatusText);
            Assert.False(vm.StatusIsError);

            // Second import of the same name → unique suffix, not overwrite.
            await vm.ImportPathsAsync(new[] { Path.Combine(outside, "incoming.txt") });
            Assert.True(File.Exists(Path.Combine(_root, "incoming 2.txt")));
        }
        finally { Directory.Delete(outside, true); }
    }

    [AvaloniaFact]
    public async Task ImportPaths_SelfAncestor_AndSymlink_AreSkipped_NotRecursive()
    {
        var outside = Path.Combine(_root, "..", $"ext-{Guid.NewGuid():N}");
        Directory.CreateDirectory(outside);
        try
        {
            // A symlink back to its own parent — copying it would loop forever.
            var loop = Path.Combine(outside, "loop");
            try { Directory.CreateSymbolicLink(loop, outside); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* FS without symlink support → covered below anyway */ }
            File.WriteAllText(Path.Combine(outside, "ok.txt"), "x");

            using var vm = new FilesViewModel();
            vm.NavigateTo(_root);
            var before = Directory.GetFileSystemEntries(_root).Length;

            var imported = await vm.ImportPathsAsync(new[]
            {
                _root,                                   // the directory itself
                Directory.GetParent(_root)!.FullName,    // an ancestor
                Path.Combine(_root, "a.txt"),            // already inside
            });
            Assert.Equal(0, imported);
            Assert.Equal(before, Directory.GetFileSystemEntries(_root).Length);
            Assert.False(vm.StatusIsError);
            Assert.Contains("跳过", vm.StatusText);

            // Source tree containing a self-link still completes: the link dir
            // is copied as nothing (skipped), not recursed into.
            var copied = await vm.ImportPathsAsync(new[] { outside });
            Assert.Equal(1, copied);
            Assert.True(File.Exists(Path.Combine(_root, Path.GetFileName(outside), "ok.txt")));
            Assert.False(Directory.Exists(Path.Combine(_root, Path.GetFileName(outside), "loop")));
        }
        finally { Directory.Delete(outside, true); }
    }

    [AvaloniaFact]   // writes into the watched dir — watcher posts need a session
    public void NewFolder_ExplicitExistingName_Dedupes()
    {
        using var vm = new FilesViewModel();
        vm.NavigateTo(_root);
        vm.NewFolder("sub");                    // "sub" exists in the fixture
        Assert.True(Directory.Exists(Path.Combine(_root, "sub 2")));
        Assert.Equal("sub 2", vm.SelectedEntry?.Name);
    }

    [Fact]
    public void Names_WithSeparators_OrTraversal_AreRejected()
    {
        using var vm = new FilesViewModel();
        vm.NavigateTo(_root);

        vm.NewFolder("../escape");
        Assert.True(vm.StatusIsError);
        Assert.False(Directory.Exists(Path.Combine(_root, "..", "escape")));

        vm.NewFolder("a/b");
        Assert.True(vm.StatusIsError);
        Assert.False(Directory.Exists(Path.Combine(_root, "a")));

        vm.NewTextFile("../escape.txt");
        Assert.True(vm.StatusIsError);

        vm.Rename(vm.Entries.First(e => e.Name == "a.txt"), "../moved.txt");
        Assert.True(vm.StatusIsError);
        Assert.True(File.Exists(Path.Combine(_root, "a.txt")));
        Assert.False(File.Exists(Path.Combine(_root, "..", "moved.txt")));
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
