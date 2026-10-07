namespace ScreenTranslator.Core;

public sealed class AppConfig
{
    /// <summary>OpenAI 兼容 chat completions 端点。</summary>
    public string EndpointUrl { get; set; } = "http://127.0.0.1:1234/v1/chat/completions";

    /// <summary>配置未指定模型时，UI 中默认选中的模型。</summary>
    public const string DefaultModel = "lmstudio-community/Qwen3.5-4B-GGUF-no-thinking";

    /// <summary>模型名；留空时在 UI 中默认选中 <see cref="DefaultModel"/>。</summary>
    public string Model { get; set; } = "";

    /// <summary>总结类调用（短期摘要压缩、长期剧情梗概）所用模型；留空则跟随翻译模型。</summary>
    public string SummarizeModel { get; set; } = "";

    /// <summary>API Key（Bearer Token）。本地模型留空；接入线上模型（如 OpenAI/兼容服务）时填写。</summary>
    public string ApiKey { get; set; } = "";

    /// <summary>是否启用短期记忆（历史压缩成摘要）。关闭后不生成、也不携带摘要，滚动窗口仍照常裁剪。</summary>
    public bool SummaryEnabled { get; set; } = false;

    /// <summary>是否启用长期留档（剧情梗概增量写入磁盘文件）。</summary>
    public bool StoryEnabled { get; set; } = false;

    /// <summary>目标语言：翻译提示词中的 {lang} 占位符在请求时替换为此值。</summary>
    public string TargetLanguage { get; set; } = "中文";

    /// <summary>发送给模型的提示词（{lang} 占位符会替换为 <see cref="TargetLanguage"/>）。</summary>
    public string Prompt { get; set; } = "把图片里的文字翻译成{lang}，并给出原文。只专注于识别并翻译画面上的文字，不要描述或评价画面内容（如背景、人物、场景等）。严格按以下格式输出（两个标签各自单独占一行，标签后接内容，可多行）：\n原文：\n译文：\n其中「原文：」后放图片里的原文，「译文：」后放{lang}译文。如果图片里没有任何文字，就只输出一个空行（不要描述画面内容）；禁止输出历史对话或上一次翻译里的任何内容，也不要任何解释或前缀。";

    /// <summary>近期摘要压缩提示词的默认值。</summary>
    public const string DefaultSummaryPrompt = "请把以上对话并入已有的近期摘要。你是一个翻译工具，本职工作是翻译，不是记录剧情或攻略：只保留对翻译有帮助、容易翻译错或需要保持一致的信息，例如专有名词（人名、地名、物品名）、术语、背景信息、当前场景与事件状态等；不要像游戏攻略那样记录「重要」的剧情事件或任务进展，凡与翻译无关的内容一律丢弃。摘要总长度必须严格控制在 400 字以内，这是一条硬性要求：宁可大胆丢弃旧信息，也绝不能超过 400 字，输出前请自行确认字数没有超限。明显是游戏界面元素而非文本的（如「载入」「保存」「购物」「休息」等菜单或提示）与翻译无关，可直接忽略，不必写入摘要。";

    /// <summary>剧情梗概补充提示词的默认值。</summary>
    public const string DefaultStoryPrompt = "下面是一次翻译任务的输出记录。请据此为剧情梗概补充新内容，规则如下：\n1. 只忠实提炼「新增对话」中明确出现的内容，绝对禁止编造、扩写、续写、脑补原文里没有的情节、人物或事件。\n2. 「已归档的剧情梗概（最后部分）」是此前已写好的剧情，不要重复或复述它；只从它结束的位置往后补充新剧情。\n3. 「新增对话」是逐句翻译后的游戏内文本（可能是角色台词、旁白、说明文字等），请按文本本身的内容理解剧情，不要臆断说话人。\n4. 输出长度不限，尽量完整、忠实；若「新增对话」中没有任何剧情进展，就只输出一个空行。\n5. 明显是游戏界面元素而非剧情的文本（如「载入」「保存」「购物」「休息」等菜单或提示）与剧情无关，可直接忽略，不要写入梗概。\n";

    /// <summary>近期摘要压缩提示词。</summary>
    public string SummaryPrompt { get; set; } = DefaultSummaryPrompt;

    /// <summary>剧情梗概补充提示词（不含结尾动态追加的「已归档梗概 / 新增对话」段落）。</summary>
    public string StoryPrompt { get; set; } = DefaultStoryPrompt;

