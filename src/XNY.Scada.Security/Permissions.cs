using System.Reflection;
using XNY.Scada.Domain;

namespace XNY.Scada.Security;

/// <summary>声明一个权限点。可标注在常量字段、类或方法上，启动时自动同步到数据库。</summary>
/// <param name="code">代码。</param>
[AttributeUsage(AttributeTargets.Field | AttributeTargets.Class | AttributeTargets.Method | AttributeTargets.Property, AllowMultiple = true)]
public sealed class PermissionAttribute(string code) : Attribute
{
    /// <summary>权限编码。</summary>
    public string Code { get; } = code;

    /// <summary>权限名称。</summary>
    public string Name { get; set; } = "";

    /// <summary>分类。</summary>
    public string Category { get; set; } = "";

    /// <summary>权限种类。</summary>
    public PermissionKind Kind { get; set; } = PermissionKind.Action;
}

/// <summary>权限定义。</summary>
/// <param name="Code">编码。</param>
/// <param name="Name">名称。</param>
/// <param name="Category">分类。</param>
/// <param name="Kind">种类。</param>
/// <param name="Sort">排序号。</param>
public sealed record PermissionDefinition(string Code, string Name, string Category, PermissionKind Kind, int Sort);

/// <summary>系统内置权限点</summary>
public static class Permissions
{
    /// <summary>菜单。</summary>
    [Permission(Menu.Dashboard, Name = "主监控", Category = "菜单", Kind = PermissionKind.Menu)]
    [Permission(Menu.Alarm, Name = "报警中心", Category = "菜单", Kind = PermissionKind.Menu)]
    [Permission(Menu.Device, Name = "设备配置", Category = "菜单", Kind = PermissionKind.Menu)]
    [Permission(Menu.Diagnostics, Name = "通讯诊断", Category = "菜单", Kind = PermissionKind.Menu)]
    [Permission(Menu.FlowDesigner, Name = "流程设计", Category = "菜单", Kind = PermissionKind.Menu)]
    [Permission(Menu.FlowMonitor, Name = "流程监控", Category = "菜单", Kind = PermissionKind.Menu)]
    [Permission(Menu.Recipe, Name = "配方与分档", Category = "菜单", Kind = PermissionKind.Menu)]
    [Permission(Menu.Production, Name = "生产数据", Category = "菜单", Kind = PermissionKind.Menu)]
    [Permission(Menu.Mes, Name = "MES 配置", Category = "菜单", Kind = PermissionKind.Menu)]
    [Permission(Menu.User, Name = "用户权限", Category = "菜单", Kind = PermissionKind.Menu)]
    [Permission(Menu.Audit, Name = "审计日志", Category = "菜单", Kind = PermissionKind.Menu)]
    [Permission(Menu.System, Name = "系统参数", Category = "菜单", Kind = PermissionKind.Menu)]
    public static class Menu
    {
        /// <summary>主监控。</summary>
        public const string Dashboard = "Menu.Dashboard";
        /// <summary>报警中心。</summary>
        public const string Alarm = "Menu.Alarm";
        /// <summary>设备配置。</summary>
        public const string Device = "Menu.Device";
        /// <summary>通讯诊断。</summary>
        public const string Diagnostics = "Menu.Diagnostics";
        /// <summary>流程设计。</summary>
        public const string FlowDesigner = "Menu.FlowDesigner";
        /// <summary>流程监控。</summary>
        public const string FlowMonitor = "Menu.FlowMonitor";
        /// <summary>配方与分档。</summary>
        public const string Recipe = "Menu.Recipe";
        /// <summary>生产数据。</summary>
        public const string Production = "Menu.Production";
        /// <summary>MES 配置。</summary>
        public const string Mes = "Menu.Mes";
        /// <summary>用户权限。</summary>
        public const string User = "Menu.User";
        /// <summary>审计日志。</summary>
        public const string Audit = "Menu.Audit";
        /// <summary>系统参数。</summary>
        public const string System = "Menu.System";
    }

    /// <summary>生产。</summary>
    [Permission(Production.Start, Name = "启动/停止生产", Category = "生产")]
    [Permission(Production.ResetCounter, Name = "清零计数", Category = "生产")]
    [Permission(Production.ManualTrigger, Name = "手动触发流程", Category = "生产")]
    [Permission(Production.Export, Name = "导出生产数据", Category = "生产")]
    [Permission(Production.TrayManual, Name = "托盘手动绑定/解绑", Category = "生产")]
    public static class Production
    {
        /// <summary>启动/停止生产。</summary>
        public const string Start = "Production.Start";
        /// <summary>清零计数。</summary>
        public const string ResetCounter = "Production.ResetCounter";
        /// <summary>手动触发流程。</summary>
        public const string ManualTrigger = "Production.ManualTrigger";
        /// <summary>导出生产数据。</summary>
        public const string Export = "Production.Export";
        /// <summary>托盘手动绑定/解绑。</summary>
        public const string TrayManual = "Production.TrayManual";
    }

    /// <summary>报警。</summary>
    [Permission(Alarm.Acknowledge, Name = "确认报警", Category = "报警")]
    [Permission(Alarm.Configure, Name = "配置报警", Category = "报警")]
    public static class Alarm
    {
        /// <summary>确认报警。</summary>
        public const string Acknowledge = "Alarm.Acknowledge";
        /// <summary>配置报警。</summary>
        public const string Configure = "Alarm.Configure";
    }

