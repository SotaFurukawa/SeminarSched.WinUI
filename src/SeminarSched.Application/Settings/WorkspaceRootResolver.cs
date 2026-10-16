namespace SeminarSched.Application.Settings;

/// <summary>
/// ユーザー要望「インストール時にデフォルトの保存先を指定しておき、workspaceを置く場所を変更
/// できるようにする」への対応。既定の保存先（%LOCALAPPDATA%\SeminarSched.WinUI）はそのままに、
/// その中の固定パスにある小さな「目印ファイル」（<see cref="BreadcrumbPath"/>）の中身で、実際の
/// データ保存先（<see cref="Current"/>）を決める。目印ファイル自体の場所は、保存先をどこへ変更
/// しても絶対に動かさない（最初に探す場所が動いてしまうと、次回起動時に見つけられなくなるため）。
///
/// 移動は「要求して次回起動時に反映する」方式にした。実行中のアプリ自身がログファイルや
/// データベースを開いたままの状態でコピーすると、ファイルが使用中で失敗したり内容が半端に
/// なったりする恐れがあるため、移動の要求（<see cref="RequestMove"/>）は目印とは別の「保留中」
/// マーカーを書くだけにし、実際のコピーはアプリの次回起動の最初（他の何よりも前に
/// <see cref="Current"/>が最初に参照されたとき）に、まだ何も開いていない状態で行う
/// （<see cref="ApplyPendingMoveIfAny"/>）。
/// </summary>
public static class WorkspaceRootResolver
{
    public static string DefaultRoot { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "SeminarSched.WinUI");

    public static string BreadcrumbPath { get; } = Path.Combine(DefaultRoot, "workspace-root.txt");

    private static string PendingMovePath { get; } = Path.Combine(DefaultRoot, "pending-workspace-move.txt");

    /// <summary>
    /// 前回起動時に要求された移動が失敗した場合のエラーメッセージ。設定ページが起動後に一度だけ
    /// 確認し、ユーザーへ表示する。成功時・移動の要求が無かった場合はnull。
    /// </summary>
    public static string? LastMoveError { get; private set; }

    public static string Current { get; } = Resolve();

    private static string Resolve()
    {
        ApplyPendingMoveIfAny();

        var root = DefaultRoot;
        if (File.Exists(BreadcrumbPath))
        {
            var overridden = File.ReadAllText(BreadcrumbPath).Trim();
            if (overridden.Length > 0) root = overridden;
        }
        Directory.CreateDirectory(root);
        return root;
    }

    private static void ApplyPendingMoveIfAny()
    {
        if (!File.Exists(PendingMovePath)) return;
        var newRoot = File.ReadAllText(PendingMovePath).Trim();
        try
        {
            if (newRoot.Length == 0) return;
            var currentRoot = DefaultRoot;
            if (File.Exists(BreadcrumbPath))
            {
                var overridden = File.ReadAllText(BreadcrumbPath).Trim();
                if (overridden.Length > 0) currentRoot = overridden;
            }
            if (string.Equals(Path.GetFullPath(currentRoot), Path.GetFullPath(newRoot), StringComparison.OrdinalIgnoreCase)) return;

            Move(currentRoot, newRoot, [BreadcrumbPath, PendingMovePath]);
            Directory.CreateDirectory(DefaultRoot);
            File.WriteAllText(BreadcrumbPath, newRoot);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // コピーの途中で失敗しても、目印ファイルはまだ書き換えていないため、今までの場所が
            // そのまま使われ続けるだけで、データは壊れたり失われたりしない。
            LastMoveError = $"データの移動先フォルダー（{newRoot}）への移動に失敗したため、元の場所を使い続けます: {exception.Message}";
        }
        finally
        {
            File.Delete(PendingMovePath);
        }
    }

    /// <summary>
    /// 保存先フォルダーの変更を要求する。実際の移動は行わず、次回起動時に反映されるよう
    /// マーカーファイルを書くだけ。移動先は空の既存フォルダーでなければならない。
    /// </summary>
    public static void RequestMove(string newRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(newRoot);
        newRoot = Path.GetFullPath(newRoot);
        if (string.Equals(Path.GetFullPath(Current), newRoot, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("現在と同じ場所です。");

        Directory.CreateDirectory(newRoot);
        if (Directory.EnumerateFileSystemEntries(newRoot).Any())
            throw new InvalidOperationException("選択したフォルダーが空ではありません。空のフォルダーを選んでください。");

        Directory.CreateDirectory(DefaultRoot);
        File.WriteAllText(PendingMovePath, newRoot);
    }

    /// <summary>
    /// sourceRoot配下のすべてのファイル・フォルダーをdestinationRootへコピーし（excludedPathsに
    /// 指定したファイルだけは、保存先をどこへ変更しても必ず固定の場所に残すため対象外とする）、
    /// コピーがすべて成功したことを確認してからsourceRoot側を削除する。経路に依存しない純粋な
    /// 処理のため、単体テストで直接検証できる。
    /// </summary>
    public static void Move(string sourceRoot, string destinationRoot, IReadOnlyCollection<string> excludedPaths)
    {
        sourceRoot = Path.GetFullPath(sourceRoot);
        destinationRoot = Path.GetFullPath(destinationRoot);
        var excluded = excludedPaths.Select(Path.GetFullPath).ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (Directory.Exists(sourceRoot))
        {
            foreach (var directory in Directory.EnumerateDirectories(sourceRoot, "*", SearchOption.AllDirectories))
                Directory.CreateDirectory(Path.Combine(destinationRoot, Path.GetRelativePath(sourceRoot, directory)));

            foreach (var file in Directory.EnumerateFiles(sourceRoot, "*", SearchOption.AllDirectories))
            {
                if (excluded.Contains(Path.GetFullPath(file))) continue;
                var destination = Path.Combine(destinationRoot, Path.GetRelativePath(sourceRoot, file));
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                File.Copy(file, destination, overwrite: true);
            }
        }

        if (!Directory.Exists(sourceRoot)) return;
        foreach (var entry in Directory.EnumerateFileSystemEntries(sourceRoot))
        {
            if (excluded.Contains(Path.GetFullPath(entry))) continue;
            if (Directory.Exists(entry)) Directory.Delete(entry, true);
            else File.Delete(entry);
        }
    }
}
