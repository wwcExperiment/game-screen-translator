using System.Net.Http;
using System.Windows.Forms;
using ScreenTranslator.Client;
using ScreenTranslator.Core;

namespace ScreenTranslator.UI;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        // 锚定到 exe 目录：config.json / logs / memory 均用相对路径，
        // 若不固定工作目录，从快捷方式或其它目录启动时会读写到错误位置（会出现多份 config）。
        Directory.SetCurrentDirectory(AppContext.BaseDirectory);

        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        var configPath = args.Length > 0 ? args[0] : "config.json";
        var config = ConfigLoader.Load(configPath);
        Log.Path = config.LogPath;

        // 未配置端点：弹窗提醒用户编辑配置文件后退出。
        if (string.IsNullOrWhiteSpace(config.EndpointUrl))
        {
            MessageBox.Show(
                $"尚未配置模型端点（EndpointUrl）。\n请编辑配置文件后重新启动：\n{Path.GetFullPath(configPath)}",
                "ScreenTranslator",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return;
        }

        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(120) };

        // 列出服务端可用模型供用户在 UI 选择；失败则留空（仍可用默认模型）。
        var models = new List<string>();
        try
        {
            models = ModelDiscovery.ListModelsAsync(http, config.EndpointUrl, config.ApiKey).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            models = new List<string>();
            Log.Write($"启动时 API 连接失败（EndpointUrl={config.EndpointUrl}）：{ex.GetType().Name}: {ex.Message}");
            MessageBox.Show(
                $"无法连接模型服务，启动时模型列表请求失败。\n请检查配置文件里的 EndpointUrl / ApiKey / 模型名是否正确。\n具体错误已写入日志文件：\n{Path.GetFullPath(Log.Path)}",
                "ScreenTranslator",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }

        // 初始模型：优先用配置里的，否则用固定默认值；之后由用户在 UI 手动切换。
        var model = string.IsNullOrWhiteSpace(config.Model) ? AppConfig.DefaultModel : config.Model;

        var client = new ModelClient(http, config.EndpointUrl, model, config.SummarizeModel, config.ApiKey);
        var store = new SummaryStore();
        var translator = new ConversationManager(client, config.Prompt, config.HistoryMaxTurns, config.HistoryKeepTurns, store)
        {
            SummaryEnabled = config.SummaryEnabled,
            StoryEnabled = config.StoryEnabled,
            SummaryPrompt = config.SummaryPrompt,
            StoryPrompt = config.StoryPrompt,
            SummaryLengthPrompt = config.SummaryLengthPrompt,
            SummaryPrefix = config.SummaryPrefix,
            UserTurnMarker = config.UserTurnMarker,
            TargetLanguage = config.TargetLanguage,
        };
        Application.Run(new MainForm(config, translator, client, models, store, configPath));
    }
}
