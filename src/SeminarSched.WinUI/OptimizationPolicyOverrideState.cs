using SeminarSched.Domain.Scheduling;

namespace SeminarSched_WinUI;

/// <summary>
/// ⑤時間割自動作成の「この回だけ探索の方針を変更する」で入力した内容を、別のページへ移動して
/// 戻ってきても保持するための一時的な状態（プロジェクトファイルへは保存しない）。
/// ユーザー報告（checkpoint129）「ラジオボタンで考慮するを押した後、別のページへ遷移すると、
/// 元に戻ってしまう。ウィンドウを閉じたり、プロジェクトを変更しない限りはそのままにしておいて
/// ほしい」への対応。OptimizationPageはNavigationCacheMode="Required"でPageインスタンス自体は
/// 使い回されるが、Page_Loadedが毎回プロジェクトの保存済み既定値へ強制的に上書きしていたため、
/// 別ページへ行って戻ると消えていた。ウィンドウを閉じれば（プロセス終了でstatic自体が破棄される
/// ため）自動的にリセットされ、プロジェクトを切り替えればパスの不一致で自動的に無視される。
/// </summary>
public static class OptimizationPolicyOverrideState
{
    private static string? _projectPath;
    private static SavedState? _state;

    public static bool TryGet(string projectPath, out SavedState state)
    {
        if (_projectPath == projectPath && _state is { } saved)
        {
            state = saved;
            return true;
        }

        state = default;
        return false;
    }

    public static void Save(string projectPath, SavedState state)
    {
        _projectPath = projectPath;
        _state = state;
    }

    public readonly record struct SavedState(
        int MaxStudentsPerTeacher,
        int MaxConcurrentSeats,
        bool ContinueBeyondNominalTimeIfIncomplete,
        IReadOnlyList<(SchedulingPolicyDimension Dimension, int SelectedOptionIndex)> Rows);
}
