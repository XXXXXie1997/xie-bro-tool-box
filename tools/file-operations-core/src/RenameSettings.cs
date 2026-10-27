using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

namespace XieBox.FileOperations;

public sealed class NamePart(string kind, string value = "") : INotifyPropertyChanged
{
    public string Kind { get; } = kind;
    public string Label => Kind switch { "text" => "自定义文字", "number" => "流水号", "date" => "日期", "created" => "创建日期", "modified" => "最后修改日期", _ => "原文件名" };
    private string _dateSource = "current";
    public string DateSource { get => _dateSource; set => Set(ref _dateSource, value); }
    public string NumberPreview => long.TryParse(Start, out var n) && int.TryParse(Digits, out var d) && d is >= 1 and <= 12 ? n.ToString("D" + d, CultureInfo.InvariantCulture) : "设置流水号";
    private string _value = value, _start = "1", _step = "1", _digits = "4";
    public string Start { get => _start; set => Set(ref _start, value); }
    public string Step { get => _step; set => Set(ref _step, value); }
    public string Digits { get => _digits; set => Set(ref _digits, value); }
    private void Set(ref string field, string value, [CallerMemberName] string? name = null) { if (field == value) return; field = value; PropertyChanged?.Invoke(this, new(name)); PropertyChanged?.Invoke(this, new(nameof(NumberPreview))); }
    public string Value { get => _value; set { if (_value == value) return; _value = value; PropertyChanged?.Invoke(this, new(nameof(Value))); } }
    public event PropertyChangedEventHandler? PropertyChanged;
}
public sealed record DateChoice(string Label, string Format);
public sealed class RenameSettings : INotifyPropertyChanged
{
    public static IReadOnlyList<DateChoice> Dates { get; } = [new("年 · 2026", "yyyy"), new("年月 · 202610", "yyyyMM"), new("年月日 · 20261015", "yyyyMMdd"), new("年月日时 · 2026101519", "yyyyMMddHH"), new("年月日时分 · 202610151930", "yyyyMMddHHmm"), new("年月日时分秒 · 20261015193045", "yyyyMMddHHmmss"), new("时间戳 · 秒", "unix"), new("时间戳 · 毫秒", "unix-ms")];
    public ObservableCollection<NamePart> Parts { get; } = [];
    public event PropertyChangedEventHandler? PropertyChanged;
    public event Action? Changed;
    private bool _regex, _append;
    private int _mode;
    private string _baseName = "文件", _addedText = "";
    public NamePart UniformNumber { get; } = new("number");
    public static IReadOnlyList<DateChoice> Sources { get; } = [new("当前时间", "current"), new("创建时间", "created"), new("修改时间", "modified")];
    public int Mode { get => _mode; set => Set(ref _mode, value); }
    public string BaseName { get => _baseName; set => Set(ref _baseName, value); }
    public string AddedText { get => _addedText; set => Set(ref _addedText, value); }
    public bool Append { get => _append; set => Set(ref _append, value); }
    private string _find = "", _replace = "";
    public bool Combine { get => Mode == 3; set => Mode = value ? 3 : 1; }
    public bool UseRegex { get => _regex; set => Set(ref _regex, value); }
    public string Find { get => _find; set => Set(ref _find, value); }
    public string Replacement { get => _replace; set => Set(ref _replace, value); }
    public string ModeLabel => Mode switch { 0 => "统一名称＋编号", 1 => UseRegex ? "正则查找替换" : "查找替换", 2 => "添加文字", _ => "自定义组合" };
    public string Example
    {
        get { try { var now = DateTimeOffset.Now; return "照片_旅行.jpg → " + Capture(now)("照片_旅行.jpg", 1, now.AddYears(-1), now.AddDays(-1)); } catch (IOException e) { return e.Message; } }
    }
    public RenameSettings()
    {
        UniformNumber.PropertyChanged += PartChanged;
        Parts.CollectionChanged += (_, e) => { if (e.OldItems is not null) foreach (NamePart part in e.OldItems) part.PropertyChanged -= PartChanged; if (e.NewItems is not null) foreach (NamePart part in e.NewItems) part.PropertyChanged += PartChanged; Update(); };
        Parts.Add(new("text", "文件")); Parts.Add(new("number"));
    }
    private void PartChanged(object? sender, PropertyChangedEventArgs e) => Update();
    private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null) { if (EqualityComparer<T>.Default.Equals(field, value)) return; field = value; PropertyChanged?.Invoke(this, new(name)); Update(); }
    private void Update() { PropertyChanged?.Invoke(this, new(nameof(Example))); PropertyChanged?.Invoke(this, new(nameof(ModeLabel))); Changed?.Invoke(); }
    public void Move(NamePart part, int offset) { var i = Parts.IndexOf(part); if (i >= 0 && i + offset >= 0 && i + offset < Parts.Count) Parts.Move(i, i + offset); }
    private static string FormatDate(DateTimeOffset date, string format) => format switch
    {
        "unix" => date.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture),
        "unix-ms" => date.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture),
        _ when Dates.Any(d => d.Format == format) => date.ToLocalTime().ToString(format, CultureInfo.InvariantCulture),
        _ => throw new IOException("请选择有效日期格式。")
    };
    public Func<string, int, DateTimeOffset, DateTimeOffset, string> Capture(DateTimeOffset date)
    {
        var mode = Mode; var combine = mode != 1; var find = Find; var replacement = Replacement;
        IEnumerable<NamePart> selected = mode switch
        {
            0 => new[] { new NamePart("text", BaseName), UniformNumber },
            2 => Append ? new[] { new NamePart("original"), new NamePart("text", AddedText) } : new[] { new NamePart("text", AddedText), new NamePart("original") },
            _ => Parts
        };
        var parts = selected.Select((p, position) =>
        {
            long start = 1, step = 1; int digits = 4;
            if (combine && p.Kind == "number" && (!long.TryParse(p.Start, out start) || start < 0 || !long.TryParse(p.Step, out step) || step < 1 || !int.TryParse(p.Digits, out digits) || digits is < 1 or > 12))
                throw new IOException($"第 {position + 1} 个片段的流水号设置无效：起始值不小于 0，步长不小于 1，位数为 1–12。");
            return (Kind: p.Kind == "date" && p.DateSource != "current" ? p.DateSource : p.Kind, p.Value, Start: start, Step: step, Digits: digits);
        }).ToArray();
        if (combine && parts.Length == 0) throw new IOException("请至少添加一个文件名片段。");
        Regex? regex = null;
        if (!combine && UseRegex && find.Length > 0)
        {
            try { regex = new Regex(find, RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100)); }
            catch (ArgumentException) { throw new IOException("正则表达式无效，请检查查找规则。"); }
        }
        return (fileName, index, created, modified) =>
        {
            try
            {
                var original = Path.GetFileNameWithoutExtension(fileName);
                var stem = combine ? string.Concat(parts.Select(p => p.Kind switch
                {
                    "text" => p.Value, "original" => original,
                    "number" => checked(p.Start + (index - 1L) * p.Step).ToString("D" + p.Digits, CultureInfo.InvariantCulture),
                    "date" or "created" or "modified" => FormatDate(p.Kind == "created" ? created : p.Kind == "modified" ? modified : date, p.Value),
                    _ => throw new IOException("文件名片段无效，请重新编辑。")
                })) : find.Length == 0 ? original : regex is null ? original.Replace(find, replacement, StringComparison.Ordinal) : regex.Replace(original, replacement);
                var name = stem + Path.GetExtension(fileName);
                if (string.IsNullOrWhiteSpace(stem) || !FilePlan.ValidName(name) || stem.EndsWith('.') || stem.EndsWith(' ')) throw new IOException("生成的文件名无效，请检查空名称、长度或特殊字符。");
                return name;
            }
            catch (RegexMatchTimeoutException) { throw new IOException("正则匹配超时，请简化表达式。"); }
            catch (OverflowException) { throw new IOException("流水号超出范围，请减小起始值或步长。"); }
            catch (ArgumentException) { throw new IOException("替换规则无效，请检查正则分组与替换内容。"); }
        };
    }
}

