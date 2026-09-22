using System.Windows.Data;
using System.Windows.Markup;
using UACToolBox.Localization;
namespace UACToolBox.Config;

/// <summary>{loc:Loc key} → 绑定 Loc 索引器；切换语言时通过 Item[] 通知全量刷新。</summary>
public class LocExtension(string key) : MarkupExtension
{
    public override object ProvideValue(IServiceProvider serviceProvider) =>
        new Binding($"[{key}]") { Source = Loc.Instance, Mode = BindingMode.OneWay }.ProvideValue(serviceProvider);
}
