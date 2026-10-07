# ScreenTranslator 架构文档（供 AI 二次开发参考）

> 本文档面向后续接手的 AI/开发者，目标是**无需通读全部源码即可上手改动**。
> 代码注释与 UI 均为中文；技术栈为 C# / .NET 10（`net10.0-windows`）+ WinForms。

## 1. 项目概览

一个 Windows 桌面「截图翻译」工具：

1. 用户拖拽**框选**屏幕上一块区域；
2. 程序以 `PollIntervalMs`（默认 150ms）为周期**定时截图**该区域；
3. 用降采样灰度签名做**变化检测**，忽略微小波动；
4. 内容保持稳定（**稳定期 / 去抖** `DebounceMs`，默认 300ms）后，把 PNG 发给**本地 LLM**（OpenAI 兼容 `/v1/chat/completions`，默认指向 LM Studio）；
5. 流式接收译文，在主窗口显示「原文 / 译文」，并可选用一个**置顶透明叠加层**把译文叠回屏幕上；
6. 维护**对话历史**（滚动窗口 + 自动摘要压缩）帮助模型保持上下文一致；
7. 可指定**目标窗口**，仅当该窗口处于前台时才检测/翻译。

## 2. 目录结构

```
screen-translator/
├── config.json                        # 运行时配置（JSON，与 AppConfig 字段一一对应）
├── ScreenTranslator/                  # 主程序（WinExe）
│   ├── ScreenTranslator.csproj
│   ├── Capture/
│   │   └── ScreenCapturer.cs          # 截取屏幕区域 → Frame（签名 + PNG）
│   ├── Client/                        # 与 LLM 通信
│   │   ├── ModelClient.cs             # 发送请求 + 解析 SSE 流式响应（翻译/总结双模型）
│   │   ├── ModelDiscovery.cs          # 列出 /v1/models 可用模型
│   │   ├── RequestBuilder.cs          # 拼 OpenAI chat completions 请求体
│   │   └── SseParser.cs               # 解析 data: 增量
│   ├── Core/                          # 与 UI / 网络无关的纯逻辑
│   │   ├── Abstractions.cs            # 关键接口：IFrameSource / ITranslator / ITranslationClient
│   │   ├── AppConfig.cs               # 配置 POCO + 默认值
│   │   ├── ConfigLoader.cs            # 读/写 JSON ↔ AppConfig
│   │   ├── CaptureLoop.cs             # 主循环：截图→检测→去抖→发送
│   │   ├── ChangeDetector.cs          # 分块灰度变化检测
│   │   ├── FrameSignature.cs          # 位图降采样成灰度签名
│   │   ├── ConversationManager.cs     # 对话历史 + 摘要压缩 + 剧情梗概增量补充
│   │   ├── ChatMessage.cs             # 消息记录（role/text/imagePng）
│   │   ├── SummaryStore.cs            # 长期摘要 + 剧情梗概按进程名持久化（memory/ 目录）
│   │   ├── Clock.cs                   # IClock/SystemClock（可测时钟）
│   │   ├── Newlines.cs                # 换行符互转（显示 vs 字面）
│   │   └── TranslationSplitter.cs     # 解析「原文：/译文：」标签
│   └── UI/
│       ├── Program.cs                 # 入口：读配置→列出模型→Application.Run
│       ├── MainForm.cs                # 主窗口，编排所有 UI + 生命周期
│       ├── OverlayForm.cs             # 置顶透明叠加层
│       ├── RegionSelectorForm.cs      # 全屏半透明框选
│       ├── SummaryPopupForm.cs        # 弹出式「上下文摘要」悬浮框（失焦自动隐藏、可编辑）
│       └── WindowEnumerator.cs        # 枚举顶层窗口 + 前台窗口检测
├── ScreenTranslator.Tests/            # xUnit 测试
│   ├── Core/...                       # CaptureLoop / ChangeDetector / 等单测
│   ├── Capture/ScreenCapturerIntegrationTests.cs
│   └── Client/...                     # ModelClient / RequestBuilder / SseParser
├── spike_2d.py / spike_layout.py / spike_diag.py   # 独立的图像文字检测实验（Python），与本 C# 程序无关
└── .codegraph/                        # CodeGraph 索引（代码理解用，非源码）
```

## 3. 核心数据流

