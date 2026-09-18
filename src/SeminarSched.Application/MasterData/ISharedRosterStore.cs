namespace SeminarSched.Application.MasterData;

/// <summary>
/// Python版のshared_roster管理相当。プロジェクトを開いていなくても使える、年度をまたぐ
/// 共通の生徒・講師名簿を1か所（固定パス）で管理し、プロジェクト作成・切り替え時に
/// そこから自動反映できるようにする。
/// </summary>
public interface ISharedRosterStore
{
    string WorkbookPath { get; }

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
}
