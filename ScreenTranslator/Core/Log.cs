using System.Text;

namespace ScreenTranslator.Core;

/// <summary>
/// 极简文件日志：把关键运行时状态追加到日志文件，便于远程排查（用户报现象时直接看日志）。
/// 线程安全；任何 I/O 异常都被吞掉，绝不影响主流程。
/// </summary>
public static class Log
{
    private static readonly object Gate = new();
    private static string _path = "logs/translator.log";

    /// <summary>日志文件路径；启动时按 config.LogPath 设置。</summary>
    public static string Path
    {
        get => _path;
        set => _path = string.IsNullOrWhiteSpace(value) ? _path : value;
    }

    /// <summary>写一条带时间戳的日志行（无条件）。</summary>
    public static void Write(string message)
    {
        try
        {
            var dir = System.IO.Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);
            lock (Gate)
            {
                File.AppendAllText(_path, $"[{DateTime.Now:HH:mm:ss.fff}] {message}{Environment.NewLine}", Encoding.UTF8);
            }
        }
        catch
        {
            // 日志失败不影响主流程
        }
    }
}