```
Program.Main
  └─ ConfigLoader.Load("config.json")            → AppConfig
  └─ ModelDiscovery.ListModelsAsync(...)         → 模型列表（供 UI 下拉）
  └─ new SummaryStore()                          → 长期摘要 + 剧情梗概的磁盘存储
  └─ new ConversationManager(..., store)          → ITranslator
  └─ new MainForm(config, translator, client, models, store)

[运行期，定时器每 PollIntervalMs 触发一次]
MainForm timer.Tick
  ├─ 若目标窗口不在前台 → 冻结（不 Tick，且隐藏叠加层）
  └─ loop.Tick()
        ├─ source.Capture()                       → Frame{ Signature, PngBytes }
        │     └─ 目标窗口：PrintWindow 抓取窗口内容并裁剪到 region；否则 CopyFromScreen
        ├─ detector.IsChanged(signature)          → 是否「真实内容变化」
        ├─ 若变化：记录时间戳、取消在途请求、return
        └─ 若稳定超过 DebounceMs 且未发送过：Send(png)
              └─ translator.TranslateAsync(png)   → ConversationManager → ModelClient（SSE）
                    └─ 完成 → Translated 事件
                          └─ MainForm.OnTranslated → TranslationSplitter 拆分
                                ├─ 译文写主文本框
                                ├─ 追加历史
                                └─ UpdateOverlay()（若开启且前台则显示）
```

## 4. 模块职责（按文件）

### CaptureLoop（`Core/CaptureLoop.cs`）—— 主循环状态机
- 字段：`_lastChangeUtc`（最近一次变化时间）、`_sentForCurrentContent`（当前内容是否已发送）、`_inFlight`（在途请求的 CancellationTokenSource）、`_lastPng`（最后一次截图，供「继续」重发）。
- `Tick()`：截图 → 检测变化 → 稳定期判定 → 发送。**每 tick 都会截图**（用于变化检测），但只有稳定且未发送时才真正调用模型。
- `Send()`：取消旧请求、触发 `Translating` 事件、异步翻译。
- 事件：`Translated` / `Failed`（后台线程）、`Translating`（UI 线程）。
- `RetranslateLast()`：用最新提示词重发最后一张截图（编辑提示词后「继续」时用）。

### ScreenCapturer（`Capture/ScreenCapturer.cs`）—— 截图
- 构造可传入 `Func<IntPtr>`（目标窗口句柄，每次截图时读取，支持运行时切换）。
- 目标窗口非零时：`PrintWindow`（带 `PW_RENDERFULLCONTENT`）抓取**窗口内容**（含被叠加层/其它窗口遮挡的部分，且不含叠加层），再裁剪到 `_region`；失败或 region 不在窗口内则退回 `CopyFromScreen`。
- 目标窗口为零：直接 `CopyFromScreen` 截取 `_region`。
- `FrameSignature` 降采样成签名，`EncodePng` 编码 PNG。

### ChangeDetector（`Core/ChangeDetector.cs`）—— 变化检测
- 把 64×64 灰度签名切成 `blockSize`×`blockSize` 小块；块内「灰度差 > pixelEpsilon」的像素占比超过 `blockChangeFraction` 则记该块「变化」；变化块占比达到 `changeFraction` 判定整帧变化。
- 目的：忽略光线起伏（低 epsilon），忽略局部小图标跳动（分块 + 全局阈值）。

### FrameSignature（`Core/FrameSignature.cs`）—— 降采样
- box-average 把位图缩到 `SignatureWidth`×`SignatureHeight`，每像素 = Rec.601 亮度均值。

