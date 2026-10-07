using System.Drawing;
using System.Windows.Forms;
using ScreenTranslator.Capture;
using ScreenTranslator.Client;
using ScreenTranslator.Core;

namespace ScreenTranslator.UI;

public sealed class MainForm : Form
{
    private readonly AppConfig _config;
    private readonly ITranslator _translator;
    private readonly Button _selectButton;
    private readonly Button _pauseButton;
    private readonly TextBox _translationBox;
    private readonly TextBox _historyBox;
    private readonly Button _summaryButton;
    private readonly Label _statusLabel;
    private readonly ComboBox _windowCombo;
    private readonly CheckBox _overlayCheck;
    private readonly ComboBox _positionCombo;
    private readonly ComboBox _fontNameCombo;
    private readonly NumericUpDown _fontSizeNumeric;
    private readonly NumericUpDown _lineSpacingNumeric;
    private readonly ComboBox _modelCombo;
    private readonly ComboBox _summarizeModelCombo;
    private readonly CheckBox _summaryCheck;
    private readonly CheckBox _storyCheck;
    private readonly CheckBox _pauseWhenNotForegroundCheck;
    private readonly ModelClient _modelClient;
    private readonly SummaryStore _store;
    private readonly string _configPath;

    private CaptureLoop? _loop;
    private System.Windows.Forms.Timer? _timer;
    private Rectangle? _region;
    private OverlayForm? _overlay;
    private IntPtr? _targetWindow;
    private Font _font;
    private bool _frozen;
    private bool _pauseWhenNotForeground;
    private SummaryPopupForm? _summaryPopup;
    private string? _currentProcess;
    private bool _suppressWindowPersist;

    private static readonly string[] FontNames =
    {
        "Microsoft YaHei UI", "Microsoft YaHei", "SimSun", "SimHei", "KaiTi",
        "FangSong", "Arial", "Segoe UI", "Consolas", "NSimSun",
    };

    private static readonly WindowInfo NoTarget = new(IntPtr.Zero, "（不限制）", null);

    public MainForm(AppConfig config, ITranslator translator, ModelClient modelClient, IReadOnlyList<string> models, SummaryStore store, string configPath)
    {
        _config = config;
        _translator = translator;
        _modelClient = modelClient;
        _store = store;
        _configPath = configPath;

        Log.Path = _config.LogPath;
        Log.Write("=== 屏幕翻译启动 ===");
        Log.Write($"配置: 签名 {_config.SignatureWidth}×{_config.SignatureHeight}，像素阈值 {_config.PixelDiffEpsilon}，块 {_config.BlockSize}，块内占比 {_config.BlockChangeFraction}，变化占比 {_config.ChangeFraction}，去抖 {_config.DebounceMs}ms，最大等待 {_config.MaxWaitMs}ms，轮询 {_config.PollIntervalMs}ms，失焦暂停 {_config.PauseWhenNotForeground}");

        Text = "屏幕翻译";
        ClientSize = new Size(620, 680);
        MinimumSize = new Size(420, 480);

        var fontName = string.IsNullOrWhiteSpace(_config.FontName) ? "Microsoft YaHei UI" : _config.FontName;
        var fontSize = _config.FontSize <= 0 ? 11f : _config.FontSize;
        _font = new Font(fontName, fontSize);

        _selectButton = new Button
        {
            Text = "框选区域",
            Location = new Point(12, 12),
            Size = new Size(110, 32),
            Anchor = AnchorStyles.Top | AnchorStyles.Left,
        };
        _selectButton.Click += (_, _) => SelectRegion();

        _pauseButton = new Button
        {
            Text = "暂停",
            Location = new Point(128, 12),
            Size = new Size(76, 32),
            Enabled = false,
            Anchor = AnchorStyles.Top | AnchorStyles.Left,
        };
        _pauseButton.Click += (_, _) => TogglePause();

        _statusLabel = new Label
        {
            Text = "未选择区域",
            AutoSize = false,
            Location = new Point(212, 16),
            Size = new Size(336, 24),
            TextAlign = ContentAlignment.MiddleLeft,
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
        };

        // 目标窗口（仅当其在前台时才检测）
        var windowLabel = new Label { Text = "目标窗口", Location = new Point(12, 52), AutoSize = true };
        _windowCombo = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Location = new Point(76, 48),
            Size = new Size(396, 24),
            Anchor = AnchorStyles.Top | AnchorStyles.Left,
        };
        RefreshWindowList();
        _windowCombo.SelectedIndexChanged += (_, _) => UpdateTargetWindow();
        _windowCombo.DropDown += (_, _) => RefreshWindowList();

