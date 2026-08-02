using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using KotobaSenpai.Core.Settings;

namespace KotobaSenpai.App.Settings;

/// <summary>
/// <see cref="ISettingsService"/> 文件实现：作为 <c>%LocalAppData%/KotobaSenpai/settings.json</c> 的唯一归属。
/// 单例、懒加载到内存 <see cref="JsonObject"/> + 写穿（write-through）；保留未知字段
/// （<c>Language</c>/<c>Theme</c>/<c>MinimumLogLevel</c> 共存互不覆盖）；文件缺失或损坏视为空对象，不抛异常。
/// 所有读写经内部 <see cref="lock"/> 串行化。取代此前散落在 <c>LocalAppDataSettingsFile</c> 与
/// <c>LogConfiguration</c> 中的各自文件读取逻辑。
/// </summary>
public sealed class SettingsService : ISettingsService
{
    private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };

    private readonly string _filePath;
    private readonly object _gate = new();
    private JsonObject? _settings;
    private bool _loaded;

    public static string DefaultFilePath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "KotobaSenpai",
        "settings.json");

    /// <summary>使用真实的 <see cref="DefaultFilePath"/>。</summary>
    public SettingsService() : this(DefaultFilePath) { }

    /// <summary><paramref name="filePath"/> 注入便于测试指向临时文件。</summary>
    public SettingsService(string filePath) => _filePath = filePath;

    /// <inheritdoc />
    public string? GetValue(string key)
    {
        lock (_gate)
        {
            EnsureLoaded();
            if (!_settings!.TryGetPropertyValue(key, out JsonNode? node) || node is null)
                return null;

            // 值非字符串节点时（如数字/对象）回退 null，与既有偏好存储的容错语义一致。
            try
            {
                return node.GetValue<string>();
            }
            catch (InvalidOperationException)
            {
                return null;
            }
        }
    }

    /// <inheritdoc />
    public void SetValue(string key, string? value)
    {
        lock (_gate)
        {
            EnsureLoaded();
            if (value is null)
                _settings!.Remove(key);
            else
                _settings![key] = value;
            Save();
        }
    }

    /// <summary>首次访问时懒加载文件；缺失或损坏视为空对象。</summary>
    private void EnsureLoaded()
    {
        if (_loaded)
            return;

        _loaded = true;
        _settings = LoadOrEmpty();
    }

    private JsonObject LoadOrEmpty()
    {
        if (!File.Exists(_filePath))
            return new JsonObject();

        try
        {
            if (JsonNode.Parse(File.ReadAllText(_filePath)) is JsonObject obj)
                return obj;
        }
        catch (IOException)
        {
        }
        catch (JsonException)
        {
        }

        return new JsonObject();
    }

    /// <summary>写穿：序列化整个内存对象（保留未知字段），含目录自动创建。</summary>
    private void Save()
    {
        var directory = Path.GetDirectoryName(_filePath);
        if (directory is not null)
            Directory.CreateDirectory(directory);

        File.WriteAllText(_filePath, _settings!.ToJsonString(SerializerOptions));
    }
}
