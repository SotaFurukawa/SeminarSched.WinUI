using System.Text;
using System.Text.Json;
using SeminarSched.Application.CourseSettings;
using SeminarSched.Application.MasterData;

namespace SeminarSched.Application.Questionnaires;

public sealed class QuestionnaireKitService
{
    private readonly ICourseSettingsRepository _courseSettings;
    private readonly IMasterDataRepository _masterData;

    public QuestionnaireKitService(ICourseSettingsRepository courseSettings, IMasterDataRepository masterData)
    { _courseSettings = courseSettings; _masterData = masterData; }

    public async Task<string> GenerateAsync(string projectPath, string parentDirectory, CancellationToken cancellationToken = default)
    {
        var days = (await _courseSettings.GetCourseDaysAsync(projectPath, cancellationToken)).Where(x => x.IsOpen).ToArray();
        var slots = (await _courseSettings.GetTimeSlotsAsync(projectPath, cancellationToken)).Where(x => x.Active).ToArray();
        var subjects = (await _masterData.GetSubjectsAsync(projectPath, false, cancellationToken)).ToArray();
        if (days.Length == 0) throw new InvalidOperationException("開校日がありません。①設定を確認してください。");
        if (slots.Length == 0) throw new InvalidOperationException("有効なコマがありません。①設定を確認してください。");
        if (subjects.Length == 0) throw new InvalidOperationException("有効な科目がありません。①設定を確認してください。");

        var target = Path.Combine(Path.GetFullPath(parentDirectory), $"SeminarSched_Forms_{DateTime.Now:yyyyMMdd_HHmmss}");
        if (Directory.Exists(target)) throw new IOException("同名の出力フォルダーが既にあります。");
        var temporary = target + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            Directory.CreateDirectory(temporary);
            var config = new
            {
                openDates = days.Select(x => x.Date.ToString("yyyy-MM-dd")),
                timeSlots = slots.Select(x => new { x.Code, x.DisplayName, start = x.StartTime.ToString("HH:mm"), end = x.EndTime.ToString("HH:mm") }),
                subjects = subjects.Select(x => new { x.Code, x.DisplayName }),
            };
            var json = JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(Path.Combine(temporary, "Code.gs"), BuildScript(json), new UTF8Encoding(false), cancellationToken);
            await File.WriteAllTextAsync(Path.Combine(temporary, "README.txt"), Readme, new UTF8Encoding(false), cancellationToken);
            Directory.Move(temporary, target);
            return target;
        }
        finally { if (Directory.Exists(temporary)) Directory.Delete(temporary, true); }
    }

    private static string BuildScript(string json) => $$"""
        // SeminarSched.WinUI generated Google Apps Script. Personal responses stay in your Google account.
        const CONFIG = {{json}};

        function createSeminarSchedForms() {
          const student = FormApp.create('季節講習 生徒アンケート');
          student.addTextItem().setTitle('生徒ID').setRequired(true);
          student.addCheckboxItem().setTitle('受講希望科目').setChoiceValues(CONFIG.subjects.map(x => x.code + ' ' + x.displayName)).setRequired(true);
          addAvailability(student);

          const teacher = FormApp.create('季節講習 講師勤務アンケート');
          teacher.addTextItem().setTitle('講師ID').setRequired(true);
          addAvailability(teacher);
          Logger.log('生徒回答URL: ' + student.getPublishedUrl());
          Logger.log('講師回答URL: ' + teacher.getPublishedUrl());
        }

        function addAvailability(form) {
          CONFIG.openDates.forEach(date => {
            form.addCheckboxItem().setTitle(date + ' 参加可能コマ')
              .setChoiceValues(CONFIG.timeSlots.map(x => x.code + ' ' + x.displayName + ' ' + x.start + '-' + x.end));
          });
        }
        """;

    private const string Readme = """
        SeminarSched Googleフォーム作成キット

        1. script.google.com で新しいプロジェクトを作成します。
        2. Code.gs の内容をすべて貼り付けます。
        3. createSeminarSchedForms を実行し、Googleの権限を承認します。
        4. 実行ログに表示された生徒用・講師用回答URLを配布します。
        5. 回答をCSVまたはXLSXで保存し、SeminarSchedの③アンケート取込みで読み込みます。
        """;
}