        // 失焦暂停：目标窗口不在前台时是否暂停检测并隐藏叠加层
        _pauseWhenNotForegroundCheck = new CheckBox
        {
            Text = "失焦暂停",
            Location = new Point(480, 50),
            AutoSize = true,
            Checked = _config.PauseWhenNotForeground,
        };
        _pauseWhenNotForeground = _config.PauseWhenNotForeground;
        _pauseWhenNotForegroundCheck.CheckedChanged += (_, _) => ApplyPauseWhenNotForeground();

        // 模型：列出服务端可用模型，默认选中当前模型，之后由用户手动切换
        var modelLabel = new Label { Text = "模型", Location = new Point(12, 82), AutoSize = true };
        _modelCombo = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Location = new Point(76, 78),
            Size = new Size(220, 24),
            Anchor = AnchorStyles.Top | AnchorStyles.Left,
        };
        _modelCombo.Items.AddRange(models.ToArray());
        if (!string.IsNullOrWhiteSpace(_modelClient.Model) && !models.Contains(_modelClient.Model))
            _modelCombo.Items.Insert(0, _modelClient.Model);
        SelectModel(_modelClient.Model);
        _modelCombo.SelectedIndexChanged += (_, _) => ApplyModel();

        // 总结模型：短期摘要压缩 / 长期剧情梗概所用模型，可单独指定更高级的模型；默认「跟随翻译模型」。
        var summarizeLabel = new Label { Text = "总结模型", Location = new Point(304, 82), AutoSize = true };
        _summarizeModelCombo = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Location = new Point(366, 78),
            Size = new Size(182, 24),
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
        };
        _summarizeModelCombo.Items.Add("（跟随翻译模型）");
        _summarizeModelCombo.Items.AddRange(models.ToArray());
        SelectSummarizeModel(_config.SummarizeModel);
        _summarizeModelCombo.SelectedIndexChanged += (_, _) => ApplySummarizeModel();

        // 叠加层开关 / 位置 / 字体 / 字号
        _overlayCheck = new CheckBox
        {
            Text = "叠加层",
            Location = new Point(12, 112),
            AutoSize = true,
            Checked = _config.OverlayEnabled,
        };
        _overlayCheck.CheckedChanged += (_, _) => UpdateOverlay();

        var positionLabel = new Label { Text = "位置", Location = new Point(92, 116), AutoSize = true };
        _positionCombo = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Location = new Point(132, 110),
            Size = new Size(80, 24),
        };
        _positionCombo.Items.AddRange(new object[] { "顶部", "居中", "底部" });
        _positionCombo.SelectedIndex = _config.OverlayPosition.ToLowerInvariant() switch
        {
            "bottom" => 2,
            "center" => 1,
            _ => 0,
        };
        _positionCombo.SelectedIndexChanged += (_, _) => ApplyOverlayPosition();

        var fontLabel = new Label { Text = "字体", Location = new Point(222, 116), AutoSize = true };
        _fontNameCombo = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Location = new Point(258, 110),
            Size = new Size(140, 24),
        };
        _fontNameCombo.Items.AddRange(FontNames);
        SelectFontName(_config.FontName);
        _fontNameCombo.SelectedIndexChanged += (_, _) => ApplyFont();

        var sizeLabel = new Label { Text = "字号", Location = new Point(404, 116), AutoSize = true };
        _fontSizeNumeric = new NumericUpDown
        {
            Location = new Point(436, 110),
            Size = new Size(60, 24),
            Minimum = 8,
            Maximum = 72,
            DecimalPlaces = 0,
            Increment = 1,
            Value = Math.Clamp((decimal)fontSize, 8, 72),
        };
        _fontSizeNumeric.ValueChanged += (_, _) => ApplyFont();

        var lineSpacingLabel = new Label { Text = "行距", Location = new Point(504, 116), AutoSize = true };
        _lineSpacingNumeric = new NumericUpDown
        {
            Location = new Point(534, 110),
            Size = new Size(62, 24),
            Minimum = 0.5m,
            Maximum = 3.0m,
            DecimalPlaces = 1,
            Increment = 0.1m,
            Value = Math.Clamp((decimal)_config.OverlayLineSpacing, 0.5m, 3.0m),
        };
        _lineSpacingNumeric.ValueChanged += (_, _) => ApplyLineSpacing();

        // 提示词
        var promptLabel = new Label { Text = "提示词", Location = new Point(12, 148), AutoSize = true };
        var promptBox = new TextBox
        {
            Multiline = true,
            ScrollBars = ScrollBars.Vertical,
            Location = new Point(56, 144),
            Size = new Size(492, 60),
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
            Text = _config.Prompt,
        };
        promptBox.TextChanged += (_, _) =>
        {
            _translator.Prompt = promptBox.Text;
            Pause(); // 编辑提示词时自动暂停
        };

        // 译文
        _translationBox = new TextBox
        {
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            Location = new Point(12, 212),
            Size = new Size(536, 150),
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
            Font = _font,
        };

        // 上下文摘要：点「查看摘要」弹出悬浮框，失去焦点自动隐藏
        _summaryButton = new Button
        {
            Text = "查看摘要",
            Location = new Point(100, 362),
            Size = new Size(88, 26),
            Anchor = AnchorStyles.Top | AnchorStyles.Left,
        };
        _summaryButton.Click += (_, _) => ShowSummaryPopup();

        // 记忆开关：短期记忆（摘要压缩）、长期留档（剧情梗概）
        _summaryCheck = new CheckBox
        {
            Text = "短期记忆",
            Location = new Point(200, 366),
            AutoSize = true,
            Checked = _config.SummaryEnabled,
        };
        _summaryCheck.CheckedChanged += (_, _) => ApplyMemoryToggles();

        _storyCheck = new CheckBox
        {
            Text = "长期留档",
            Location = new Point(310, 366),
            AutoSize = true,
            Checked = _config.StoryEnabled,
        };
        _storyCheck.CheckedChanged += (_, _) => ApplyMemoryToggles();

        var historyLabel = new Label { Text = "历史记录", Location = new Point(12, 368), AutoSize = true };
        _historyBox = new TextBox
        {
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            Location = new Point(12, 386),
            Size = new Size(536, 280),
            Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
            Font = new Font("Microsoft YaHei UI", 10F),
        };

        Controls.Add(_selectButton);
        Controls.Add(_pauseButton);
        Controls.Add(_statusLabel);
        Controls.Add(windowLabel);
        Controls.Add(_windowCombo);
        Controls.Add(_pauseWhenNotForegroundCheck);
        Controls.Add(modelLabel);
        Controls.Add(_modelCombo);
        Controls.Add(summarizeLabel);
        Controls.Add(_summarizeModelCombo);
        Controls.Add(_overlayCheck);
        Controls.Add(positionLabel);
        Controls.Add(_positionCombo);
        Controls.Add(fontLabel);
        Controls.Add(_fontNameCombo);
        Controls.Add(sizeLabel);
        Controls.Add(_fontSizeNumeric);
        Controls.Add(lineSpacingLabel);
        Controls.Add(_lineSpacingNumeric);
        Controls.Add(promptLabel);
        Controls.Add(promptBox);
        Controls.Add(_translationBox);
        Controls.Add(_summaryButton);
        Controls.Add(_summaryCheck);
        Controls.Add(_storyCheck);
        Controls.Add(historyLabel);
        Controls.Add(_historyBox);

        _overlay = new OverlayForm(_font);
        _overlay.LineSpacing = Math.Clamp(_config.OverlayLineSpacing, 0.5f, 3.0f);
        _translationBox.TextChanged += (_, _) => UpdateOverlay();
        _translator.SummaryChanged += OnSummaryChanged;
        TryRestoreLastWindow();
    }

    private void SelectRegion()
    {
        using (var selector = new RegionSelectorForm())
        {
            if (selector.ShowDialog(this) != DialogResult.OK)
                return;
            _region = selector.SelectedRegion;
        }

        if (_region is not Rectangle region || region.Width <= 0 || region.Height <= 0)
        {
            SetStatus("选择的区域无效");
            return;
        }

        StartLoop(region);
    }

    private void StartLoop(Rectangle region)
    {
        StopLoop();

        var source = new ScreenCapturer(region, _config.SignatureWidth, _config.SignatureHeight,
            () => _targetWindow ?? IntPtr.Zero);
        var detector = new ChangeDetector(
            _config.SignatureWidth, _config.SignatureHeight,
            _config.ChangeFraction,
            _config.CancelChangeFraction,
            _config.PixelDiffEpsilon,
            _config.BlockSize,
            _config.BlockChangeFraction);
        var loop = new CaptureLoop(source, detector, _translator, new SystemClock(), _config.DebounceMs, _config.MaxWaitMs, _config.MaxWaitCeilingMs);
        loop.Translated += OnTranslated;
        loop.Failed += OnFailed;
        loop.Translating += OnTranslating;

        Log.Write($"开始监控: 区域 {region.Width}×{region.Height}@({region.X},{region.Y})");

        var timer = new System.Windows.Forms.Timer { Interval = _config.PollIntervalMs };
        timer.Tick += (_, _) =>
        {
            if (_pauseWhenNotForeground && !TargetIsForeground())
            {
                if (!_frozen)
                {
                    _frozen = true;
                    SetStatus("目标窗口不在最前，检测已冻结");
                    Log.Write("冻结：目标窗口不在最前（失焦暂停开启）");
                    UpdateOverlay();
                }
                return;
            }
            if (_frozen)
            {
                _frozen = false;
                SetStatus("监控中…");
                Log.Write("解冻：目标窗口回到最前");
                UpdateOverlay();
            }
            loop.Tick();
        };
        timer.Start();

        _loop = loop;
        _timer = timer;
        _pauseButton.Enabled = true;
        _pauseButton.Text = "暂停";
        _translationBox.Clear();
        _historyBox.Clear();
        _overlay?.Hide();
        _frozen = false;
        SetStatus($"已框选区域 ({region.Width}×{region.Height})，监控中…");
    }

    private bool TargetIsForeground()
    {
        if (_targetWindow is not IntPtr hwnd)
            return true; // 未选择目标窗口：不限制
        return WindowEnumerator.ForegroundWindow == hwnd;
    }

    private void UpdateTargetWindow()
    {
        var info = _windowCombo.SelectedItem as WindowInfo;
        var handle = info?.Handle ?? IntPtr.Zero;
        var process = handle == IntPtr.Zero ? null : info!.ProcessName;

        // 切走前，把旧进程的长期记忆落盘（没选就不存）
        if (!_suppressWindowPersist && !string.IsNullOrWhiteSpace(_currentProcess))
            _store.Save(_currentProcess, _translator.Summary);

        _targetWindow = handle == IntPtr.Zero ? null : handle;
        _currentProcess = string.IsNullOrWhiteSpace(process) ? null : process;
        _translator.MemoryKey = _currentProcess;

        if (_suppressWindowPersist)
            return;

        if (_currentProcess is null)
        {
            // 未选择目标窗口：不存，也清除自动恢复记录
            _store.SaveLastProcessName(null);
            return;
        }

        _store.SaveLastProcessName(_currentProcess);
        _translator.SetSummary(_store.Load(_currentProcess));
    }

    private void TryRestoreLastWindow()
    {
        var last = _store.LoadLastProcessName();
        if (string.IsNullOrWhiteSpace(last))
            return;
        for (var i = 0; i < _windowCombo.Items.Count; i++)
        {
            if (_windowCombo.Items[i] is WindowInfo w && string.Equals(w.ProcessName, last, StringComparison.Ordinal))
            {
                _windowCombo.SelectedIndex = i; // 触发 UpdateTargetWindow → 载入摘要
                return;
            }
        }
    }

    private void RefreshWindowList()
    {
        _suppressWindowPersist = true;
        try
        {
            var previous = _windowCombo.SelectedItem as WindowInfo;
            var windows = WindowEnumerator.GetTopLevelWindows();

            _windowCombo.BeginUpdate();
            _windowCombo.Items.Clear();
            _windowCombo.Items.Add(NoTarget);
            foreach (var w in windows)
                _windowCombo.Items.Add(w);

            var idx = previous is null
                ? 0
                : windows.FindIndex(w => w.Handle == previous.Handle) + 1; // +1 跳过「不限制」
            _windowCombo.SelectedIndex = idx >= 0 ? idx : 0;
            _windowCombo.EndUpdate();
        }
        finally
        {
            _suppressWindowPersist = false;
        }
    }

    private void SelectFontName(string preferred)
    {
        var idx = Array.FindIndex(FontNames, f => string.Equals(f, preferred, StringComparison.OrdinalIgnoreCase));
        _fontNameCombo.SelectedIndex = idx >= 0 ? idx : 0;
    }

    private void SelectModel(string preferred)
    {
        var idx = -1;
        for (var i = 0; i < _modelCombo.Items.Count; i++)
        {
            if (string.Equals(_modelCombo.Items[i] as string, preferred, StringComparison.Ordinal))
            {
                idx = i;
                break;
            }
        }
        _modelCombo.SelectedIndex = _modelCombo.Items.Count == 0 ? -1 : (idx >= 0 ? idx : 0);
    }

    private void ApplyModel()
    {
        if (_modelCombo.SelectedItem is string model)
        {
            _modelClient.Model = model;
            _config.Model = model;
            SaveConfig();
        }
    }

    private void SelectSummarizeModel(string preferred)
    {
        var idx = -1;
        for (var i = 0; i < _summarizeModelCombo.Items.Count; i++)
        {
            if (string.Equals(_summarizeModelCombo.Items[i] as string, preferred, StringComparison.Ordinal))
            {
                idx = i;
                break;
            }
        }
        _summarizeModelCombo.SelectedIndex = idx >= 0 ? idx : 0;
    }

    private void ApplySummarizeModel()
    {
        if (_summarizeModelCombo.SelectedIndex <= 0)
        {
            _modelClient.SummarizeModel = ""; // 跟随翻译模型
            _config.SummarizeModel = "";
        }
        else if (_summarizeModelCombo.SelectedItem is string model)
        {
            _modelClient.SummarizeModel = model;
            _config.SummarizeModel = model;
        }
        SaveConfig();
    }

    private void ApplyMemoryToggles()
    {
        _translator.SummaryEnabled = _summaryCheck.Checked;
        _translator.StoryEnabled = _storyCheck.Checked;
        _config.SummaryEnabled = _summaryCheck.Checked;
        _config.StoryEnabled = _storyCheck.Checked;
        SaveConfig();
    }

    private void ApplyPauseWhenNotForeground()
    {
        _pauseWhenNotForeground = _pauseWhenNotForegroundCheck.Checked;
        _config.PauseWhenNotForeground = _pauseWhenNotForeground;
        SaveConfig();
        if (!_pauseWhenNotForeground && _frozen)
        {
            // 关闭「失焦暂停」：立即解除冻结，恢复正常检测与叠加层显示
            _frozen = false;
            SetStatus("监控中…");
        }
        UpdateOverlay();
    }

    private void SaveConfig()
    {
        try
        {
            ConfigLoader.Save(_configPath, _config);
        }
        catch
        {
            // 写回配置失败不影响运行
        }
    }

    private void ApplyFont()
    {
        var name = _fontNameCombo.SelectedItem as string;
        if (string.IsNullOrWhiteSpace(name))
            return;
        var size = Math.Clamp((float)_fontSizeNumeric.Value, 8f, 72f);

        Font newFont;
        try
        {
            newFont = new Font(name, size);
        }
        catch
        {
            return;
        }

        var old = _font;
        _font = newFont;
        _translationBox.Font = newFont;
        _overlay?.SetFont(newFont);
        old.Dispose();
    }

    private void ApplyOverlayPosition()
    {
        _config.OverlayPosition = _positionCombo.SelectedIndex switch
        {
            2 => "bottom",
            1 => "center",
            _ => "top",
        };
        SaveConfig();
        UpdateOverlay();
    }

    private void ApplyLineSpacing()
    {
        var spacing = Math.Clamp((float)_lineSpacingNumeric.Value, 0.5f, 3.0f);
        _config.OverlayLineSpacing = spacing;
        if (_overlay is not null)
            _overlay.LineSpacing = spacing;
        SaveConfig();
        UpdateOverlay();
    }

    private void UpdateOverlay()
    {
        if (_overlay is null)
            return;

        if (!_overlayCheck.Checked || _region is not Rectangle region || (_pauseWhenNotForeground && !TargetIsForeground()))
        {
            _overlay.Hide();
            return;
        }

        var text = _translationBox.Text;
        if (string.IsNullOrWhiteSpace(text))
        {
            _overlay.Hide();
            return;
        }

        _overlay.SetText(text, region.Width);
        var x = region.X;
        var y = _positionCombo.SelectedIndex switch
        {
            2 => region.Bottom - _overlay.Height,
            1 => region.Y + Math.Max(0, (region.Height - _overlay.Height) / 2),
            _ => region.Top,
        };
        _overlay.Location = new Point(x, y);
        _overlay.Show();
        _overlay.BringToFront();
    }

    private void TogglePause()
    {
        if (_timer is null)
            return;

        if (_timer.Enabled)
            Pause();
        else
            Resume();
    }

    private void Pause()
    {
        if (_timer is null || !_timer.Enabled)
            return;
        _timer.Stop();
        _pauseButton.Text = "继续";
        SetStatus("已暂停");
    }

    private void Resume()
    {
        if (_timer is null || _timer.Enabled)
            return;
        _timer.Start();
        _pauseButton.Text = "暂停";
        SetStatus("监控中…");
        _loop?.RetranslateLast(); // 继续时用保留的最后一张截图重新翻译（用最新提示词）
    }

    private void StopLoop()
    {
        _timer?.Stop();
        _timer?.Dispose();
        _timer = null;
        _loop = null;
        _pauseButton.Enabled = false;
    }

    private void OnTranslating()
    {
        if (IsDisposed)
            return;
        if (InvokeRequired)
        {
            BeginInvoke(new Action(OnTranslating));
            return;
        }
        SetStatus("翻译中…");
    }

    private void OnTranslated(string text)
    {
        if (IsDisposed)
            return;
        if (InvokeRequired)
        {
            BeginInvoke(new Action<string>(OnTranslated), text);
            return;
        }
        var result = TranslationSplitter.Split(text);
        if (TranslationSplitter.IsNoText(result))
        {
            // 图片里没有文字：清空译文区，不写入历史，避免显示上一次的译文。
            Log.Write("翻译结果判定为「无文字」，清空译文区");
            _translationBox.Text = "";
            SetStatus($"已翻译 {DateTime.Now:HH:mm:ss}（无文字）");
            return;
        }
        _translationBox.Text = Newlines.ToReal(result.Translation);
        AppendHistory(Newlines.ToLiteral(result.Original), Newlines.ToLiteral(result.Translation));
        SetStatus($"已翻译 {DateTime.Now:HH:mm:ss}");
    }

    private void AppendHistory(string original, string translation)
    {
        var entry = string.IsNullOrWhiteSpace(original)
            ? $"译文：{translation}"
            : $"原文：{original}\r\n译文：{translation}";
        _historyBox.AppendText($"[{DateTime.Now:HH:mm:ss}]\r\n{entry}\r\n\r\n");
        _historyBox.SelectionStart = _historyBox.TextLength;
        _historyBox.ScrollToCaret();
    }

    private void OnFailed(Exception ex)
    {
        if (IsDisposed)
            return;
        if (InvokeRequired)
        {
            BeginInvoke(new Action<Exception>(OnFailed), ex);
            return;
        }
        SetStatus($"错误：{ex.Message}");
    }

    private void OnSummaryChanged()
    {
        if (IsDisposed)
            return;
        if (InvokeRequired)
        {
            BeginInvoke(new Action(OnSummaryChanged));
            return;
        }
        _summaryPopup?.RefreshText(Newlines.ToReal(_translator.Summary ?? ""));
        if (!string.IsNullOrWhiteSpace(_currentProcess))
            _store.Save(_currentProcess, _translator.Summary);
    }

    private void ShowSummaryPopup()
    {
        var text = Newlines.ToReal(_translator.Summary ?? "");
        if (string.IsNullOrWhiteSpace(text))
            text = "（暂无摘要）";
        if (_summaryPopup is null || _summaryPopup.IsDisposed)
        {
            _summaryPopup = new SummaryPopupForm();
            _summaryPopup.OnTextCommitted = CommitSummaryEdit;
        }
        _summaryPopup.ShowNear(_summaryButton, text);
    }

    private void CommitSummaryEdit(string text)
    {
        // 空或占位符视为清空摘要
        if (string.IsNullOrWhiteSpace(text) || string.Equals(text, "（暂无摘要）", StringComparison.Ordinal))
        {
            _translator.SetSummary(null);
            return;
        }
        _translator.SetSummary(text.Replace("\r\n", "\n"));
    }

    private void SetStatus(string message) => _statusLabel.Text = message;

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        StopLoop();
        _overlay?.Close();
        _overlay?.Dispose();
        _overlay = null;
        _summaryPopup?.Dispose();
        _summaryPopup = null;
        base.OnFormClosed(e);
    }
}