### ConversationManager（`Core/ConversationManager.cs`）—— 对话历史
- 每次请求滚动携带最近若干轮；超过 `HistoryMaxTurns` 触发**压缩**：只把「即将被裁剪掉」的旧内容（旧摘要 + 本次裁掉的部分）喂给模型生成新摘要，保留在滚动窗口里的最近 `HistoryKeepTurns` 轮不重复进摘要。
- 压缩在**后台异步执行**（fire-and-forget），不阻塞翻译返回；`_compressing` 标志防止重入。
- 线程安全（`lock(_gate)`），压缩失败不影响主流程。
- `Summary` 属性返回压缩后的长期摘要（未压缩为 null）；压缩完成后触发 `SummaryChanged` 事件（后台线程），供 UI 刷新显示。`SetSummary` 供外部恢复长期记忆（启动/切换窗口时从磁盘读入）。
- **剧情梗概**（独立于近期摘要）：**每两次压缩执行一次**（`_compressionCount` 计数），把最近 `StoryRecentMessages`（12）条消息 + 已有剧情末尾 `StoryTailLines`（20）行喂给模型，产出一段剧情补充，经 `IStoryStore` 追加到磁盘（`memory/_story_<进程名>.txt`），随进程累积越来越长。**不随对话发送给模型，也不显示在 UI**，仅在文件里累积完整剧情。`MemoryKey`（进程名）为 null 时不写剧情。提示词强调**完全忠实于给定上下文，禁止扩写/续写/脑补/编造**。
- **静置超时总结**：每次翻译后重置一个一次性 `Timer`（默认 10 分钟，`idleSummarizeDelay`）；若期间无新翻译则触发一次压缩（短期记忆 + 按规则顺带剧情）。触发后不再重复，直到下一次翻译重新计时——持续有新翻译则永不触发。
- **总结模型**：摘要压缩与剧情梗概这两类「总结」调用走 `ITranslationClient.SummarizeAsync`，可用单独配置的 `SummarizeModel`（更高一级模型）；翻译仍走 `TranslateAsync`。`SummarizeModel` 留空则跟随翻译模型。
- **开关**：`SummaryEnabled`（默认开）关闭后既不生成、也不携带短期摘要；`StoryEnabled`（默认开）关闭后不写剧情文件。二者独立，UI 以「短期记忆 / 长期留档」复选框切换并写回配置。

### Client 层
- `RequestBuilder`：把消息列表拼成 OpenAI 兼容 body，图片用 `data:image/png;base64,...`，`stream=true`。
- `ModelClient`：POST + 逐行读 SSE，`SseParser.ExtractDelta` 提取 `choices[0].delta.content` 拼成完整译文。`Model` 为可写属性（`volatile`），UI 切换模型后下次请求生效。另有 `SummarizeModel`（留空则跟随 `Model`）：`SummarizeAsync` 用它调用，供摘要压缩/剧情梗概走更高一级模型。
- `ModelDiscovery`：GET `/v1/models`，返回全部模型 `id` 列表。

### UI 层
- `MainForm`：全部控件、定时器、`CaptureLoop`、`OverlayForm` 的编排；`StartLoop`/`StopLoop`、`TargetIsForeground`、`UpdateOverlay`、`OnTranslated` 等。「模型」下拉框列出 `ModelDiscovery` 返回的模型，`SelectedIndexChanged` 时把选中项写入 `ModelClient.Model`；初始选中 `config.Model`（留空则 `AppConfig.DefaultModel`）。「查看摘要」按钮弹出 `SummaryPopupForm` 悬浮框显示 `ConversationManager.Summary`（订阅 `SummaryChanged` 刷新；无边框置顶，失去焦点自动隐藏）。目标窗口切换时通过 `SummaryStore` 按进程名把旧摘要落盘、载入新摘要；未选目标窗口则不存，启动时按记录的上次进程名自动恢复。剧情梗概只在后台累积写入 `memory/_story_<进程名>.txt`，不在界面显示。另加「总结模型」下拉框，选择摘要压缩/剧情梗概所用的模型（默认「跟随翻译模型」，即留空）。切换翻译/总结模型会写回 `config.json` 持久化到下次启动；「查看摘要」悬浮框现可编辑，失焦/按 Esc 时把修改经 `SetSummary` 写回 `SummaryStore` 对应摘要文件。另加「短期记忆」「长期留档」两个复选框，分别控制 `ITranslator.SummaryEnabled` / `StoryEnabled` 并写回 `config.json`。
- `OverlayForm`：无边框、置顶（`WS_EX_TOPMOST`）、鼠标穿透（`WS_EX_TRANSPARENT`）、不抢焦点（`WS_EX_NOACTIVATE`）、分层（`WS_EX_LAYERED`）、不显示在任务栏（`WS_EX_TOOLWINDOW`）；`TransparencyKey=Magenta` 实现透明背景，仅绘制白色文字 + 黑色阴影。
- `RegionSelectorForm`：全屏半透明（Opacity 0.3）框选，返回屏幕坐标矩形。
- `WindowEnumerator`：`EnumWindows` 枚举可见、有标题、非工具、非本进程的顶层窗口；`GetForegroundWindow` 提供前台窗口句柄。每个 `WindowInfo` 附带所属进程名（`ProcessName`），供摘要按进程持久化。