    /// <summary>摘要超长补救提示词的默认值。占位符：{len}=当前长度、{limit}=上限、{summary}=待压缩摘要。</summary>
    public const string DefaultSummaryLengthPrompt = "你上一轮输出的近期摘要太长了（当前 {len} 字，上限 {limit} 字）。请立刻把它压缩到 {limit} 字以内，只保留最关键的人物、专有名词、术语和最近发生的事件，其余内容一律删除，并务必保证输出不超过 {limit} 字：\n\n{summary}";

    /// <summary>近期摘要作为 system 消息时的前缀默认值。</summary>
    public const string DefaultSummaryPrefix = "【此前对话摘要】\n";

    /// <summary>历史里标记「上一次截图」的占位文本默认值。</summary>
    public const string DefaultUserTurnMarker = "[此前截图]";

    /// <summary>摘要超长补救提示词（占位符见 <see cref="DefaultSummaryLengthPrompt"/>）。</summary>
    public string SummaryLengthPrompt { get; set; } = DefaultSummaryLengthPrompt;

    /// <summary>近期摘要作为 system 消息时的前缀。</summary>
    public string SummaryPrefix { get; set; } = DefaultSummaryPrefix;

    /// <summary>历史里标记「上一次截图」的占位文本。</summary>
    public string UserTurnMarker { get; set; } = DefaultUserTurnMarker;

    /// <summary>轮询间隔（毫秒）。</summary>
    public int PollIntervalMs { get; set; } = 150;

    /// <summary>内容变化后需要保持稳定的时长（毫秒），超过才发送。</summary>
    public int DebounceMs { get; set; } = 300;

    /// <summary>画面持续变化时的初始超时（毫秒）：超过该时长且无在途请求时，即使画面仍在变也强制翻译一次；译文与上次几乎相同时会成倍退避，上限见 <see cref="MaxWaitCeilingMs"/>。</summary>
    public int MaxWaitMs { get; set; } = 1000;

    /// <summary>超时退避上限（毫秒）：连续多次译文几乎相同时，超时最多放大到该值。</summary>
    public int MaxWaitCeilingMs { get; set; } = 30000;

    /// <summary>变化检测降采样宽度。</summary>
    public int SignatureWidth { get; set; } = 64;

    /// <summary>变化检测降采样高度。</summary>
    public int SignatureHeight { get; set; } = 64;

    /// <summary>变化判定：单个像素灰度差超过该值才视为显著变化（0–255）。微小波动（光线起伏等）低于此值会被忽略。</summary>
    public int PixelDiffEpsilon { get; set; } = 6;

    /// <summary>变化判定：切块的边长（签名像素）。</summary>
    public int BlockSize { get; set; } = 8;

    /// <summary>变化判定：块内显著变化像素占比超过该值即视为该块有变化（0–1）。</summary>
    public double BlockChangeFraction { get; set; } = 0.03;

    /// <summary>变化判定：变化块数占总块数的比例达到该值即视为内容变化（0–1）。</summary>
    public double ChangeFraction { get; set; } = 0.04;

    /// <summary>取消判定：变化块数占总块数的比例达到该值（应显著大于 <see cref="ChangeFraction"/>）才取消在途请求。普通小变化只标记待重译、不打断在途请求。</summary>
    public double CancelChangeFraction { get; set; } = 0.5;

    /// <summary>滚动保留的最近对话轮数上限，超过后触发压缩。</summary>
    public int HistoryMaxTurns { get; set; } = 15;

    /// <summary>压缩后保留的最近对话轮数。</summary>
    public int HistoryKeepTurns { get; set; } = 5;

    /// <summary>叠加层默认开关。</summary>
    public bool OverlayEnabled { get; set; } = false;

    /// <summary>叠加层位置：top（靠上）、center（居中）或 bottom（靠下）。</summary>
    public string OverlayPosition { get; set; } = "top";

    /// <summary>叠加层行距倍率：1.0 为字体默认行距，越大行间越稀疏。</summary>
    public float OverlayLineSpacing { get; set; } = 1.0f;

    /// <summary>叠加层文字描边粗细：false=细描边（默认，单次 1px 阴影）；true=粗描边（四周约 2px）。</summary>
    public bool OverlayThickOutline { get; set; } = false;

    /// <summary>目标窗口失去前台焦点时是否暂停检测并隐藏叠加层。关闭后无论是否前台都照常处理。</summary>
    public bool PauseWhenNotForeground { get; set; } = true;

    /// <summary>译文文本框与叠加层共用的字体名。</summary>
    public string FontName { get; set; } = "Microsoft YaHei UI";

    /// <summary>译文文本框与叠加层共用的字号。</summary>
    public float FontSize { get; set; } = 11f;

    public string LogPath { get; set; } = "logs/translator.log";
}
