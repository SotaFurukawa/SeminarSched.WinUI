using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using SeminarSched.Application.CourseSettings;
using SeminarSched.Application.MasterData;
using SeminarSched.Domain.MasterData;

namespace SeminarSched.Application.Questionnaires;

/// <summary>
/// Python版questionnaire_script_service.pyの移植。生成したGoogleフォームの回答は
/// <see cref="Importing.ICourseSurveyImportService"/>がそのまま取り込める列構成になる
/// （姓・名の分割、学年、在籍区分、教科ごとの学校区分・受講教科・受講回数、日付ごとの
/// 受講/出勤不可日時列）。3つのApps Scriptは共通の本体テンプレートを種類ごとの設定値
/// （kind・関数名・プロパティキー）だけ差し替えて生成する。
/// </summary>
public sealed class QuestionnaireKitService
{
    private readonly ICourseSettingsRepository _courseSettings;
    private readonly IMasterDataRepository _masterData;

    public QuestionnaireKitService(ICourseSettingsRepository courseSettings, IMasterDataRepository masterData)
    { _courseSettings = courseSettings; _masterData = masterData; }

    public async Task<string> GenerateAsync(string projectPath, string parentDirectory, string projectTitle, string studentTitle, string teacherTitle, string studentDeadline, string teacherDeadline, string contact, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectTitle);
        ArgumentException.ThrowIfNullOrWhiteSpace(studentTitle);
        ArgumentException.ThrowIfNullOrWhiteSpace(teacherTitle);
        ArgumentException.ThrowIfNullOrWhiteSpace(studentDeadline);
        ArgumentException.ThrowIfNullOrWhiteSpace(teacherDeadline);
        ArgumentException.ThrowIfNullOrWhiteSpace(contact);

        var days = (await _courseSettings.GetCourseDaysAsync(projectPath, cancellationToken).ConfigureAwait(false)).Where(x => x.IsOpen).OrderBy(x => x.Date).ToArray();
        var slots = (await _courseSettings.GetTimeSlotsAsync(projectPath, cancellationToken).ConfigureAwait(false)).Where(x => x.Active).OrderBy(x => x.SortOrder).ToArray();
        var subjects = await _masterData.GetSubjectsAsync(projectPath, includeInactive: false, cancellationToken).ConfigureAwait(false);
        if (days.Length == 0) throw new InvalidOperationException("開校日がありません。①設定を確認してください。");
        if (slots.Length == 0) throw new InvalidOperationException("有効なコマがありません。①設定を確認してください。");
        if (subjects.Count == 0) throw new InvalidOperationException("有効な科目がありません。①設定を確認してください。");

