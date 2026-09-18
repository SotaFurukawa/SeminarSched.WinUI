using SeminarSched.Application.Scheduling;

namespace SeminarSched_WinUI;

// ④時間割編集と⑤時間割自動作成はPython版と同様に別画面（別Page）だが、元に戻す・やり直す履歴は
// 両画面をまたいで保持する必要がある（⑤で自動作成した結果を④で取り消せるように）。WinUIの
// Frame.Navigateはページ遷移のたびに新しいPageインスタンスを作るため、履歴はページの外（ここ）に置く。
internal static class ScheduleUndoState
{
    public static Stack<ScheduleSnapshot> UndoStack { get; } = new();
    public static Stack<ScheduleSnapshot> RedoStack { get; } = new();

    public static void Push(ScheduleSnapshot snapshot)
    {
        UndoStack.Push(snapshot);
        RedoStack.Clear();
    }

    public static void Clear()
    {
        UndoStack.Clear();
        RedoStack.Clear();
    }
}
