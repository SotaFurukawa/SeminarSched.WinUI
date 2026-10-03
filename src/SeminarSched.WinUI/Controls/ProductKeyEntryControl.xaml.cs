using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.ApplicationModel.DataTransfer;
using Windows.System;

namespace SeminarSched_WinUI.Controls;

/// <summary>
/// ユーザー要望「プロダクトキー入力はハイフンは自分で入れさせず、12枠用意しておき、そこに入力
/// させる。1枠目が入力されたら自動的に2枠目に移動し、逆にバックスペースが押された場合には、
/// ひとつ前の枠を削除するようにする。githubの二段階認証で用いられているような方式」への対応。
/// 4桁×3グループ（ハイフン区切り表示のみ、入力はさせない）の1文字ずつのTextBoxを並べ、自動の
/// フォーカス前進・バックスペースでの前枠削除＋フォーカス後退・貼り付け時の全枠への分配を行う。
/// </summary>
public sealed partial class ProductKeyEntryControl : UserControl
{
    private const int GroupSize = 4;
    private const int GroupCount = 3;

    private readonly List<TextBox> _boxes = new(GroupSize * GroupCount);

    public ProductKeyEntryControl()
    {
        InitializeComponent();

        for (var group = 0; group < GroupCount; group++)
        {
            if (group > 0)
            {
                RootPanel.Children.Add(new TextBlock
                {
                    Text = "-",
                    FontSize = 20,
                    VerticalAlignment = VerticalAlignment.Center,
                });
            }

            for (var i = 0; i < GroupSize; i++)
            {
                var box = new TextBox
                {
                    Width = 36,
                    Height = 44,
                    MaxLength = 1,
                    FontSize = 20,
                    FontWeight = FontWeights.SemiBold,
                    TextAlignment = TextAlignment.Center,
                    CharacterCasing = CharacterCasing.Upper,
                };
                box.BeforeTextChanging += Box_BeforeTextChanging;
                box.TextChanged += Box_TextChanged;
                box.KeyDown += Box_KeyDown;
                box.GotFocus += Box_GotFocus;
                box.Paste += Box_Paste;
                _boxes.Add(box);
                RootPanel.Children.Add(box);
            }
        }
    }

    /// <summary>12枠を連結した入力値（ハイフン無し）。<see cref="SeminarSched.Domain.Licensing.ProductKeyService"/>
    /// は非16進文字を無視して解釈するため、ハイフン無しのままValidateへ渡して問題ない。</summary>
    public string Value => string.Concat(_boxes.Select(b => b.Text));

    public void FocusFirst() => _boxes[0].Focus(FocusState.Programmatic);

    public void ResetAndFocus()
    {
        foreach (var box in _boxes)
        {
            box.Text = string.Empty;
        }

        FocusFirst();
    }

    private static void Box_BeforeTextChanging(TextBox sender, TextBoxBeforeTextChangingEventArgs args)
        => args.Cancel = args.NewText.Length > 0 && !args.NewText.All(Uri.IsHexDigit);

    private void Box_TextChanged(object sender, TextChangedEventArgs e)
    {
        var box = (TextBox)sender;
        if (box.Text.Length != 1)
        {
            return;
        }

        var index = _boxes.IndexOf(box);
        if (index < _boxes.Count - 1)
        {
            _boxes[index + 1].Focus(FocusState.Programmatic);
        }
    }

    // バックスペース時、自枠が既に空ならひとつ前の枠へ移動してその内容を消す（GitHubの二段階認証
    // コード入力と同じ、連続バックスペースで後ろから1文字ずつ消えていく挙動）。自枠に文字が残って
    // いる場合は既定の削除動作に任せる（次のバックスペースでこのハンドラーに入る）。
    private void Box_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.Back)
        {
            return;
        }

        var box = (TextBox)sender;
        if (box.Text.Length > 0)
        {
            return;
        }

        var index = _boxes.IndexOf(box);
        if (index <= 0)
        {
            return;
        }

        _boxes[index - 1].Text = string.Empty;
        _boxes[index - 1].Focus(FocusState.Programmatic);
        e.Handled = true;
    }

    // フォーカスを得た枠の既存の1文字を選択状態にしておき、そのまま入力すれば上書きできるようにする
    // （戻って1文字だけ直したい場合に、まず削除してから入力し直す手間を無くす）。
    private static void Box_GotFocus(object sender, RoutedEventArgs e) => ((TextBox)sender).SelectAll();

    // キー全体をコピー＆ペーストする使い方（プロダクトキーはメール等でまとめて配布されるため）にも
    // 対応する。貼り付けた文字列から16進文字だけを取り出し、貼り付け開始位置以降の枠へ分配する。
    private async void Box_Paste(object sender, TextControlPasteEventArgs e)
    {
        e.Handled = true;
        var dataPackageView = Clipboard.GetContent();
        if (!dataPackageView.Contains(StandardDataFormats.Text))
        {
            return;
        }

        var text = await dataPackageView.GetTextAsync();
        var hex = new string(text.Where(Uri.IsHexDigit).ToArray()).ToUpperInvariant();
        if (hex.Length == 0)
        {
            return;
        }

        var startIndex = _boxes.IndexOf((TextBox)sender);
        for (var i = 0; i < hex.Length && startIndex + i < _boxes.Count; i++)
        {
            _boxes[startIndex + i].Text = hex[i].ToString();
        }

        var nextIndex = Math.Min(startIndex + hex.Length, _boxes.Count - 1);
        _boxes[nextIndex].Focus(FocusState.Programmatic);
    }
}
