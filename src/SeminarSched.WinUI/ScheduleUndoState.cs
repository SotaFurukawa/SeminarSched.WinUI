using SeminarSched.Application.Scheduling;

namespace SeminarSched_WinUI;

// ④時間割編集と⑤時間割自動作成はPython版と同様に別画面（別Page）だが、元に戻す・やり直す履歴は
// 両画面をまたいで保持する必要がある（⑤で自動作成した結果を④で取り消せるように）。WinUIの
// Frame.Navigateはページ遷移のたびに新しいPageインスタンスを作るため、履歴はページの外（ここ）に置く。
internal static class ScheduleUndoState
{
    public static Stack<ScheduleSnapshot> UndoStack { get; } = new();
    public static Stack<ScheduleSnapshot> RedoStack { get; } = new();

    // Python版の④時間割編集「差分」タブ相当。⑤で「時間割を自動作成」を押す直前の状態だけを別途
    // 覚えておき、④へ戻ったときに現在の状態との差分（新規配置・日時変更・講師変更・未配置化）を
    // 表示できるようにする。通常のundo/redo履歴とは独立（reset等でも消さない）。
    public static ScheduleSnapshot? ReoptimizationBaseline { get; set; }

    public static void Push(ScheduleSnapshot snapshot)
    {
        UndoStack.Push(snapshot);
        RedoStack.Clear();
    }

    public static void Clear()
    {
        UndoStack.Clear();
        RedoStack.Clear();
        ReoptimizationBaseline = null;
    }
}
