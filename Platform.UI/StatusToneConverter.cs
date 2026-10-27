using System.Globalization;
using System.Windows.Data;
namespace XieBox.Platform.UI;

// Shared presentation for legacy status strings across the independent tools.
public sealed class StatusToneConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    => Classify(value as string ?? "");
    public static string Classify(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "Info";
        if (new[] { "请选择", "请先", "请缩小", "请修改", "请重新", "请稍后", "禁止修改", "不支持设备路径", "路径格式不支持", "规则已调整", "未允许", "已取消", "已停止", "未执行操作" }.Any(text.Contains)) return "Warning";
        if (new[] { "未能", "无法", "失败", "读取未完成", "错误：", "错误:", "异常：", "异常:", "不存在", "无效", "权限不足", "没有权限", "拒绝访问", "不可用" }.Any(text.Contains)) return "Error";
        if (new[] { "警告", "需要管理员权限", "请重试", "请检查" }.Any(text.Contains)) return "Warning";
        return "Info";
    }
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}
