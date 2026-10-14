namespace SeminarSched.Application.MasterData;

/// <summary>
/// Python版のshared_roster管理相当。プロジェクトを開いていなくても使える、年度をまたぐ
/// 共通の生徒・講師名簿を1か所（固定パス）で管理し、プロジェクト作成・切り替え時に
/// そこから自動反映できるようにする。
/// </summary>
public interface ISharedRosterStore
{
    string WorkbookPath { get; }

    /// <summary>共通正本のデータベースファイルパス。生徒・講師・科目・講師指導可能科目・通常授業担当設定を
    /// プロジェクトを開かずに直接編集する「0 基本情報設定」画面が、共通名簿Excelと同じデータへ直接
    /// アクセスするために使う（既存のGetStudentsAsync等、プロジェクトパスを受け取る既存のMasterData
    /// リポジトリのメソッドを、プロジェクトパスの代わりにこのパスで呼び出せる）。</summary>
    string DatabasePath { get; }

    /// <summary>共通名簿Excelが無ければ現在の内容（未反映なら空）から新規作成し、パスを返す。
    /// 既に存在する場合は再生成せず、そのまま返す（編集中の内容を上書きしないため）。</summary>
    Task<string> EnsureWorkbookAsync(CancellationToken cancellationToken = default);

    /// <summary>共通正本を変更せず、入力用の新しい空の名簿テンプレートを指定パスへ保存する。</summary>
    Task ExportBlankTemplateAsync(string targetPath, CancellationToken cancellationToken = default);

    Task<SharedRosterPreview> PreviewImportAsync(string sourceWorkbookPath, CancellationToken cancellationToken = default);

    /// <summary>検証済みの内容を共通正本へ反映する。</summary>
    Task<SharedRosterImportResult> ApplyImportAsync(SharedRosterPreview preview, CancellationToken cancellationToken = default);

    /// <summary>共通正本の現在の内容を、指定したプロジェクトへ反映する。共通正本が空なら何もしない。</summary>
    Task<SharedRosterImportResult?> CopyIntoProjectAsync(string projectPath, CancellationToken cancellationToken = default);

    /// <summary>新しい季節講習を迎える際に、在籍中の生徒全員の学年をまとめて1つ繰り上げる。
    /// 高3だった生徒は既卒として在籍を停止する（グレー表示・使用しない扱いになる）。</summary>
    Task<GradeAdvancementSummary> AdvanceStudentGradesAsync(CancellationToken cancellationToken = default);

    /// <summary>共通正本の現在の内容から、共通名簿Excelを無条件に（既存ファイルの有無に関わらず）
    /// 再生成する。「0 基本情報設定」画面で生徒・講師等を直接編集した変更を、保存の都度すぐに
    /// Excelへ反映するために使う（EnsureWorkbookAsyncと異なり、既存ファイルを編集中でも常に
    /// 最新の内容で上書きする）。</summary>
    Task RefreshWorkbookAsync(CancellationToken cancellationToken = default);
}

/// <summary>学年繰り上げ操作の結果件数。</summary>
public sealed record GradeAdvancementSummary(int AdvancedCount, int GraduatedCount);
