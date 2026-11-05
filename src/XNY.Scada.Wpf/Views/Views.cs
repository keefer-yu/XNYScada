using System.Windows.Data;
using XNY.Scada.Presentation.ViewModels;

namespace XNY.Scada.Wpf.Views;

/// <summary>看板视图。</summary>
public partial class DashboardView
{
    /// <summary>创建看板视图。</summary>
    public DashboardView() => InitializeComponent();
}

/// <summary>报警视图。</summary>
public partial class AlarmView
{
    /// <summary>创建报警视图。</summary>
    public AlarmView() => InitializeComponent();
}

/// <summary>生产视图。</summary>
public partial class ProductionView
{
    /// <summary>创建生产视图。</summary>
    public ProductionView() => InitializeComponent();
}

/// <summary>配方视图。</summary>
public partial class RecipeView
{
    /// <summary>创建配方视图。</summary>
    public RecipeView() => InitializeComponent();
}

/// <summary>流程监控视图。</summary>
public partial class FlowMonitorView
{
    /// <summary>创建流程监控视图。</summary>
    public FlowMonitorView() => InitializeComponent();
}

/// <summary>设备配置视图。</summary>
public partial class DeviceConfigView
{
    /// <summary>创建设备配置视图。</summary>
    public DeviceConfigView() => InitializeComponent();
}

/// <summary>诊断视图。</summary>
public partial class DiagnosticsView
{
    /// <summary>创建诊断视图。</summary>
    public DiagnosticsView() => InitializeComponent();
}

/// <summary>MES视图。</summary>
public partial class MesView
{
    /// <summary>创建MES视图。</summary>
    public MesView() => InitializeComponent();
}

/// <summary>用户管理员视图。</summary>
public partial class UserAdminView
{
    /// <summary>创建用户管理员视图。</summary>
    public UserAdminView()
    {
        InitializeComponent();
        DataContextChanged += (_, e) =>
        {
            if (e.NewValue is not UserAdminViewModel vm) return;
            var view = CollectionViewSource.GetDefaultView(vm.PermissionItems);
            if (view.GroupDescriptions.Count == 0) view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(CheckItem.Group)));
        };
    }
}

/// <summary>审计视图。</summary>
public partial class AuditView
{
    /// <summary>创建审计视图。</summary>
    public AuditView() => InitializeComponent();
}

/// <summary>系统视图。</summary>
public partial class SystemView
{
    /// <summary>创建系统视图。</summary>
    public SystemView() => InitializeComponent();
}
