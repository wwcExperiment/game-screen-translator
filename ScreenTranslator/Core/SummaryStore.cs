using System.IO;
using System.Text;

namespace ScreenTranslator.Core;

/// <summary>剧情梗概的持久化抽象（按进程名，累积追加，不随对话给模型）。</summary>
public interface IStoryStore
{
    /// <summary>读剧情梗概全文；无存档返回 null。</summary>
    string? LoadStory(string? key);

    /// <summary>把一段剧情补充追加到存档末尾。</summary>
    void AppendStory(string? key, string text);

    /// <summary>读存档最后 maxLines 行；无存档返回 null。</summary>
    string? LoadStoryTail(string? key, int maxLines);
}

/// <summary>
/// 把长期摘要按「键」持久化到磁盘（<c>memory/</c> 目录）。
/// 键通常是目标窗口所属进程名（可执行文件名），跨窗口/跨启动保持稳定；键里的非法文件名字符会被替换。
/// </summary>
public sealed class SummaryStore : IStoryStore
{
    private readonly string _directory;

    public SummaryStore(string directory = "memory")
    {
        _directory = directory;
    }

    /// <summary>按键加载摘要；无存档或键为空返回 null。</summary>
    public string? Load(string? key)
    {
        if (string.IsNullOrWhiteSpace(key))
            return null;
        var path = GetPath(key);
        try
        {
            if (!File.Exists(path))
                return null;
            var text = File.ReadAllText(path, Encoding.UTF8);
            return text.Length == 0 ? null : text;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>按键保存摘要；摘要为空则删除对应存档。</summary>
    public void Save(string? key, string? summary)
    {
        if (string.IsNullOrWhiteSpace(key))
            return;
        var path = GetPath(key);
        try
        {
            if (string.IsNullOrWhiteSpace(summary))
            {
                if (File.Exists(path))
                    File.Delete(path);
                return;
            }
            Directory.CreateDirectory(_directory);
            File.WriteAllText(path, summary, Encoding.UTF8);
        }
        catch
        {
            // 持久化失败不影响翻译主流程
        }
    }

    /// <summary>读取上次使用的进程名（无记录返回 null）。</summary>
    public string? LoadLastProcessName()
    {
        try
        {
            if (!File.Exists(LastProcessPath))
                return null;
            var name = File.ReadAllText(LastProcessPath, Encoding.UTF8).Trim();
            return name.Length == 0 ? null : name;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>记录上次使用的进程名（null 表示清除）。</summary>
    public void SaveLastProcessName(string? processName)
    {
        try
        {
            Directory.CreateDirectory(_directory);
            if (string.IsNullOrWhiteSpace(processName))
            {
                if (File.Exists(LastProcessPath))
                    File.Delete(LastProcessPath);
                return;
            }
            File.WriteAllText(LastProcessPath, processName, Encoding.UTF8);
        }
        catch
        {
            // 忽略
        }
    }

    /// <summary>读剧情梗概全文；无存档返回 null。</summary>
    public string? LoadStory(string? key)
    {
        if (string.IsNullOrWhiteSpace(key))
            return null;
        var path = GetStoryPath(key);
        try
        {
            if (!File.Exists(path))
                return null;
            var text = File.ReadAllText(path, Encoding.UTF8);
            return text.Length == 0 ? null : text;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>把一段剧情补充追加到存档末尾（空段不写入）。</summary>
    public void AppendStory(string? key, string text)
    {
        if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(text))
            return;
        var path = GetStoryPath(key);
        try
        {
            Directory.CreateDirectory(_directory);
            var separator = File.Exists(path) ? "\n\n" : "";
            File.AppendAllText(path, separator + text.Trim(), Encoding.UTF8);
        }
        catch
        {
            // 忽略
        }
    }

    /// <summary>读存档最后 maxLines 行；无存档返回 null。</summary>
    public string? LoadStoryTail(string? key, int maxLines)
    {
        if (string.IsNullOrWhiteSpace(key))
            return null;
        var path = GetStoryPath(key);
        try
        {
            if (!File.Exists(path))
                return null;
            var lines = File.ReadAllLines(path, Encoding.UTF8);
            if (lines.Length == 0)
                return null;
            var take = Math.Max(1, Math.Min(maxLines, lines.Length));
            return string.Join("\n", lines[^take..]);
        }
        catch
        {
            return null;
        }
    }

    private string GetStoryPath(string key) => Path.Combine(_directory, "_story_" + Sanitize(key) + ".txt");

    private string LastProcessPath => Path.Combine(_directory, "_last_process.txt");

    private string GetPath(string key)
    {
        var safe = Sanitize(key);
        return Path.Combine(_directory, safe + ".txt");
    }

    private static string Sanitize(string key)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sb = new StringBuilder(key.Length);
        foreach (var c in key)
            sb.Append(invalid.Contains(c) ? '_' : c);

        var result = sb.ToString().Trim();
        if (string.IsNullOrWhiteSpace(result))
            result = "_";
        if (result.Length > 120)
            result = result[..120];
        return result;
    }
}
