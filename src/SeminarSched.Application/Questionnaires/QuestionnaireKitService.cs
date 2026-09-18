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
            var bySchoolLevel = subjects.GroupBy(x => ClassifySchoolLevel(x.SchoolLevel))
                .ToDictionary(g => g.Key, g => g.Select(x => new { x.Code, x.DisplayName }).ToArray());
            object[] Bucket(SchoolLevelGroup level) => bySchoolLevel.TryGetValue(level, out var list) ? list : [];
            var config = new
            {
                openDates = days.Select(x => x.Date.ToString("yyyy-MM-dd")),
                timeSlots = slots.Select(x => new { x.Code, x.DisplayName, start = x.StartTime.ToString("HH:mm"), end = x.EndTime.ToString("HH:mm") }),
                subjects = subjects.Select(x => new { x.Code, x.DisplayName }),
                subjectsByLevel = new
                {
                    elementary = Bucket(SchoolLevelGroup.Elementary),
                    juniorHigh = Bucket(SchoolLevelGroup.JuniorHigh),
                    seniorHigh = Bucket(SchoolLevelGroup.SeniorHigh),
                    other = Bucket(SchoolLevelGroup.Other),
                },
            };
            var json = JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(Path.Combine(temporary, "Code.gs"), BuildScript(json), new UTF8Encoding(false), cancellationToken);
            await File.WriteAllTextAsync(Path.Combine(temporary, "README.txt"), Readme, new UTF8Encoding(false), cancellationToken);
            Directory.Move(temporary, target);
            return target;
        }
        finally { if (Directory.Exists(temporary)) Directory.Delete(temporary, true); }
    }

    private enum SchoolLevelGroup { Elementary, JuniorHigh, SeniorHigh, Other }

    private static SchoolLevelGroup ClassifySchoolLevel(string schoolLevel)
    {
        if (schoolLevel.Contains('小')) return SchoolLevelGroup.Elementary;
        if (schoolLevel.Contains('中')) return SchoolLevelGroup.JuniorHigh;
        if (schoolLevel.Contains('高')) return SchoolLevelGroup.SeniorHigh;
        return SchoolLevelGroup.Other;
    }

    private static string BuildScript(string json) => $$"""
        // SeminarSched.WinUI generated Google Apps Script. Personal responses stay in your Google account.
        const CONFIG = {{json}};

        // Grade choices route to the matching school-level page; subjects tagged with an
        // unrecognized SchoolLevel land only in the override page (CONFIG.subjectsByLevel.other),
        // never silently dropped.
        const GRADE_CHOICES = [
          ['小1','elementary'],['小2','elementary'],['小3','elementary'],['小4','elementary'],['小5','elementary'],['小6','elementary'],
          ['中1','juniorHigh'],['中2','juniorHigh'],['中3','juniorHigh'],
          ['高1','seniorHigh'],['高2','seniorHigh'],['高3','seniorHigh'],
        ];

        function createSeminarSchedForms() {
          const student = createStudentForm();
          const teacher = createTeacherForm();
          const teacherQualification = createTeacherQualificationForm();
          Logger.log('生徒回答URL: ' + student.getPublishedUrl());
          Logger.log('講師回答URL: ' + teacher.getPublishedUrl());
          Logger.log('講師指導可能科目回答URL: ' + teacherQualification.getPublishedUrl());
        }

        function createStudentForm() {
          const form = FormApp.create('季節講習 生徒アンケート');
          form.addTextItem().setTitle('生徒ID').setRequired(true);
          form.addTextItem().setTitle('氏名').setRequired(true);
          const gradeItem = form.addListItem().setTitle('学年').setRequired(true);

          const elementaryPage = form.addPageBreakItem().setTitle('受講科目（小学校）');
          const elementaryYesNo = addLevelSubjectPage(form, '小学校', CONFIG.subjectsByLevel.elementary);
          const juniorPage = form.addPageBreakItem().setTitle('受講科目（中学校）');
          const juniorYesNo = addLevelSubjectPage(form, '中学校', CONFIG.subjectsByLevel.juniorHigh);
          const seniorPage = form.addPageBreakItem().setTitle('受講科目（高等学校）');
          const seniorYesNo = addLevelSubjectPage(form, '高等学校', CONFIG.subjectsByLevel.seniorHigh);

          const overridePage = form.addPageBreakItem().setTitle('他学年の受講科目');
          form.addParagraphTextItem().setTitle('特記事項').setRequired(false);
          addSubjectCheckboxIfAny(form, '追加で受講する科目（小学校）', CONFIG.subjectsByLevel.elementary);
          addSubjectCheckboxIfAny(form, '追加で受講する科目（中学校）', CONFIG.subjectsByLevel.juniorHigh);
          addSubjectCheckboxIfAny(form, '追加で受講する科目（高等学校）', CONFIG.subjectsByLevel.seniorHigh);
          addSubjectCheckboxIfAny(form, '追加で受講する科目（その他）', CONFIG.subjectsByLevel.other);

          const availabilityPage = form.addPageBreakItem().setTitle('参加可能日時');
          form.addMultipleChoiceItem().setTitle('学力テストの受験を希望しますか').setChoiceValues(['はい', 'いいえ']).setRequired(true);
          addAvailability(form);

          const levelPages = { elementary: elementaryPage, juniorHigh: juniorPage, seniorHigh: seniorPage };
          gradeItem.setChoices(GRADE_CHOICES.map(([label, level]) => gradeItem.createChoice(label, levelPages[level])));
          setYesNoBranch(elementaryYesNo, overridePage, availabilityPage);
          setYesNoBranch(juniorYesNo, overridePage, availabilityPage);
          setYesNoBranch(seniorYesNo, overridePage, availabilityPage);
          return form;
        }

        // Adds the level's subject checklist (if any) plus the "他学年も受講する" branch question,
        // and returns that MultipleChoiceItem so the caller can wire its per-choice page jump once
        // the destination pages exist.
        function addLevelSubjectPage(form, levelLabel, subjectsForLevel) {
          addSubjectCheckboxIfAny(form, levelLabel + 'の受講希望科目', subjectsForLevel);
          if (subjectsForLevel.length === 0) {
            form.addParagraphTextItem().setTitle('この学年区分に対応する科目は設定されていません。').setRequired(false);
          }
          return form.addMultipleChoiceItem().setTitle('中高一貫などで他学年の授業も受講しますか').setRequired(true);
        }

        function addSubjectCheckboxIfAny(form, title, subjectsForLevel) {
          if (subjectsForLevel.length === 0) return;
          form.addCheckboxItem().setTitle(title)
            .setChoiceValues(subjectsForLevel.map(x => x.code + ' ' + x.displayName));
        }

        function setYesNoBranch(yesNoItem, yesPage, noPage) {
          yesNoItem.setChoices([
            yesNoItem.createChoice('はい（他学年の科目も選ぶ）', yesPage),
            yesNoItem.createChoice('いいえ（このまま進む）', noPage),
          ]);
        }

        function createTeacherForm() {
          const form = FormApp.create('季節講習 講師勤務アンケート');
          form.addTextItem().setTitle('講師ID').setRequired(true);
          addAvailability(form);
          return form;
        }

        function addAvailability(form) {
          CONFIG.openDates.forEach(date => {
            form.addCheckboxItem().setTitle(date + ' 参加可能コマ')
              .setChoiceValues(CONFIG.timeSlots.map(x => x.code + ' ' + x.displayName + ' ' + x.start + '-' + x.end));
          });
        }

        // Separate from createTeacherForm (availability) because qualification only needs to be
        // collected once per teacher, not re-sent every time availability is surveyed.
        function createTeacherQualificationForm() {
          const form = FormApp.create('季節講習 講師指導可能科目アンケート');
          form.addTextItem().setTitle('講師ID').setRequired(true);
          addSubjectCheckboxIfAny(form, '指導可能科目（小学校）', CONFIG.subjectsByLevel.elementary);
          addSubjectCheckboxIfAny(form, '指導可能科目（中学校）', CONFIG.subjectsByLevel.juniorHigh);
          addSubjectCheckboxIfAny(form, '指導可能科目（高等学校）', CONFIG.subjectsByLevel.seniorHigh);
          addSubjectCheckboxIfAny(form, '指導可能科目（その他）', CONFIG.subjectsByLevel.other);
          return form;
        }
        """;

    private const string Readme = """
        SeminarSched Googleフォーム作成キット

        1. script.google.com で新しいプロジェクトを作成します。
        2. Code.gs の内容をすべて貼り付けます。
        3. createSeminarSchedForms を実行し、Googleの権限を承認します。
        4. 実行ログに表示された生徒用・講師用（勤務可能日時）・講師指導可能科目用の3つの回答URLを配布します。講師指導可能科目は毎回の講習期間ごとではなく、初回または科目構成が変わった時のみ配布すれば十分です。
        5. 生徒・講師（勤務可能日時）の回答をCSVまたはXLSXで保存し、SeminarSchedの③アンケート取込みで読み込みます。講師指導可能科目の回答は①設定の「担当設定」タブへ手動で反映してください（自動取込みは今後の課題です）。
        """;
}
