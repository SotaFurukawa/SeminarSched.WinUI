using System.Text.RegularExpressions;
using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Graphics;
using Windows.System;
using Windows.UI;

namespace SeminarSched_WinUI;

// Python版GoogleFormsGuideDialog.qmlの内容（10手順）をそのまま移植した
// 「Googleフォーム作成手順」ポップアップ。手順1の画像2枚はPython版のUIではなく、この
// WinUI版②アンケート作成画面自体のスクリーンショットへ差し替えた（ユーザー指摘「作成手順の画像が
// 以前のpython版を用いたものになっています」checkpoint109）。保存後の画面に写り込む保存先パスは
// 実在するWindowsユーザー名を含むため、Python版と同様に黒塗り処理をしてから同梱している。
// 手順2以降の画像は、Windows/Google側の汎用UI画面（Explorer・Apps Script等）のみで
// 個人情報を含まないため、そのまま流用している。
internal static class GoogleFormsGuide
{
    private static readonly Regex UrlPattern = new(@"https?://\S+", RegexOptions.Compiled);

    private sealed record GuideImage(string FileName, string AccessibleName, double Height);
    private sealed record GuideStep(int Number, string Title, string Description, IReadOnlyList<GuideImage> Images, string Note);

    private static readonly IReadOnlyList<GuideStep> Steps =
    [
        new(1, "アプリで作成キットを保存",
            "生徒用・講師用のフォーム名、回答締切、問い合わせ先を確認し、「作成キットを保存」を押します。保存が完了すると「保存先を開く」ボタンが表示されます。",
            [new("01_save_kit_a.png", "2 アンケート作成画面（保存前）", 340), new("01_save_kit_b.png", "2 アンケート作成画面（保存後、保存先パスは伏せています）", 340)],
            "開校日と有効コマが未設定の場合は保存できません。先に1 プロジェクト設定で授業日とコマを確定してください。"),
        new(2, "保存先を開く",
            "「保存先を開く」を押すと、作成された2つの.gsと手順書が入ったフォルダーを開けます。",
            [new("02_open_saved_folder.png", "保存先フォルダーの中身", 220)],
            "生徒用は create_student_questionnaire.gs、講師勤務日時用は create_teacher_questionnaire.gs です。"),
        new(3, "create_student_questionnaire.gsをメモ帳で開く",
            "create_student_questionnaire.gsを右クリックし、「プログラムから開く」から「メモ帳」を選びます。メモ帳に表示された内容を先頭から最後まで選択してコピーします。",
            [new("03_open_with_menu.png", "右クリックメニューのプログラムから開く", 340), new("03_choose_notepad.png", "メモ帳を選択する画面", 340)],
            "ファイル名ではなく、メモ帳に表示されたコードの全内容をコピーします。"),
        new(4, "Apps Scriptで新しいプロジェクトを作る",
            "https://script.google.com/home を開き、「新しいプロジェクト」を押します。Code.gsに最初から入っている function myFunction() のコードをすべて削除します。",
            [new("04_apps_script_home.png", "Apps Scriptのホーム画面", 330), new("04_blank_code_gs.png", "新規プロジェクトのCode.gs", 330)],
            "生徒用・講師勤務日時用は、それぞれ別のApps Scriptプロジェクトで作成します。"),
        new(5, "メモ帳の内容をコピー＆ペースト",
            "空にしたCode.gsへ、メモ帳からコピーした.gsの全内容を貼り付けます。日付・コマ・フォーム名などは、アプリで設定した内容がコード内へ反映されています。",
            [new("05_paste_script.png", "Code.gsへ貼り付けた状態", 360)],
            "貼り付けた後に先頭や末尾が欠けていないことを確認してください。"),
        new(6, "保存して作成関数を実行",
            "Ctrl＋Sまたはフロッピーディスクのボタンで保存します。関数が createStudentQuestionnaire になっていることを確認し、「実行」を押します。",
            [new("06_select_function.png", "実行する関数の選択画面", 360)],
            "講師勤務日時用は createTeacherQuestionnaire を選びます。Google Apps Scriptの「デプロイ」は不要です。"),
        new(7, "権限を確認",
            "初回実行時に「承認が必要です」と表示されたら、「権限を確認」を押して使用するGoogleアカウントを選択します。",
            [new("07_confirm_permissions.png", "権限の確認ダイアログ", 260)],
            "自分でアプリから保存したコードを貼り付けたことを確認してから進んでください。第三者から受け取った不明なコードは実行しません。"),
        new(8, "詳細を表示し、安全ではないページへ移動",
            "「このアプリはGoogleで確認されていません」と表示された場合は「詳細」を押し、続いて「無題のプロジェクト（安全ではないページ）に移動」を押します。",
            [new("08_google_warning.png", "Googleの未確認アプリ警告", 440), new("08_continue_unsafe.png", "安全ではないページへ移動するリンク", 440)],
            "これは自分のGoogleアカウント内で作成した未公開スクリプトに対する警告です。コードの出所を確認できない場合は進まないでください。"),
        new(9, "すべて選択して続行",
            "アクセス権限の画面で「すべて選択」にチェックを入れ、フォームとスプレッドシートの権限内容を確認して「続行」を押します。",
            [new("09_select_all_continue.png", "アクセス権限のすべて選択画面", 430)],
            "フォームと回答先スプレッドシートを自分のGoogleドライブへ作成するために必要な権限です。"),
        new(10, "実行ログのリンクからアンケートを開く",
            "実行が完了すると、実行ログにフォーム編集URL・回答URL・回答原本URLが表示されます。回答URLを開けばアンケートへ回答でき、生徒や講師へ案内できます。",
            [new("10_result_links.png", "実行ログに表示されるリンク一覧", 480)],
            "配布前に回答URLを自分で開いてテストしてください。フォーム編集URLと回答原本URLは担当者だけで管理します。"),
    ];

