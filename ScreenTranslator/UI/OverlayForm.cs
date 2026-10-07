using System.ComponentModel;
using System.Drawing;
using System.Text;
using System.Windows.Forms;

namespace ScreenTranslator.UI;

/// <summary>无边框、置顶、鼠标穿透的文字叠加层，透明背景上仅绘制翻译文本（带阴影便于阅读）。</summary>
public sealed class OverlayForm : Form
{
    private const int WS_EX_TOPMOST = 0x00000008;
    private const int WS_EX_TRANSPARENT = 0x00000020;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const int WS_EX_LAYERED = 0x00080000;
    private const int WS_EX_NOACTIVATE = 0x08000000;

    private const TextFormatFlags LineFlags =
        TextFormatFlags.NoPadding | TextFormatFlags.SingleLine | TextFormatFlags.Left;

    private static readonly Color KeyColor = Color.Magenta;

    private string _text = "";
    private Font _font;
    private int _maxWidth;
    private float _lineSpacing = 1.0f;

    private readonly List<string> _lines = new();
    private int _lineHeight;
    private int _advance;

    public OverlayForm(Font font)
    {
        _font = font;
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        TopMost = true;
        BackColor = KeyColor;
        TransparencyKey = KeyColor;
    }

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= WS_EX_TOPMOST | WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW | WS_EX_LAYERED | WS_EX_NOACTIVATE;
            return cp;
        }
    }

    /// <summary>行距倍率：1.0 为字体默认行距，越大行间越稀疏。</summary>
    [DefaultValue(1.0f)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public float LineSpacing
    {
        get => _lineSpacing;
        set
        {
            _lineSpacing = value;
            RebuildLayout();
            Invalidate();
        }
    }

    /// <summary>设置显示文本，并按 maxWidth 宽度换行、自动调整高度。</summary>
    public void SetText(string text, int maxWidth)
    {
        _text = text ?? "";
        _maxWidth = maxWidth;
        RebuildLayout();
        Invalidate();
    }

    public void SetFont(Font font)
    {
        _font = font;
        RebuildLayout();
        Invalidate();
    }

    private void RebuildLayout()
    {
        _lines.Clear();
        if (string.IsNullOrEmpty(_text))
        {
            Size = Size.Empty;
            return;
        }

        var maxWidth = Math.Max(1, _maxWidth);
        _lineHeight = Math.Max(1, Measure("Ag").Height);
        _advance = Math.Max(_lineHeight, (int)Math.Round(_lineHeight * _lineSpacing));

        foreach (var line in WrapText(_text, maxWidth))
            _lines.Add(line);

        var totalHeight = _lines.Count == 0 ? 0 : (_lines.Count - 1) * _advance + _lineHeight;
        Size = new Size(maxWidth, totalHeight + 4);
    }

    private Size Measure(string text) =>
        TextRenderer.MeasureText(text, _font, new Size(int.MaxValue, int.MaxValue), LineFlags);

    private List<string> WrapText(string text, int maxWidth)
    {
        var result = new List<string>();
        foreach (var paragraph in text.Replace("\r\n", "\n").Split('\n'))
        {
            if (paragraph.Length == 0)
            {
                result.Add("");
                continue;
            }
            result.AddRange(WrapParagraph(paragraph, maxWidth));
        }
        return result;
    }

    private List<string> WrapParagraph(string paragraph, int maxWidth)
    {
        var lines = new List<string>();
        var line = new StringBuilder();
        foreach (var token in Tokenize(paragraph))
        {
            var candidate = line.Length == 0 ? token : line.ToString() + token;
            if (Measure(candidate).Width <= maxWidth || line.Length == 0)
            {
                line.Append(token);
            }
            else
            {
                lines.Add(line.ToString().TrimEnd());
                line.Clear();
                line.Append(token.TrimStart());
            }
        }
        if (line.Length > 0)
            lines.Add(line.ToString().TrimEnd());
        else if (lines.Count == 0)
            lines.Add("");
        return lines;
    }

    /// <summary>把段落拆成换行单位：CJK 按字符、拉丁按词（连续非空白非 CJK 为一词）、空白单独成段。</summary>
    private static IEnumerable<string> Tokenize(string text)
    {
        var i = 0;
        while (i < text.Length)
        {
            var ch = text[i];
            if (char.IsWhiteSpace(ch))
            {
                var j = i;
                while (j < text.Length && char.IsWhiteSpace(text[j])) j++;
                yield return text.Substring(i, j - i);
                i = j;
            }
            else if (IsCjk(ch))
            {
                yield return ch.ToString();
                i++;
            }
            else
            {
                var j = i;
                while (j < text.Length && !char.IsWhiteSpace(text[j]) && !IsCjk(text[j])) j++;
                yield return text.Substring(i, j - i);
                i = j;
            }
        }
    }

    private static bool IsCjk(char ch) =>
        ch >= 0x2E80 && ch <= 0x9FFF ||
        ch >= 0xF900 && ch <= 0xFAFF ||
        ch >= 0x3000 && ch <= 0x30FF;

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        if (_lines.Count == 0)
            return;

        for (var i = 0; i < _lines.Count; i++)
        {
            var y = 1 + i * _advance;
            var shadowRect = new Rectangle(2, y + 1, Math.Max(1, Width - 2), _advance);
            var textRect = new Rectangle(1, y, Math.Max(1, Width - 2), _advance);
            TextRenderer.DrawText(e.Graphics, _lines[i], _font, shadowRect, Color.Black, LineFlags);
            TextRenderer.DrawText(e.Graphics, _lines[i], _font, textRect, Color.White, LineFlags);
        }
    }
}
