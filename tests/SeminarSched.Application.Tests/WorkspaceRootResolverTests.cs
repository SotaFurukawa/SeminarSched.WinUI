using SeminarSched.Application.Settings;

namespace SeminarSched.Application.Tests;

public sealed class WorkspaceRootResolverTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "SeminarSched.Tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void Move_CopiesNestedFilesAndFoldersToDestination_AndRemovesSource()
    {
        var source = Path.Combine(_directory, "source");
        var destination = Path.Combine(_directory, "destination");
        Directory.CreateDirectory(Path.Combine(source, "Workspace", "Projects"));
        File.WriteAllText(Path.Combine(source, "settings.json"), "{}");
        File.WriteAllText(Path.Combine(source, "Workspace", "Projects", "sample.jukuschedule"), "data");
        Directory.CreateDirectory(destination);

        WorkspaceRootResolver.Move(source, destination, []);

        Assert.Equal("{}", File.ReadAllText(Path.Combine(destination, "settings.json")));
        Assert.Equal("data", File.ReadAllText(Path.Combine(destination, "Workspace", "Projects", "sample.jukuschedule")));
        Assert.Empty(Directory.EnumerateFileSystemEntries(source)); // 内容はすべて移動先へコピーされ、元の場所からは削除される。
    }

    [Fact]
    public void Move_LeavesExcludedFileInSource_AndDoesNotCopyIt()
    {
        var source = Path.Combine(_directory, "source2");
        var destination = Path.Combine(_directory, "destination2");
        Directory.CreateDirectory(source);
        Directory.CreateDirectory(destination);
        var breadcrumb = Path.Combine(source, "workspace-root.txt");
        File.WriteAllText(breadcrumb, "elsewhere");
        File.WriteAllText(Path.Combine(source, "settings.json"), "{}");

        WorkspaceRootResolver.Move(source, destination, [breadcrumb]);

        Assert.True(File.Exists(breadcrumb)); // 目印ファイルは移動先へコピーされず、元の場所に残る。
        Assert.False(File.Exists(Path.Combine(destination, "workspace-root.txt")));
        Assert.True(File.Exists(Path.Combine(destination, "settings.json")));
        Assert.False(File.Exists(Path.Combine(source, "settings.json"))); // 除外対象以外は元の場所から削除される。
    }

    [Fact]
    public void Move_WhenSourceDoesNotExist_DoesNothingAndDoesNotThrow()
    {
        var source = Path.Combine(_directory, "missing-source");
        var destination = Path.Combine(_directory, "destination3");
        Directory.CreateDirectory(destination);

        WorkspaceRootResolver.Move(source, destination, []);

        Assert.Empty(Directory.EnumerateFileSystemEntries(destination));
    }

    public void Dispose() { if (Directory.Exists(_directory)) Directory.Delete(_directory, true); }
}
