using System.Drawing;
using System.Windows.Forms;

namespace ScreenTranslator.UI;

/// <summary>
/// 弹出式的「上下文摘要」悬浮框：无边框、置顶，显示在触发控件附近，
/// 失去焦点时自动隐藏（点击其它地方即关闭）。
/// </summary>
public sealed class SummaryPopupForm : Form
{
    private readonly TextBox _textBox;
    private string _shownText = "";

    /// <summary>文本被编辑并在失焦/关闭时提交的回调（参数为新文本）。</summary>
    public Action<string>? OnTextCommitted;

    public SummaryPopupForm()
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        TopMost = true;
        KeyPreview = true;
        ClientSize = new Size(480, 200);

        _textBox = new TextBox
        {
            Multiline = true,
            ScrollBars = ScrollBars.Vertical,
            Dock = DockStyle.Fill,
            BackColor = SystemColors.Info,
            BorderStyle = BorderStyle.FixedSingle,
            Font = new Font("Microsoft YaHei UI", 10F),
        };
        Controls.Add(_textBox);
    }

    /// <summary>更新内容并显示在 <paramref name="anchor"/> 正下方。</summary>
    public void ShowNear(Control anchor, string text)
    {
        _shownText = text;
        _textBox.Text = text;
        var p = anchor.PointToScreen(new Point(0, anchor.Height + 2));

        // 防止超出屏幕右/下边缘
        var wa = Screen.FromPoint(p).WorkingArea;
        if (p.X + Width > wa.Right) p.X = wa.Right - Width;
        if (p.Y + Height > wa.Bottom) p.Y = wa.Bottom - Height;

        Location = p;
        Show();
        Activate();
    }

    /// <summary>若正在显示，刷新内容。</summary>
    public void RefreshText(string text)
    {
        if (Visible)
        {
            _shownText = text;
            _textBox.Text = text;
        }
    }

    protected override void OnDeactivate(EventArgs e)
    {
        base.OnDeactivate(e);
        Hide();
        CommitIfChanged();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Escape)
        {
            Hide();
            CommitIfChanged();
        }
        base.OnKeyDown(e);
    }

    private void CommitIfChanged()
    {
        if (!string.Equals(_textBox.Text, _shownText, StringComparison.Ordinal))
            OnTextCommitted?.Invoke(_textBox.Text);
    }
}