    public static async Task ShowAsync(XamlRoot xamlRoot)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = xamlRoot,
            Title = "Googleフォーム作成手順",
            Content = new Border { Width = 1120, Height = 660, Child = BuildWizard() },
            PrimaryButtonText = "別ウィンドウで表示",
            CloseButtonText = "閉じる",
        };
        // 既定のContentDialogは幅548px相当に固定されており、画像を含む本文には狭すぎるため、
        // このダイアログのResourcesだけを上書きして拡張する（WinUI3の既知の回避策）。
        dialog.Resources["ContentDialogMaxWidth"] = 1180d;
        dialog.Resources["ContentDialogMinWidth"] = 1180d;
        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary) OpenWindow();
    }

    // Python版と同様、モーダルのまま手順書を見ながらメモ帳やApps Scriptを操作すると
    // 他ウィンドウに切り替えられず不便なため、独立した通常ウィンドウでも開けるようにする。
    private static void OpenWindow()
    {
        var window = new Window
        {
            Title = "Googleフォーム作成手順",
            Content = new Border { Padding = new Thickness(16), Child = BuildWizard() },
        };
        window.AppWindow.SetIcon("Assets/AppIcon.ico");
        window.AppWindow.Resize(new SizeInt32(1180, 820));
        if (window.AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.PreferredMinimumWidth = 900;
            presenter.PreferredMinimumHeight = 620;
        }
        window.Activate();
    }

    private sealed record RailItem(Button Button, Border Badge, TextBlock BadgeText, TextBlock TitleText);

    // Python版は10枚のカードを縦一列に並べただけの単純なスクロールだった。ここでは左に手順一覧の
    // レール、右に選択中の手順の詳細（説明・スクリーンショット・補足）を表示するウィザード形式にし、
    // 上部の進捗バーと下部の「前へ／次へ」で現在地が一目でわかるようにしている。
    private static FrameworkElement BuildWizard()
    {
        var currentIndex = 0;
        var railItems = new List<RailItem>();
        var detailHost = new Border();
        var progressBar = new ProgressBar { Minimum = 1, Maximum = Steps.Count, Value = 1, Width = 220, VerticalAlignment = VerticalAlignment.Center };
        var progressText = new TextBlock { FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"] };
        var stepTitleText = new TextBlock { FontSize = 20, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap };
        var prevButton = new Button { Content = "← 前の手順" };
        var nextButton = new Button { Content = "次の手順 →", Style = (Style)Application.Current.Resources["AccentButtonStyle"] };

        void Refresh()
        {
            var step = Steps[currentIndex];
            stepTitleText.Text = $"手順 {step.Number}：{step.Title}";
            progressBar.Value = step.Number;
            progressText.Text = $"{step.Number} / {Steps.Count}";
            detailHost.Child = BuildStepDetail(step);
            prevButton.IsEnabled = currentIndex > 0;
            nextButton.IsEnabled = currentIndex < Steps.Count - 1;
            for (var i = 0; i < railItems.Count; i++) StyleRailItem(railItems[i], i == currentIndex);
        }

        var rail = new StackPanel { Spacing = 2 };
        for (var i = 0; i < Steps.Count; i++)
        {
            var index = i;
            var item = BuildRailItem(Steps[i]);
            item.Button.Click += (_, _) => { currentIndex = index; Refresh(); };
            railItems.Add(item);
            rail.Children.Add(item.Button);
        }
        prevButton.Click += (_, _) => { if (currentIndex > 0) { currentIndex--; Refresh(); } };
        nextButton.Click += (_, _) => { if (currentIndex < Steps.Count - 1) { currentIndex++; Refresh(); } };

        var railScroll = new ScrollViewer { Content = rail, Width = 230, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        var detailScroll = new ScrollViewer { Content = detailHost, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(4, 0, 16, 0) };

        var body = new Grid { ColumnSpacing = 20 };
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetColumn(railScroll, 0);
        Grid.SetColumn(detailScroll, 1);
        body.Children.Add(railScroll);
        body.Children.Add(detailScroll);

        var header = new StackPanel { Spacing = 8 };
        header.Children.Add(new TextBlock
        {
            Text = "左の一覧から手順を選ぶか、下の「次の手順」で順番に進めてください。Google側の画面は更新により多少異なる場合があります。",
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
        });
        var progressRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        progressRow.Children.Add(progressBar);
        progressRow.Children.Add(progressText);
        header.Children.Add(progressRow);
        header.Children.Add(stepTitleText);

        var footer = new Grid { ColumnSpacing = 16 };
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var privacyNote = new TextBlock
        {
            Text = "回答原本には氏名・学年・希望日時などの個人情報が含まれます。一般公開せず、担当者だけがアクセスできる場所で管理してください。",
            FontSize = 11,
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = (Brush)Application.Current.Resources["SystemFillColorCautionBrush"],
        };
        Grid.SetColumn(privacyNote, 0);
        var navButtons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        navButtons.Children.Add(prevButton);
        navButtons.Children.Add(nextButton);
        Grid.SetColumn(navButtons, 1);
        footer.Children.Add(privacyNote);
        footer.Children.Add(navButtons);

        var root = new Grid { RowSpacing = 16 };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Grid.SetRow(header, 0);
        Grid.SetRow(body, 1);
        Grid.SetRow(footer, 2);
        root.Children.Add(header);
        root.Children.Add(body);
        root.Children.Add(footer);

        Refresh();
        return root;
    }

    private static RailItem BuildRailItem(GuideStep step)
    {
        var badgeText = new TextBlock { Text = step.Number.ToString(), FontSize = 12, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        var badge = new Border { Width = 26, Height = 26, CornerRadius = new CornerRadius(13), BorderThickness = new Thickness(1), Child = badgeText };
        var title = new TextBlock { Text = step.Title, FontSize = 13, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
        var content = new Grid { ColumnSpacing = 10 };
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetColumn(badge, 0);
        Grid.SetColumn(title, 1);
        content.Children.Add(badge);
        content.Children.Add(title);

        var button = new Button
        {
            Content = content,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Left,
            Padding = new Thickness(10, 8, 10, 8),
            BorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(6),
        };
        AutomationProperties.SetName(button, $"手順{step.Number}：{step.Title}");
        return new RailItem(button, badge, badgeText, title);
    }

    private static void StyleRailItem(RailItem item, bool selected)
    {
        var accent = (Brush)Application.Current.Resources["AccentFillColorDefaultBrush"];
        var subtle = (Brush)Application.Current.Resources["SubtleFillColorSecondaryBrush"];
        var cardStroke = (Brush)Application.Current.Resources["CardStrokeColorDefaultBrush"];
        var textPrimary = (Brush)Application.Current.Resources["TextFillColorPrimaryBrush"];
        item.Button.Background = selected ? subtle : new SolidColorBrush(Colors.Transparent);
        item.Badge.Background = selected ? accent : new SolidColorBrush(Colors.Transparent);
        item.Badge.BorderBrush = selected ? accent : cardStroke;
        item.BadgeText.Foreground = selected ? new SolidColorBrush(Colors.White) : textPrimary;
        item.TitleText.FontWeight = selected ? FontWeights.SemiBold : FontWeights.Normal;
    }

    private static FrameworkElement BuildStepDetail(GuideStep step)
    {
        var content = new StackPanel { Spacing = 14 };

        var description = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 14 };
        AppendDescription(description, step.Description);
        content.Children.Add(description);

        if (step.Images.Count > 0)
        {
            var imageGrid = new Grid { ColumnSpacing = 12 };
            foreach (var _ in step.Images) imageGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            for (var i = 0; i < step.Images.Count; i++)
            {
                var image = step.Images[i];
                var frame = new Border
                {
                    BorderBrush = (Brush)Application.Current.Resources["CardStrokeColorDefaultBrush"],
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(4),
                    Padding = new Thickness(4),
                    Child = new Image
                    {
                        Source = new BitmapImage(new Uri($"ms-appx:///Assets/GoogleFormsGuide/{image.FileName}")),
                        Height = image.Height,
                        Stretch = Stretch.Uniform,
                    },
                };
                AutomationProperties.SetName(frame, image.AccessibleName);
                Grid.SetColumn(frame, i);
                imageGrid.Children.Add(frame);
            }
            content.Children.Add(imageGrid);
        }

        content.Children.Add(new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(40, 255, 185, 0)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(120, 255, 185, 0)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(10),
            Child = new TextBlock { Text = $"補足：{step.Note}", TextWrapping = TextWrapping.Wrap, FontSize = 12 },
        });

        return content;
    }

    // 手順4の説明文に含まれるURL（https://script.google.com/home）だけをHyperlinkにする。
    // Python版のQt.openUrlExternally相当としてWindows.System.Launcherで既定ブラウザーを開く。
    private static void AppendDescription(TextBlock block, string text)
    {
        var lastIndex = 0;
        foreach (Match match in UrlPattern.Matches(text))
        {
            if (match.Index > lastIndex) block.Inlines.Add(new Run { Text = text[lastIndex..match.Index] });
            var url = match.Value;
            var hyperlink = new Hyperlink();
            hyperlink.Inlines.Add(new Run { Text = url });
            hyperlink.Click += async (_, _) => await Launcher.LaunchUriAsync(new Uri(url));
            block.Inlines.Add(hyperlink);
            lastIndex = match.Index + match.Length;
        }
        if (lastIndex < text.Length) block.Inlines.Add(new Run { Text = text[lastIndex..] });
    }
}