    /// <summary>设备。</summary>
    [Permission(Device.Edit, Name = "编辑设备与点位", Category = "设备")]
    [Permission(Device.ManualWrite, Name = "手动写 PLC", Category = "设备")]
    [Permission(Device.Reload, Name = "重载通讯", Category = "设备")]
    public static class Device
    {
        /// <summary>编辑设备与点位。</summary>
        public const string Edit = "Device.Edit";
        /// <summary>手动写 PLC。</summary>
        public const string ManualWrite = "Device.ManualWrite";
        /// <summary>重载通讯。</summary>
        public const string Reload = "Device.Reload";
    }

    /// <summary>流。</summary>
    [Permission(Flow.Edit, Name = "编辑流程草稿", Category = "流程")]
    [Permission(Flow.Publish, Name = "发布流程", Category = "流程")]
    [Permission(Flow.Script, Name = "编辑脚本节点", Category = "流程")]
    [Permission(Flow.Debug, Name = "调试/模拟运行", Category = "流程")]
    [Permission(Flow.Control, Name = "启停流程引擎", Category = "流程")]
    public static class Flow
    {
        /// <summary>编辑流程草稿。</summary>
        public const string Edit = "Flow.Edit";
        /// <summary>发布流程。</summary>
        public const string Publish = "Flow.Publish";
        /// <summary>编辑脚本节点。</summary>
        public const string Script = "Flow.Script";
        /// <summary>调试/模拟运行。</summary>
        public const string Debug = "Flow.Debug";
        /// <summary>启停流程引擎。</summary>
        public const string Control = "Flow.Control";
    }

    /// <summary>配方。</summary>
    [Permission(Recipe.Edit, Name = "编辑配方", Category = "配方")]
    [Permission(Recipe.Approve, Name = "审批配方", Category = "配方")]
    [Permission(Recipe.Switch, Name = "切换配方", Category = "配方")]
    public static class Recipe
    {
        /// <summary>编辑配方。</summary>
        public const string Edit = "Recipe.Edit";
        /// <summary>审批配方。</summary>
        public const string Approve = "Recipe.Approve";
        /// <summary>切换配方。</summary>
        public const string Switch = "Recipe.Switch";
    }

    /// <summary>MES。</summary>
    [Permission(Mes.Edit, Name = "编辑 MES 接口", Category = "MES")]
    [Permission(Mes.Test, Name = "测试 MES 接口", Category = "MES")]
    public static class Mes
    {
        /// <summary>编辑 MES 接口。</summary>
        public const string Edit = "Mes.Edit";
        /// <summary>测试 MES 接口。</summary>
        public const string Test = "Mes.Test";
    }

    /// <summary>当前用户。</summary>
    [Permission(User.Manage, Name = "管理用户", Category = "用户")]
    [Permission(User.ManageRole, Name = "管理角色与授权", Category = "用户")]
    [Permission(User.ResetPassword, Name = "重置密码", Category = "用户")]
    public static class User
    {
        /// <summary>管理用户。</summary>
        public const string Manage = "User.Manage";
        /// <summary>管理角色与授权。</summary>
        public const string ManageRole = "User.ManageRole";
        /// <summary>重置密码。</summary>
        public const string ResetPassword = "User.ResetPassword";
    }

    /// <summary>系统。</summary>
    [Permission(System.Edit, Name = "修改系统参数", Category = "系统")]
    [Permission(System.ImportTemplate, Name = "导入机型模板", Category = "系统")]
    public static class System
    {
        /// <summary>修改系统参数。</summary>
        public const string Edit = "System.Edit";
        /// <summary>导入机型模板。</summary>
        public const string ImportTemplate = "System.ImportTemplate";
    }
}

/// <summary>权限清单。</summary>
public static class PermissionCatalog
{
    /// <summary>扫描程序集中所有 <see cref="PermissionAttribute"/></summary>
    public static IReadOnlyList<PermissionDefinition> Discover(IEnumerable<Assembly> assemblies)
    {
        var list = new Dictionary<string, PermissionDefinition>(StringComparer.OrdinalIgnoreCase);
        var sort = 0;
        foreach (var asm in assemblies.Distinct())
        {
            Type[] types;
            try { types = asm.GetTypes(); }
            catch (ReflectionTypeLoadException ex) { types = ex.Types.Where(t => t != null).ToArray()!; }

            foreach (var t in types)
            {
                var members = new MemberInfo[] { t }.Concat(t.GetMembers(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly));
                foreach (var m in members)
                {
                    foreach (var a in m.GetCustomAttributes<PermissionAttribute>(false))
                    {
                        if (list.ContainsKey(a.Code)) continue;
                        list[a.Code] = new PermissionDefinition(a.Code, string.IsNullOrEmpty(a.Name) ? a.Code : a.Name,
                            string.IsNullOrEmpty(a.Category) ? "其他" : a.Category, a.Kind, sort++);
                    }
                }
            }
        }
        return list.Values.OrderBy(p => p.Sort).ToList();
    }
}
