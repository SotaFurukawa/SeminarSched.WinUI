namespace SeminarSched_WinUI;

// Fixed, centralized folders for output/forms generation, mirroring the Python reference app's
// AppData-based workspace instead of always starting a folder picker at a generic folder. Project
// files and backups have their own equivalents on ProjectService (DefaultProjectsDirectory /
// DefaultBackupDirectory) since those are Application-layer concerns shared with non-UI callers.
internal static class WorkspacePaths
{
    public static string Output { get; } = EnsureDirectory("Output");
    public static string Forms { get; } = EnsureDirectory("Forms");

    private static string EnsureDirectory(string name)
    {
        var path = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SeminarSched.WinUI", "Workspace", name);
        Directory.CreateDirectory(path);
        return path;
    }
}