## 5. 配置项（`config.json` / `AppConfig`）

| 字段 | 默认 | 含义 |
|---|---|---|
| `endpointUrl` | `http://127.0.0.1:1234/v1/chat/completions` | LLM 端点 |
| `model` | `""` | 模型名；留空时 UI 默认选中 `lmstudio-community/Qwen3.5-4B-GGUF-no-thinking` |
| `summarizeModel` | `""` | 总结类调用（摘要压缩/剧情梗概）所用模型；留空则跟随翻译模型 |
| `summaryEnabled` | `true` | 是否启用短期记忆（摘要压缩）；关闭后不生成、也不携带摘要 |
| `storyEnabled` | `true` | 是否启用长期留档（剧情梗概写盘） |
| `prompt` | （见 config） | 发送给模型的提示词（要求输出「原文：/译文：」） |
| `pollIntervalMs` | 150 | 截图轮询间隔 |
| `debounceMs` | **300** | 稳定期 / 去抖时长（变化后保持稳定多久才发送） |
| `signatureWidth` / `signatureHeight` | 64 / 64 | 变化检测降采样尺寸 |
| `pixelDiffEpsilon` | 6 | 单像素灰度差阈值 |
| `blockSize` | 8 | 分块边长 |
| `blockChangeFraction` | 0.03 | 块内变化像素占比阈值 |
| `changeFraction` | 0.04 | 变化块占比阈值 |
| `historyMaxTurns` / `historyKeepTurns` | 15 / 5 | 历史压缩阈值 / 压缩后保留轮数 |
| `overlayEnabled` | false | 叠加层默认开关 |
| `overlayPosition` | `"top"` | 叠加层位置（top / bottom） |
| `fontName` / `fontSize` | Microsoft YaHei UI / 11 | 译文与叠加层字体 |
| `logPath` | `logs/translator.log` | 日志路径（当前未实际写入） |

## 6. 关键实现细节与坑

- **叠加层不进截图**：`OverlayForm` 是置顶分层窗口，`CopyFromScreen` 会把它一并截进图里。选择了目标窗口时，`ScreenCapturer` 改用 `PrintWindow` 直接抓取窗口内容——**天然不含叠加层**，还能抓到被遮挡的部分，因此叠加层无需隐藏、不会闪烁。未选择目标窗口时退回 `CopyFromScreen`（此时叠加层可能被截入，属已知限制）。
- **前台窗口判定**：`TargetIsForeground()` 在未选择目标窗口时返回 `true`（不限制）。叠加层带 `WS_EX_NOACTIVATE` 不会抢焦点，故 `GetForegroundWindow()` 不会因叠加层显示而改变。
- **叠加层可见性只有一个入口** `UpdateOverlay()`：同时受「叠加层开关」「是否已框选区域」「是否有译文」「目标窗口是否前台」四条件约束；需要隐藏/恢复叠加层的地方统一调它。
- **`Clock` 抽象**：`CaptureLoop` 依赖 `IClock` 而非 `DateTime.UtcNow`，测试用 `FakeClock` 推进时间。
- **`using var` 与 CTS**：`CaptureLoop` 故意不 Dispose `CancellationTokenSource`（Dispose 后再 Cancel 会抛异常），交给 GC。
- **换行**：WinForms 多行 TextBox 只认 CRLF；`Newlines.ToReal` 把字面 `\n` 转成 `\r\n` 显示，`ToLiteral` 反之写入历史。

## 7. 扩展点 / 常见改动

- **调整翻译触发灵敏度**：改 `config.json` 的 `debounceMs`（稳定期）、`pollIntervalMs`（轮询）、`changeFraction` 等变化检测阈值。
- **换模型/端点**：运行期在「模型」下拉框切换（写入 `ModelClient.Model`）；也可改 `endpointUrl` / `model`。流式解析在 `SseParser`，非流式需改 `ModelClient` 与 `RequestBuilder.Build(stream:)`。
- **加新 UI 项**：在 `MainForm` 构造函数加控件、`AppConfig` 加字段、`config.json` 同步，加载在 `ConfigLoader`。
- **叠加层样式**：改 `OverlayForm.OnPaint`（阴影/文字）与 `DrawFlags`（换行、对齐）。
- **变化检测算法**：`ChangeDetector` 纯函数式，易单测；`FrameSignature` 决定签名来源。

