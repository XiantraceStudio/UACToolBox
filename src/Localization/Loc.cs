using System.ComponentModel;
using System.IO;
using System.Text.Json;
namespace UACToolBox.Localization;

/// <summary>
/// JSON 资源加载器：内嵌语言表为基础，用户目录 languages\*.json 可追加语言包；
/// 解析顺序 精确文化 → 父文化 → zh-CN；缺失键返回键名本身（开发可见）。
/// 语言偏好保存在 %APPDATA%\XianTrace\UACToolBox\settings.json，所有进程（含提权
/// 子进程与执行端）启动时读取，保证跨进程文化一致。
/// </summary>
public sealed class Loc : INotifyPropertyChanged
{
    public const string FallbackCulture = "zh-CN";
    const string Prefix = "strings-";
    public static string LanguagesDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "XianTrace", "UACToolBox", "languages");
    public static string SettingsPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "XianTrace", "UACToolBox", "settings.json");

    static readonly object gate = new();
    static readonly Dictionary<string, Dictionary<string, string>> tables = new(StringComparer.OrdinalIgnoreCase);
    static string current = FallbackCulture;
    static bool loaded;

    public static Loc Instance { get; } = new();
    public event PropertyChangedEventHandler? PropertyChanged;
    Loc() { }

    public static event Action? CultureChanged;

    /// <summary>当前文化的可显示名称；语言下拉与日志展示用。</summary>
    public static string Current => current;
    public string this[string key] => T(key);

    /// <summary>所有可用文化（内嵌 + 外部语言包），按名称排序；至少包含回退语言。</summary>
    public static IReadOnlyList<string> AvailableCultures
    {
        get { Load(); lock (gate) return tables.Keys.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray(); }
    }

    public static string T(string key, params object?[] args)
    {
        Load();
        string text = Resolve(key);
        return args.Length == 0 ? text : string.Format(System.Globalization.CultureInfo.CurrentCulture, text, args);
    }
    static string Resolve(string key)
    {
        lock (gate)
        {
            foreach (var name in Chain(current))
                if (tables.TryGetValue(name, out var table) && table.TryGetValue(key, out var value))
                    return value;
            foreach (var name in Chain(FallbackCulture))
                if (tables.TryGetValue(name, out var table) && table.TryGetValue(key, out var value))
                    return value;
        }
        return key;
    }
    static IEnumerable<string> Chain(string culture)
    {
        yield return culture;
        var parents = new List<string>();
        try
        {
            var parent = System.Globalization.CultureInfo.GetCultureInfo(culture).Parent;
            while (!string.IsNullOrEmpty(parent.Name)) { parents.Add(parent.Name); parent = parent.Parent; }
        }
        catch (System.Globalization.CultureNotFoundException) { }
        foreach (var name in parents) yield return name;
    }

    /// <summary>切换当前文化。persist=false 供测试固定文化而不落盘。</summary>
    public static void SetCulture(string culture, bool persist = true)
    {
        Load();
        if (string.Equals(current, culture, StringComparison.OrdinalIgnoreCase)) { if (persist) Save(culture); return; }
        lock (gate) current = culture;
        if (persist) Save(culture);
        CultureChanged?.Invoke();
        Instance.PropertyChanged?.Invoke(Instance, new PropertyChangedEventArgs("Item[]"));
    }

    /// <summary>测试钩子：丢弃缓存，按当前磁盘与内嵌资源重新加载（不读取语言偏好以外的状态）。</summary>
    public static void Reload()
    {
        lock (gate) { loaded = false; tables.Clear(); current = FallbackCulture; }
        _ = AvailableCultures; // 触发重载
    }

    static void Load()
    {
        if (loaded) return;
        lock (gate)
        {
            if (loaded) return;
            foreach (var name in typeof(Loc).Assembly.GetManifestResourceNames())
            {
                if (!name.StartsWith(Prefix, StringComparison.Ordinal) || !name.EndsWith(".json", StringComparison.Ordinal)) continue;
                var culture = name[Prefix.Length..^".json".Length];
                using var stream = typeof(Loc).Assembly.GetManifestResourceStream(name);
                if (stream is null) continue;
                using var reader = new StreamReader(stream);
                tables[culture] = Parse(reader.ReadToEnd());
            }
            try
            {
                if (Directory.Exists(LanguagesDirectory))
                    foreach (var file in Directory.EnumerateFiles(LanguagesDirectory, "*.json"))
                    {
                        var culture = Path.GetFileNameWithoutExtension(file);
                        try { _ = System.Globalization.CultureInfo.GetCultureInfo(culture); }
                        catch (System.Globalization.CultureNotFoundException) { continue; }
                        tables[culture] = Parse(File.ReadAllText(file));
                    }
            }
            catch (IOException) { }
            try
            {
                if (File.Exists(SettingsPath))
                {
                    using var document = JsonDocument.Parse(File.ReadAllText(SettingsPath));
                    if (document.RootElement.TryGetProperty("language", out var value) && value.GetString() is string saved && tables.ContainsKey(saved))
                        current = saved;
                }
            }
            catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { }
            loaded = true;
        }
    }
    static Dictionary<string, string> Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var entry in document.RootElement.EnumerateObject())
            result[entry.Name] = entry.Value.GetString() ?? "";
        return result;
    }
    static void Save(string culture)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
            File.WriteAllText(SettingsPath, JsonSerializer.Serialize(new { language = culture }, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
