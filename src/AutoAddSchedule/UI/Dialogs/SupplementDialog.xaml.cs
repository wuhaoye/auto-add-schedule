using System.Windows;
using System.Windows.Input;

namespace AutoAddSchedule.UI.Dialogs;

/// <summary>
/// 截图工具条的「补充信息」对话框，输入内容会随截图一起发给大模型。
/// 为支持多行输入，普通 Enter 用于换行，Ctrl+Enter 或「确定」按钮才提交。
/// </summary>
public partial class SupplementDialog : Window
{
    public SupplementDialog()
        : this(null)
    {
    }

    public SupplementDialog(string? initialText)
    {
        InitializeComponent();
        InputBox.Text = initialText ?? string.Empty;
        Loaded += OnLoaded;
        PreviewKeyDown += OnPreviewKeyDown;
    }

    /// <summary>用户填写的补充信息（已去除首尾空白）。</summary>
    public string SupplementText => InputBox.Text.Trim();

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // 自动聚焦输入框，光标放到已有内容末尾，方便多次修改后继续编辑。
        InputBox.Focus();
        InputBox.CaretIndex = InputBox.Text.Length;
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            CloseWith(false);
            return;
        }

        if (e.Key == Key.Return && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
        {
            e.Handled = true;
            CloseWith(true);
        }
    }

    private void Ok_Click(object sender, RoutedEventArgs e) => CloseWith(true);

    private void Cancel_Click(object sender, RoutedEventArgs e) => CloseWith(false);

    private void CloseWith(bool accepted)
    {
        try
        {
            DialogResult = accepted;
        }
        catch (InvalidOperationException)
        {
            // 非模态方式打开时没有 DialogResult，直接关闭即可。
            Close();
        }
    }
}
