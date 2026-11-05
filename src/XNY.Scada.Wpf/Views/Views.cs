using System.Windows.Data;
using XNY.Scada.Presentation.ViewModels;

namespace XNY.Scada.Wpf.Views;

public partial class DashboardView
{
    public DashboardView() => InitializeComponent();
}

public partial class AlarmView
{
    public AlarmView() => InitializeComponent();
}

public partial class ProductionView
{
    public ProductionView() => InitializeComponent();
}

public partial class RecipeView
{
    public RecipeView() => InitializeComponent();
}

public partial class FlowMonitorView
{
    public FlowMonitorView() => InitializeComponent();
}

public partial class DeviceConfigView
{
    public DeviceConfigView() => InitializeComponent();
}

public partial class DiagnosticsView
{
    public DiagnosticsView() => InitializeComponent();
}

public partial class MesView
{
    public MesView() => InitializeComponent();
}

public partial class UserAdminView
{
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

public partial class AuditView
{
    public AuditView() => InitializeComponent();
}

public partial class SystemView
{
    public SystemView() => InitializeComponent();
}