## 8. 构建与测试

```bash
# 构建主程序
dotnet build ScreenTranslator/ScreenTranslator.csproj

# 运行测试
dotnet test ScreenTranslator.Tests/ScreenTranslator.Tests.csproj

# 运行（指定配置路径）
dotnet run --project ScreenTranslator/ScreenTranslator.csproj -- config.json
```

测试框架 xUnit；`ScreenCapturerIntegrationTests` 依赖真实屏幕（会截取屏幕左上角区域），其余 `Core`/`Client` 测试为纯逻辑单测。

## 9. 最近改动

1. **截图不截叠加层**：目标窗口模式下改用 `PrintWindow` 抓取窗口内容（不含叠加层、含被遮挡部分），彻底去掉「隐藏叠加层」方案，消除闪烁。
2. **缩短稳定期**：`debounceMs` 默认 400 → 300。
3. **目标窗口不在前台时隐藏叠加层**：`UpdateOverlay` 增加 `TargetIsForeground()` 判定，并在定时器检测到前台状态切换（冻结/解冻）时刷新叠加层。
4. **模型改为用户自选**：不再自动探测模型；启动时 `ModelDiscovery.ListModelsAsync` 拉取全部模型填入「模型」下拉框，初始选中 `config.Model`（留空则 `lmstudio-community/Qwen3.5-4B-GGUF-no-thinking`），用户切换后固定不动。
5. **显示压缩后的长期摘要**：`ITranslator` 增加 `Summary`/`SummaryChanged`，`ConversationManager` 压缩完成后触发事件；`MainForm` 新增「查看摘要」按钮弹出 `SummaryPopupForm`（无边框置顶，失焦自动隐藏）显示摘要。
6. **长期记忆按进程持久化**：新增 `SummaryStore`（`memory/` 目录）按目标窗口所属进程名存档压缩摘要；切换窗口时落盘旧摘要、载入新摘要，未选目标窗口则不存；启动时按记录的上次进程名自动恢复窗口并载入摘要。
7. **剧情梗概单独成文件**：在近期摘要之外另维护一份「剧情梗概」，独立存到 `memory/_story_<进程名>.txt`，随进程累积越来越长、**不随对话发给模型、也不显示在界面**。压缩摘要时顺带让模型总结最近十几句对话 + 已有剧情末尾几行，产出补充追加到剧情文件（`IStoryStore`/`SummaryStore` 的 `LoadStory`/`AppendStory`/`LoadStoryTail`）。
8. **摘要只归档被裁剪的旧内容**：压缩时仅把「即将被裁剪掉」的部分（旧摘要 + 本次裁掉的内容）喂给模型，保留在滚动窗口里的最近 `HistoryKeepTurns` 轮不再重复写进摘要；摘要提示词强调「简洁、不过长」。
9. **总结类调用可用更高一级模型**：`ITranslationClient` 新增 `SummarizeAsync`，`ModelClient` 新增 `SummarizeModel`（留空跟随翻译模型）；摘要压缩与剧情梗概走 `SummarizeAsync`。UI 新增「总结模型」下拉框单独指定。剧情梗概提示词强调「完全忠实、禁止扩写/编造」。
10. **模型选择持久化 + 摘要可编辑**：切换翻译/总结模型时通过 `ConfigLoader.Save` 写回 `config.json`，下次启动恢复；「查看摘要」悬浮框改为可编辑，失焦/按 Esc 时把修改提交到 `ITranslator.SetSummary`（进而写回 `SummaryStore` 摘要文件）。
11. **压缩后台化 + 剧情降频 + 静置超时总结**：压缩改为 fire-and-forget 后台执行（不再阻塞翻译返回）；剧情梗概由「每次压缩」降为「每两次压缩执行一次」；新增静置超时总结——每次翻译后重置一次性 `Timer`（默认 10 分钟），静置超时即触发一次压缩，触发后仅一次，直到下次翻译重新计时。
12. **短期记忆 / 长期留档独立开关**：`AppConfig` 新增 `SummaryEnabled` / `StoryEnabled`（默认都开），`ITranslator` 同步暴露两个可写属性；关闭短期记忆后不生成/不携带摘要，关闭长期留档后不写剧情文件。UI 加「短期记忆」「长期留档」复选框，切换即写回 `config.json`。
