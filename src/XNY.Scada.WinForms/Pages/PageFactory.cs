using XNY.Scada.Domain;
using XNY.Scada.Presentation.ViewModels;
using XNY.Scada.WinForms.Mvvm;

namespace XNY.Scada.WinForms.Pages;

public static class PageFactory
{
    public static Control Create(ViewModelBase vm) => vm switch
    {
        DashboardViewModel d => new DashboardPage(d),
        AlarmViewModel a => new AlarmPage(a),
        ProductionViewModel p => new ProductionPage(p),
        RecipeViewModel r => new RecipePage(r),
        FlowMonitorViewModel m => new FlowMonitorPage(m),
        FlowDesignerViewModel f => new FlowDesignerPage(f),
        DeviceConfigViewModel c => new DeviceConfigPage(c),
        DiagnosticsViewModel g => new DiagnosticsPage(g),
        MesViewModel s => new MesPage(s),
        UserAdminViewModel u => new UserAdminPage(u),
        AuditViewModel t => new AuditPage(t),
        SystemViewModel y => new SystemPage(y),
        _ => new Label { Text = "未实现的页面：" + vm.Title, AutoSize = true }
    };
}

internal static class Colors
{
    public static Color? Level(AlarmLevel level) => level switch
    {
        AlarmLevel.Critical or AlarmLevel.Error => Ui.Danger,
        AlarmLevel.Warning => Ui.Warn,
        _ => null
    };

    public static Color? Status(object? status) => status?.ToString() switch
    {
        "Failed" or "TimedOut" => Ui.Danger,
        "Cancelled" => Ui.Warn,
        "Running" => Ui.Primary,
        _ => null
    };
}
