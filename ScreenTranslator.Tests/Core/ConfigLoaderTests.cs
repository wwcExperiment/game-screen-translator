using ScreenTranslator.Core;

namespace ScreenTranslator.Tests.Core;

public sealed class ConfigLoaderTests
{
    [Fact]
    public void Load_missing_file_returns_defaults()
    {
        var cfg = ConfigLoader.Load(Path.Combine(Path.GetTempPath(), "screen_translator_no_such.json"));
        Assert.Equal(150, cfg.PollIntervalMs);
        Assert.Equal(300, cfg.DebounceMs);
        Assert.Equal(1000, cfg.MaxWaitMs);
        Assert.Equal(30000, cfg.MaxWaitCeilingMs);
        Assert.Equal(0.04, cfg.ChangeFraction);
        Assert.Equal(0.5, cfg.CancelChangeFraction);
        Assert.Equal(8, cfg.BlockSize);
        Assert.Equal(64, cfg.SignatureWidth);
        Assert.Equal(64, cfg.SignatureHeight);
    }

    [Fact]
    public void Load_reads_values_case_insensitively()
    {
        var path = Path.Combine(Path.GetTempPath(), $"st_{Guid.NewGuid():N}.json");
        File.WriteAllText(path, """{"debounceMs": 800, "model": "qwen-vl", "endpointUrl": "http://x/v1/chat/completions"}""");
        try
        {
            var cfg = ConfigLoader.Load(path);
            Assert.Equal(800, cfg.DebounceMs);
            Assert.Equal("qwen-vl", cfg.Model);
            Assert.Equal("http://x/v1/chat/completions", cfg.EndpointUrl);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