        var openDates = days.Select(x => x.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)).ToArray();
        var timeSlots = slots.Select(x => $"{x.Code} {x.StartTime:HH\\:mm}～{x.EndTime:HH\\:mm}").ToArray();

        var bySchoolLevel = subjects.GroupBy(x => ClassifySchoolLevel(x.SchoolLevel)).ToDictionary(g => g.Key, g => g.ToArray());
        Subject[] Bucket(SchoolLevelGroup level) => bySchoolLevel.TryGetValue(level, out var list) ? list : [];
        var elementary = Bucket(SchoolLevelGroup.Elementary);
        var juniorHigh = Bucket(SchoolLevelGroup.JuniorHigh);
        var highSchool = Bucket(SchoolLevelGroup.SeniorHigh);
        if (elementary.Length == 0 || juniorHigh.Length == 0 || highSchool.Length == 0)
            throw new InvalidOperationException("生徒用フォームに必要な小学校・中学校・高校の使用中科目が不足しています。①の「科目」を確認してください。");

        var studentSubjectsBySchoolLevel = new Dictionary<string, string[]>
        {
            ["elementary"] = elementary.Select(x => StudentSubjectLabel(x.DisplayName)).ToArray(),
            ["juniorHigh"] = juniorHigh.Select(x => StudentSubjectLabel(x.DisplayName)).ToArray(),
            ["highSchool"] = highSchool.Select(x => StudentSubjectLabel(x.DisplayName)).ToArray(),
        };
        var studentSubjectChoices = studentSubjectsBySchoolLevel.Values.SelectMany(x => x).Distinct().ToArray();
        var teacherSubjectsBySchoolLevel = new Dictionary<string, string[]>
        {
            ["elementary"] = elementary.Select(x => x.DisplayName).ToArray(),
            ["juniorHigh"] = juniorHigh.Select(x => x.DisplayName).ToArray(),
            ["highSchool"] = highSchool.Select(x => x.DisplayName).ToArray(),
        };

        var target = AvailableDirectory(Path.Combine(Path.GetFullPath(parentDirectory), $"Googleフォーム_{SafeFileName(projectTitle)}_{DateTime.Now:yyyyMMdd_HHmmss}"));
        var temporary = target + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            Directory.CreateDirectory(temporary);
            var jsonOptions = new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

            var studentConfig = new
            {
                kind = "student",
                title = studentTitle,
                deadline = studentDeadline,
                contact,
                description = "個別指導コースの受講申込フォームです。メールアドレス、お子様のお名前、学年、受講教科・回数、受講できない日時をご回答ください。\n\n回答は講習の受付、時間割作成、内容確認、必要な連絡にだけ使用します。回答先スプレッドシートの閲覧者は担当者に限定してください。",
                openDates,
                timeSlots,
                gradeGroups = new { elementary = GradeGroupElementary, juniorHigh = GradeGroupJuniorHigh, highSchool = GradeGroupHighSchool },
                enrollmentTypes = new[] { "在籍生", "体験生" },
                schoolLevels = new[] { "小学校", "中学校", "高校" },
                subjectsBySchoolLevel = studentSubjectsBySchoolLevel,
                studentSubjectChoices,
                sessionCounts = Enumerable.Range(1, 20).Select(x => x.ToString(CultureInfo.InvariantCulture)).ToArray(),
                summerTestChoices = new[] { "夏期学力テストを受験する", "夏期学力テストを受験しない", "対象外（小1～小3・高校生）" },
            };
            var teacherConfig = new
            {
                kind = "teacher",
                title = teacherTitle,
                deadline = teacherDeadline,
                contact,
                description = "講習の出勤可能日時を確認するフォームです。出勤できない日時にだけチェックを入れてください。\n\n回答は勤務希望の確認、時間割作成、内容確認、必要な連絡にだけ使用します。回答先スプレッドシートの閲覧者は担当者に限定してください。",
                openDates,
                timeSlots,
            };
            var teacherSubjectConfig = new
            {
                kind = "teacher_subject",
                title = $"{projectTitle} 講師 指導可能科目アンケート",
                deadline = teacherDeadline,
                contact,
                description = "現在、講師本人が単独で授業を進められる指導可能科目を確認するフォームです。小学校・中学校・高校から、該当する科目をすべて選択してください。\n\n回答は講師マスターの更新、担当可能科目の確認、時間割作成にだけ使用します。回答先スプレッドシートの閲覧者は担当者に限定してください。",
                subjectsBySchoolLevel = teacherSubjectsBySchoolLevel,
            };

            await File.WriteAllTextAsync(Path.Combine(temporary, "create_student_questionnaire.gs"),
                RenderScript(studentConfig, jsonOptions, "SUMMER_SCHEDULER_STUDENT_FORM_ID", "SUMMER_SCHEDULER_STUDENT_RESPONSE_SHEET_ID", "createStudentQuestionnaire", "showCreatedQuestionnaireUrls", "createReplacementStudentQuestionnaire", "生徒・保護者用"),
                new UTF8Encoding(false), cancellationToken).ConfigureAwait(false);
            await File.WriteAllTextAsync(Path.Combine(temporary, "create_teacher_questionnaire.gs"),
                RenderScript(teacherConfig, jsonOptions, "SUMMER_SCHEDULER_TEACHER_FORM_ID", "SUMMER_SCHEDULER_TEACHER_RESPONSE_SHEET_ID", "createTeacherQuestionnaire", "showCreatedTeacherQuestionnaireUrls", "createReplacementTeacherQuestionnaire", "講師用"),
                new UTF8Encoding(false), cancellationToken).ConfigureAwait(false);
            await File.WriteAllTextAsync(Path.Combine(temporary, "create_teacher_subject_questionnaire.gs"),
                RenderScript(teacherSubjectConfig, jsonOptions, "SUMMER_SCHEDULER_TEACHER_SUBJECT_FORM_ID", "SUMMER_SCHEDULER_TEACHER_SUBJECT_RESPONSE_SHEET_ID", "createTeacherSubjectQuestionnaire", "showCreatedTeacherSubjectQuestionnaireUrls", "createReplacementTeacherSubjectQuestionnaire", "講師指導可能科目用"),
                new UTF8Encoding(false), cancellationToken).ConfigureAwait(false);
            await File.WriteAllTextAsync(Path.Combine(temporary, "Googleフォーム作成手順.txt"), BuildInstructions(projectTitle, days.Length, slots.Length), new UTF8Encoding(false), cancellationToken).ConfigureAwait(false);
            Directory.Move(temporary, target);
            return target;
        }
        finally { if (Directory.Exists(temporary)) Directory.Delete(temporary, true); }
    }

    private static readonly string[] GradeGroupElementary = ["小1", "小2", "小3", "小4", "小5", "小6"];
    private static readonly string[] GradeGroupJuniorHigh = ["中1", "中2", "中3"];
    private static readonly string[] GradeGroupHighSchool = ["高1", "高2", "高3"];

    private enum SchoolLevelGroup { Elementary, JuniorHigh, SeniorHigh, Other }

    private static SchoolLevelGroup ClassifySchoolLevel(string schoolLevel)
    {
        if (schoolLevel.Contains('小')) return SchoolLevelGroup.Elementary;
        if (schoolLevel.Contains('中')) return SchoolLevelGroup.JuniorHigh;
        if (schoolLevel.Contains('高')) return SchoolLevelGroup.SeniorHigh;
        return SchoolLevelGroup.Other;
    }

    // 生徒フォームでは学校段階の接頭辞を省き、選択肢を簡潔にする（Python版_student_subject_label相当）。
    private static string StudentSubjectLabel(string displayName)
    {
        var label = displayName;
        foreach (var prefix in new[] { "小学校・", "中学校・", "高校・" })
            if (label.StartsWith(prefix, StringComparison.Ordinal)) { label = label[prefix.Length..]; break; }
        return label.Replace("（中学受験以外なら可能）", "（中学受験以外）");
    }

    private static string RenderScript(object config, JsonSerializerOptions jsonOptions, string formIdProperty, string sheetIdProperty, string createFunction, string showFunction, string replacementFunction, string kindLabel)
    {
        var json = JsonSerializer.Serialize(config, jsonOptions);
        return ScriptTemplate
            .Replace("__CONFIG_JSON__", json)
            .Replace("__FORM_ID_PROPERTY__", formIdProperty)
            .Replace("__SHEET_ID_PROPERTY__", sheetIdProperty)
            .Replace("__CREATE_FUNCTION__", createFunction)
            .Replace("__SHOW_FUNCTION__", showFunction)
            .Replace("__REPLACEMENT_FUNCTION__", replacementFunction)
            .Replace("__KIND_LABEL__", kindLabel)
            .TrimEnd() + "\n";
    }

    private static string SafeFileName(string value)
    {
        var cleaned = Regex.Replace(value.Trim(), "[<>:\"/\\\\|?* -]", "_");
        cleaned = cleaned.TrimEnd('.', ' ');
        if (cleaned.Length > 60) cleaned = cleaned[..60];
        return cleaned.Length == 0 ? "講習" : cleaned;
    }

    private static string AvailableDirectory(string basePath)
    {
        var candidate = basePath;
        var suffix = 2;
        while (Directory.Exists(candidate)) { candidate = $"{basePath}_{suffix}"; suffix++; }
        return candidate;
    }

    private static string BuildInstructions(string projectTitle, int openDateCount, int timeSlotCount) => $$"""
        {{projectTitle}} Googleフォーム作成手順

        このフォルダーには、①で設定した開校日{{openDateCount}}日・有効コマ{{timeSlotCount}}件を
        反映した生徒用／講師勤務日時用／講師指導可能科目用Google Apps Scriptが入っています。

        【生徒用：上から順に進めます】
        1. アプリの「作成キットを保存」から作成キットを保存します。
        2. 保存後に表示される「保存先を開く」を押します。
        3. create_student_questionnaire.gsを右クリックし、「プログラムから開く」から
           「メモ帳」を選び、表示されたコードを先頭から最後までコピーします。
        4. https://script.google.com/home を開き、「新しいプロジェクト」を作ります。
           Code.gsに最初から入っているコードはすべて削除します。
        5. メモ帳からコピーしたコードをCode.gsへ貼り付けます。
        6. Ctrl+Sまたはフロッピーディスクのボタンで保存し、関数が
           createStudentQuestionnaireであることを確認して「実行」を押します。
        7. 「承認が必要です」と表示されたら「権限を確認」を押します。
        8. 「このアプリはGoogleで確認されていません」と表示された場合は「詳細」を押し、
           「無題のプロジェクト（安全ではないページ）に移動」を押します。
        9. 権限画面で「すべて選択」にチェックを入れ、内容を確認して「続行」を押します。
        10. 実行ログの回答URLからアンケートを開きます。フォーム編集URLと回答原本URLは
            担当者だけで管理し、回答URLだけを生徒へ案内します。

        【講師用・講師指導可能科目用】
        上記と同じ1～10の手順を、別々のApps Scriptプロジェクトで繰り返します。
        - 講師勤務日時用：create_teacher_questionnaire.gs／createTeacherQuestionnaire
        - 講師指導可能科目用：create_teacher_subject_questionnaire.gs／
          createTeacherSubjectQuestionnaire
        講師指導可能科目用は、完成後に科目一覧と説明文を確認します。
        講師のメールアドレスは収集しません。

        Google Apps Scriptの「デプロイ」は不要です。配布前に、タイトル、締切、質問、開校日、
        コマ、回答先スプレッドシートの共有範囲を必ず確認してください。

        生徒・講師勤務日時の回答後はGoogleスプレッドシートの「Form Responses 1」シートを
        xlsxまたはCSVでダウンロードし、アプリの③「アンケート取込み」で生徒回答／講師回答を
        選んで検証・反映します。指導可能科目の回答は回答原本で確認し、共通名簿Excel
        （生徒・講師_基本情報.xlsx）の「講師対応科目」へ、校舎側で内容を確認して反映してください。

        このスクリプトはフォーム作成時だけGoogleへアクセスします。アプリ本体はGoogleへ接続せず、
        回答や個人情報を外部へ送信しません。
        """;

    // Python版questionnaire_script_service.pyのApps Scriptテンプレートの移植。生徒・講師・
    // 講師指導可能科目の3種類は本体が共通で、__プレースホルダー__部分だけ種類ごとに差し替える。
    // 生成されるフォーム回答（正規化後のスプレッドシート列）はICourseSurveyImportServiceが
    // そのまま取り込める列名になっている。
    private const string ScriptTemplate = """
        /**
         * SeminarSched.WinUIが生成した__KIND_LABEL__Googleフォーム作成スクリプト。
         * Google Apps ScriptのCode.gsへ全内容を貼り付けて使用します。
         */

        const QUESTIONNAIRE_CONFIG = Object.freeze(__CONFIG_JSON__);
        const FORM_ID_PROPERTY = "__FORM_ID_PROPERTY__";
        const SPREADSHEET_ID_PROPERTY = "__SHEET_ID_PROPERTY__";

        /** フォームと回答先スプレッドシートを1組だけ作成する。 */
        function __CREATE_FUNCTION__() {
          validateQuestionnaireConfig_();
          const properties = PropertiesService.getScriptProperties();
          const existingFormId = properties.getProperty(FORM_ID_PROPERTY);
          if (existingFormId) {
            try {
              const existingForm = FormApp.openById(existingFormId);
              logQuestionnaireUrls_(
                existingForm,
                properties.getProperty(SPREADSHEET_ID_PROPERTY),
              );
              throw new Error(
                "このスクリプトではフォームを作成済みです。" +
                  "重複作成せず、実行ログのURLから既存フォームを開いてください。",
              );
            } catch (error) {
              if (String(error).includes("フォームを作成済みです")) throw error;
              properties.deleteProperty(FORM_ID_PROPERTY);
              properties.deleteProperty(SPREADSHEET_ID_PROPERTY);
            }
          }

          const form = FormApp.create(QUESTIONNAIRE_CONFIG.title, true)
            .setDescription(
              QUESTIONNAIRE_CONFIG.description +
                `\n\n回答締切: ${QUESTIONNAIRE_CONFIG.deadline}` +
                `\n問い合わせ先: ${QUESTIONNAIRE_CONFIG.contact}`,
            )
            .setCollectEmail(QUESTIONNAIRE_CONFIG.kind === "student")
            .setProgressBar(true)
            .setShowLinkToRespondAgain(false)
            .setAllowResponseEdits(true)
            .setConfirmationMessage(
              "回答を受け付けました。修正が必要な場合は、回答編集リンクを使用するか、" +
                QUESTIONNAIRE_CONFIG.contact + "。",
            );

          form
            .addMultipleChoiceItem()
            .setTitle("個人情報の利用目的への同意（必須）")
            .setHelpText(
              QUESTIONNAIRE_CONFIG.kind === "student"
                ? "回答を講習の受付、時間割作成、内容確認、必要な連絡に使用します。"
                : QUESTIONNAIRE_CONFIG.kind === "teacher_subject"
                  ? "回答を講師マスターの更新、担当可能科目の確認、時間割作成に使用します。"
                  : "回答を勤務希望の確認、時間割作成、内容確認、必要な連絡に使用します。",
            )
            .setChoiceValues(["上記の利用目的を確認し、回答します"])
            .setRequired(true);
          form
            .addTextItem()
            .setTitle("姓（苗字）（必須）")
            .setHelpText("姓（苗字）をご記入ください。例: 山田")
            .setRequired(true);
          form
            .addTextItem()
            .setTitle("名（必須）")
            .setHelpText("名をご記入ください。例: 太郎")
            .setRequired(true);

          if (QUESTIONNAIRE_CONFIG.kind === "student") {
            addStudentQuestions_(form);
          } else if (QUESTIONNAIRE_CONFIG.kind === "teacher") {
            addTeacherQuestions_(form);
          } else {
            addTeacherSubjectQuestions_(form);
          }

          const spreadsheet = SpreadsheetApp.create(`${QUESTIONNAIRE_CONFIG.title} 回答原本`);
          form.setDestination(FormApp.DestinationType.SPREADSHEET, spreadsheet.getId());
          if (QUESTIONNAIRE_CONFIG.kind === "student") {
            prepareStudentResponseSheets_(spreadsheet);
            installStudentResponseNormalizer_(spreadsheet.getId());
          }
          properties.setProperty(FORM_ID_PROPERTY, form.getId());
          properties.setProperty(SPREADSHEET_ID_PROPERTY, spreadsheet.getId());
          logQuestionnaireUrls_(form, spreadsheet.getId());
        }

        function addStudentQuestions_(form) {
          const gradeItem = form
            .addListItem()
            .setTitle("学年（必須）")
            .setRequired(true);
          form
            .addListItem()
            .setTitle("在籍区分（必須）")
            .setChoiceValues(QUESTIONNAIRE_CONFIG.enrollmentTypes)
            .setRequired(true);
          const otherGradeItem = form
            .addMultipleChoiceItem()
            .setTitle("中高一貫などで他学年の授業を受講される際はこちらにチェックを入れてください")
            .setHelpText("他学年の科目も選ぶ場合だけ選択してください。該当しない場合は未回答のまま進んでください。")
            .setRequired(false);

          const elementaryPage = form
            .addPageBreakItem()
            .setTitle("受講教科・回数（小学校）")
            .setHelpText("最大4教科まで回答してください。学校区分は小学校として自動的に扱います。");
          addSubjectRequestSection_(
            form,
            QUESTIONNAIRE_CONFIG.subjectsBySchoolLevel.elementary,
            "小学校",
          );

          const juniorHighPage = form
            .addPageBreakItem()
            .setTitle("受講教科・回数（中学校）")
            .setHelpText("最大4教科まで回答してください。学校区分は中学校として自動的に扱います。");
          addSubjectRequestSection_(
            form,
            QUESTIONNAIRE_CONFIG.subjectsBySchoolLevel.juniorHigh,
            "中学校",
          );

          const highSchoolPage = form
            .addPageBreakItem()
            .setTitle("受講教科・回数（高校）")
            .setHelpText("最大4教科まで回答してください。学校区分は高校として自動的に扱います。");
          addSubjectRequestSection_(
            form,
            QUESTIONNAIRE_CONFIG.subjectsBySchoolLevel.highSchool,
            "高校",
          );

          const otherGradePage = form
            .addPageBreakItem()
            .setTitle("受講教科・回数（他学年を含む）")
            .setHelpText("最大4教科まで、各教科の学校区分・受講教科・受講回数を回答してください。");
          addSubjectRequestSection_(form, QUESTIONNAIRE_CONFIG.studentSubjectChoices);

          const availabilityPage = addAvailabilityPage_(form, "受講");
          addAvailabilityGrid_(form, "受講不可日時（チェックしたコマは受講不可）");
          form.addPageBreakItem().setTitle("確認・特記事項");
          form
            .addMultipleChoiceItem()
            .setTitle("受講不可日時の確認（必須）")
            .setHelpText("上でチェックした日時が、受講できない日時で間違いないか確認してください。")
            .setChoiceValues(["間違いありません"])
            .setRequired(true);
          form
            .addParagraphTextItem()
            .setTitle("特記事項")
            .setHelpText("例: 1日に英数連続2コマで組んでほしい、送迎は20時まで、など。")
            .setRequired(false);
          form
            .addMultipleChoiceItem()
            .setTitle("夏期講習学力テスト")
            .setHelpText("個別指導生の夏期学力テストは選択制です（小4～中3対象）。")
            .setChoiceValues(QUESTIONNAIRE_CONFIG.summerTestChoices)
            .setRequired(false);

          // 各PageBreakItemは直前のページの遷移先を設定する。最後の他学年ページは
          // 直後のavailabilityPageへ通常遷移するため、追加設定は不要。
          juniorHighPage.setGoToPage(availabilityPage);
          highSchoolPage.setGoToPage(availabilityPage);
          otherGradePage.setGoToPage(availabilityPage);
          gradeItem.setChoices([
            ...QUESTIONNAIRE_CONFIG.gradeGroups.elementary.map((grade) =>
              gradeItem.createChoice(grade, elementaryPage),
            ),
            ...QUESTIONNAIRE_CONFIG.gradeGroups.juniorHigh.map((grade) =>
              gradeItem.createChoice(grade, juniorHighPage),
            ),
            ...QUESTIONNAIRE_CONFIG.gradeGroups.highSchool.map((grade) =>
              gradeItem.createChoice(grade, highSchoolPage),
            ),
          ]);
          // 同じページの最後のナビゲーション質問を優先し、選択時だけ全校種ページへ進める。
          otherGradeItem.setChoices([
            otherGradeItem.createChoice("他学年の授業を受講する", otherGradePage),
          ]);
        }

        function addTeacherQuestions_(form) {
          addAvailabilityPage_(form, "出勤");
          addAvailabilityGrid_(form, "出勤不可日時（チェックしたコマは出勤不可）");
          form
            .addMultipleChoiceItem()
            .setTitle("出勤不可日時の確認（必須）")
            .setHelpText("上でチェックした日時が、出勤できない日時で間違いないか確認してください。")
            .setChoiceValues(["間違いありません"])
            .setRequired(true);
          form
            .addParagraphTextItem()
            .setTitle("勤務に関する特記事項")
            .setHelpText("連続勤務、到着・退出時刻など、日程について必要な事項をご記入ください。")
            .setRequired(false);
        }

        function addTeacherSubjectQuestions_(form) {
          form
            .addSectionHeaderItem()
            .setTitle("現在の指導可能科目")
            .setHelpText(
              "教材を使い、講師本人が単独で授業を進められる科目をすべて選択してください。" +
                "未経験、補助が必要、または現在は担当できない科目は選択しないでください。",
            );
          addTeacherSubjectCheckbox_(
            form,
            "指導可能科目（小学校）",
            QUESTIONNAIRE_CONFIG.subjectsBySchoolLevel.elementary,
          );
          addTeacherSubjectCheckbox_(
            form,
            "指導可能科目（中学校）",
            QUESTIONNAIRE_CONFIG.subjectsBySchoolLevel.juniorHigh,
          );
          addTeacherSubjectCheckbox_(
            form,
            "指導可能科目（高校）",
            QUESTIONNAIRE_CONFIG.subjectsBySchoolLevel.highSchool,
          );
          form
            .addMultipleChoiceItem()
            .setTitle("指導可能科目の確認（必須）")
            .setHelpText(
              "選択した科目だけを現在の指導可能科目として回答することを確認してください。" +
                "1科目もない場合は、2つ目を選択してください。",
            )
            .setChoiceValues([
              "上記で選択した科目を現在指導できます",
              "現在指導可能な科目はありません",
            ])
            .setRequired(true);
          form
            .addParagraphTextItem()
            .setTitle("指導可能科目に関する補足")
            .setHelpText(
              "例: 高校数学は数学I・Aのみ、受験指導は要相談、研修後に追加可能、など。",
            )
            .setRequired(false);
        }

        function addTeacherSubjectCheckbox_(form, title, subjects) {
          form
            .addCheckboxItem()
            .setTitle(title)
            .setChoiceValues(subjects)
            .setRequired(false);
        }

        function addSubjectRequestSection_(form, subjects, automaticSchoolLevel = "") {
          for (let index = 1; index <= 4; index += 1) {
            const required = index === 1;
            if (!automaticSchoolLevel) {
              form
                .addListItem()
                .setTitle(`学校区分（${index}教科目）`)
                .setChoiceValues(QUESTIONNAIRE_CONFIG.schoolLevels)
                .setRequired(required);
            }
            const schoolLabel = automaticSchoolLevel ? `${automaticSchoolLevel}・` : "";
            form
              .addListItem()
              .setTitle(`受講教科（${schoolLabel}${index}教科目）${required ? "（必須）" : ""}`)
              .setChoiceValues(subjects)
              .setRequired(required);
            form
              .addListItem()
              .setTitle(`受講回数（${schoolLabel}${index}教科目）${required ? "（必須）" : ""}`)
              .setChoiceValues(QUESTIONNAIRE_CONFIG.sessionCounts)
              .setRequired(required);
          }
        }

        const STUDENT_COMPACT_SHEET_NAME = "Form Responses 1";
        const STUDENT_RAW_SHEET_NAME = "回答原本（システム用）";

        /**
         * フォーム固有の分岐列を非表示の原本へ残し、利用者向け回答表を44列へ統一する。
         * Googleフォームは分岐ごとに別列を作るため、原本の列を削除せず転記先を用意する。
         */
        function prepareStudentResponseSheets_(spreadsheet) {
          const rawSheet = findStudentRawResponseSheet_(spreadsheet);
          const compactSheet = spreadsheet
            .getSheets()
            .find((sheet) => sheet.getSheetId() !== rawSheet.getSheetId());
          if (!compactSheet) {
            throw new Error("回答を整形するためのシートを準備できませんでした。");
          }

          rawSheet.setName(STUDENT_RAW_SHEET_NAME);
          compactSheet.clear();
          compactSheet.setName(STUDENT_COMPACT_SHEET_NAME);
          const headers = studentCompactResponseHeaders_();
          compactSheet.getRange(1, 1, 1, headers.length).setValues([headers]);
          styleStudentCompactSheet_(compactSheet, headers.length);
          spreadsheet.setActiveSheet(compactSheet);
          spreadsheet.moveActiveSheet(1);
          rawSheet.hideSheet();
        }

        function findStudentRawResponseSheet_(spreadsheet) {
          for (let attempt = 0; attempt < 10; attempt += 1) {
            SpreadsheetApp.flush();
            const rawSheet = spreadsheet.getSheets().find((sheet) => {
              const lastColumn = sheet.getLastColumn();
              if (lastColumn < 1) return false;
              const headers = sheet.getRange(1, 1, 1, lastColumn).getDisplayValues()[0];
              return headers.includes("学年（必須）") && headers.includes("在籍区分（必須）");
            });
            if (rawSheet) return rawSheet;
            Utilities.sleep(500);
          }
          throw new Error("Googleフォームの回答シートを確認できませんでした。もう一度実行してください。");
        }

        function studentCompactResponseHeaders_() {
          const headers = [
            "Timestamp",
            "Email Address",
            "個人情報の利用目的への同意（必須）",
            "姓（苗字）（必須）",
            "名（必須）",
            "学年（必須）",
            "在籍区分（必須）",
            "中高一貫などで他学年の授業を受講される際はこちらにチェックを入れてください",
          ];
          for (let index = 1; index <= 4; index += 1) {
            headers.push(`学校区分（${index}教科目）`);
            headers.push(`受講教科（${index}教科目）${index === 1 ? "（必須）" : ""}`);
            headers.push(`受講回数（${index}教科目）${index === 1 ? "（必須）" : ""}`);
          }
          headers.push("受講不可日時の確認（必須）", "特記事項", "夏期講習学力テスト");
          QUESTIONNAIRE_CONFIG.openDates.forEach((isoDate) => {
            headers.push(
              `受講不可日時（チェックしたコマは受講不可） [${formatDateLabel_(isoDate)}]`,
            );
          });
          return headers;
        }

        function styleStudentCompactSheet_(sheet, columnCount) {
          const header = sheet.getRange(1, 1, 1, columnCount);
          header
            .setBackground("#5B3F86")
            .setFontColor("#FFFFFF")
            .setFontFamily("Arial")
            .setFontSize(10)
            .setFontWeight("normal")
            .setHorizontalAlignment("left")
            .setVerticalAlignment("middle")
            .setWrap(false);
          sheet.setFrozenRows(1);
          sheet.setRowHeight(1, 23);
          const body = sheet.getRange(2, 1, sheet.getMaxRows() - 1, columnCount);
          body
            .setFontColor("#202124")
            .setFontFamily("Arial")
            .setFontSize(10)
            .setFontWeight("normal")
            .setVerticalAlignment("middle")
            .setWrap(false);
          const stripeRule = SpreadsheetApp.newConditionalFormatRule()
            .whenFormulaSatisfied("=AND(COUNTA(2:2)>0,ISODD(ROW()))")
            .setBackground("#F8F9FA")
            .setRanges([body])
            .build();
          sheet.setConditionalFormatRules([stripeRule]);

          const widths = [135, 180, 220, 110, 110, 90, 110, 240];
          for (let index = 1; index <= 4; index += 1) {
            widths.push(120, 160, 130);
          }
          widths.push(180, 240, 180);
          QUESTIONNAIRE_CONFIG.openDates.forEach(() => widths.push(230));
          widths.forEach((width, index) => sheet.setColumnWidth(index + 1, width));
        }

        function styleStudentCompactRow_(sheet, row, columnCount) {
          sheet
            .getRange(row, 1, 1, columnCount)
            .setBackground(row % 2 === 0 ? "#FFFFFF" : "#F8F9FA")
            .setFontColor("#202124")
            .setFontFamily("Arial")
            .setFontSize(10)
            .setFontWeight("normal")
            .setVerticalAlignment("middle")
            .setWrap(false);
          sheet.setRowHeight(row, 21);
        }

        /** フォーム回答を、アプリが読み込む共通の学校区分・科目・回数列へ転記する。 */
        function installStudentResponseNormalizer_(spreadsheetId) {
          ScriptApp.getProjectTriggers()
            .filter((trigger) =>
              ["fillAutomaticStudentSchoolLevels_", "writeCompactStudentResponse_"].includes(
                trigger.getHandlerFunction(),
              ),
            )
            .forEach((trigger) => ScriptApp.deleteTrigger(trigger));
          ScriptApp.newTrigger("writeCompactStudentResponse_")
            .forSpreadsheet(spreadsheetId)
            .onFormSubmit()
            .create();
        }

        function writeCompactStudentResponse_(event) {
          const rawSheet = event.range.getSheet();
          if (rawSheet.getName() !== STUDENT_RAW_SHEET_NAME) return;
          const compactSheet = event.source.getSheetByName(STUDENT_COMPACT_SHEET_NAME);
          if (!compactSheet) throw new Error("整形済み回答シートが見つかりません。");

          const headers = rawSheet
            .getRange(1, 1, 1, rawSheet.getLastColumn())
            .getDisplayValues()[0];
          const values = event.range.getValues()[0];
          const otherGradeTitle =
            "中高一貫などで他学年の授業を受講される際はこちらにチェックを入れてください";
          const otherGradeAnswer = responseValue_(headers, values, [otherGradeTitle]);
          const grade = String(responseValue_(headers, values, ["学年（必須）"])).trim();
          const automaticSchoolLevel = schoolLevelForStudentGrade_(grade);
          const compactValues = [
            responseValue_(headers, values, ["Timestamp", "タイムスタンプ"]),
            responseValue_(headers, values, ["Email Address", "メールアドレス"]),
            responseValue_(headers, values, ["個人情報の利用目的への同意（必須）"]),
            responseValue_(headers, values, ["姓（苗字）（必須）"]),
            responseValue_(headers, values, ["名（必須）"]),
            grade,
            responseValue_(headers, values, ["在籍区分（必須）"]),
            otherGradeAnswer,
          ];

          for (let index = 1; index <= 4; index += 1) {
            const prefix = otherGradeAnswer ? "" : `${automaticSchoolLevel}・`;
            const subject = responseValueStartingWith_(
              headers,
              values,
              `受講教科（${prefix}${index}教科目）`,
            );
            const count = responseValueStartingWith_(
              headers,
              values,
              `受講回数（${prefix}${index}教科目）`,
            );
            const schoolLevel = otherGradeAnswer
              ? responseValue_(headers, values, [`学校区分（${index}教科目）`])
              : subject
                ? automaticSchoolLevel
                : "";
            compactValues.push(schoolLevel, subject, count);
          }

          compactValues.push(
            responseValue_(headers, values, ["受講不可日時の確認（必須）"]),
            responseValue_(headers, values, ["特記事項"]),
            responseValue_(headers, values, ["夏期講習学力テスト"]),
          );
          QUESTIONNAIRE_CONFIG.openDates.forEach((isoDate) => {
            compactValues.push(
              responseValue_(headers, values, [
                `受講不可日時（チェックしたコマは受講不可） [${formatDateLabel_(isoDate)}]`,
              ]),
            );
          });
          appendCompactStudentResponse_(compactSheet, compactValues);
        }

        function appendCompactStudentResponse_(compactSheet, compactValues) {
          const lock = LockService.getScriptLock();
          lock.waitLock(30000);
          try {
            const targetRow = Math.max(compactSheet.getLastRow() + 1, 2);
            compactSheet
              .getRange(targetRow, 1, 1, compactValues.length)
              .setValues([compactValues]);
            styleStudentCompactRow_(compactSheet, targetRow, compactValues.length);
          } finally {
            lock.releaseLock();
          }
        }

        function schoolLevelForStudentGrade_(grade) {
          if (/^小[1-6]$/.test(grade)) return "小学校";
          if (/^中[1-3]$/.test(grade)) return "中学校";
          if (/^高[1-3]$/.test(grade)) return "高校";
          return "";
        }

        function responseValue_(headers, values, candidates) {
          for (const candidate of candidates) {
            const column = headers.indexOf(candidate);
            if (column >= 0) return values[column];
          }
          return "";
        }

        function responseValueStartingWith_(headers, values, prefix) {
          const column = headers.findIndex((header) => header.startsWith(prefix));
          return column >= 0 ? values[column] : "";
        }

        function addAvailabilityPage_(form, actionLabel) {
          return form
            .addPageBreakItem()
            .setTitle(`${actionLabel}できない日時`)
            .setHelpText(
              `チェックした日時は「${actionLabel}不可」として扱います。` +
                `${actionLabel}できる日時にはチェックを入れないでください。`,
            );
        }

        function addAvailabilityGrid_(form, title) {
          form
            .addCheckboxGridItem()
            .setTitle(title)
            .setRows(QUESTIONNAIRE_CONFIG.openDates.map(formatDateLabel_))
            .setColumns(QUESTIONNAIRE_CONFIG.timeSlots)
            .setRequired(false);
        }

        /** 作成済みフォームのURLをもう一度表示する。 */
        function __SHOW_FUNCTION__() {
          const properties = PropertiesService.getScriptProperties();
          const formId = properties.getProperty(FORM_ID_PROPERTY);
          if (!formId) throw new Error("フォームはまだ作成されていません。");
          logQuestionnaireUrls_(
            FormApp.openById(formId),
            properties.getProperty(SPREADSHEET_ID_PROPERTY),
          );
        }

        /** 既存フォームを残し、現在のコードで置換用フォームを新規作成する。 */
        function __REPLACEMENT_FUNCTION__() {
          const properties = PropertiesService.getScriptProperties();
          properties.deleteProperty(FORM_ID_PROPERTY);
          properties.deleteProperty(SPREADSHEET_ID_PROPERTY);
          __CREATE_FUNCTION__();
        }

        function formatDateLabel_(isoDate) {
          const parts = isoDate.split("-").map(Number);
          const date = new Date(parts[0], parts[1] - 1, parts[2]);
          const weekdays = ["日", "月", "火", "水", "木", "金", "土"];
          return `${isoDate}（${weekdays[date.getDay()]}）`;
        }

        function validateQuestionnaireConfig_() {
          if (QUESTIONNAIRE_CONFIG.kind !== "teacher_subject") {
            const dates = QUESTIONNAIRE_CONFIG.openDates;
            if (dates.length === 0) throw new Error("開校日を1日以上設定してください。");
            if (new Set(dates).size !== dates.length) throw new Error("開校日が重複しています。");
            dates.forEach((value) => {
              if (!/^\d{4}-\d{2}-\d{2}$/.test(value)) {
                throw new Error(`開校日はYYYY-MM-DD形式にしてください: ${value}`);
              }
            });
            if (QUESTIONNAIRE_CONFIG.timeSlots.length === 0) {
              throw new Error("時間帯を1件以上設定してください。");
            }
          }
          if (
            QUESTIONNAIRE_CONFIG.kind === "student" ||
            QUESTIONNAIRE_CONFIG.kind === "teacher_subject"
          ) {
            const subjectGroups = Object.values(QUESTIONNAIRE_CONFIG.subjectsBySchoolLevel);
            const invalidGroup = subjectGroups.some(
              (subjects) => subjects.length === 0 || new Set(subjects).size !== subjects.length,
            );
            const studentSubjects = QUESTIONNAIRE_CONFIG.studentSubjectChoices || [];
            const invalidStudentSubjects = QUESTIONNAIRE_CONFIG.kind === "student" &&
              (studentSubjects.length === 0 ||
               new Set(studentSubjects).size !== studentSubjects.length ||
               QUESTIONNAIRE_CONFIG.schoolLevels.join("・") !== "小学校・中学校・高校");
            if (invalidGroup || invalidStudentSubjects) {
              throw new Error("科目選択肢が未設定または重複しています。");
            }
          }
        }

        function logQuestionnaireUrls_(form, spreadsheetId) {
          console.log(`フォーム編集URL: ${form.getEditUrl()}`);
          console.log(`回答用URL: ${form.getPublishedUrl()}`);
          if (spreadsheetId) {
            console.log(`回答原本URL: https://docs.google.com/spreadsheets/d/${spreadsheetId}/edit`);
          }
        }
        """;
}
