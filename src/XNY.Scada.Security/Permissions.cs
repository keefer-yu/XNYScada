using System.Reflection;
using XNY.Scada.Domain;

namespace XNY.Scada.Security;

/// <summary>声明一个权限点。可标注在常量字段、类或方法上，启动时自动同步到数据库。</summary>
[AttributeUsage(AttributeTargets.Field | AttributeTargets.Class | AttributeTargets.Method | AttributeTargets.Property, AllowMultiple = true)]
public sealed class PermissionAttribute(string code) : Attribute
{
    public string Code { get; } = code;

    public string Name { get; set; } = "";

    public string Category { get; set; } = "";

    public PermissionKind Kind { get; set; } = PermissionKind.Action;
}

public sealed record PermissionDefinition(string Code, string Name, string Category, PermissionKind Kind, int Sort);

/// <summary>系统内置权限点</summary>
public static class Permissions
{
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
        public const string Dashboard = "Menu.Dashboard";
        public const string Alarm = "Menu.Alarm";
        public const string Device = "Menu.Device";
        public const string Diagnostics = "Menu.Diagnostics";
        public const string FlowDesigner = "Menu.FlowDesigner";
        public const string FlowMonitor = "Menu.FlowMonitor";
        public const string Recipe = "Menu.Recipe";
        public const string Production = "Menu.Production";
        public const string Mes = "Menu.Mes";
        public const string User = "Menu.User";
        public const string Audit = "Menu.Audit";
        public const string System = "Menu.System";
    }

    [Permission(Production.Start, Name = "启动/停止生产", Category = "生产")]
    [Permission(Production.ResetCounter, Name = "清零计数", Category = "生产")]
    [Permission(Production.ManualTrigger, Name = "手动触发流程", Category = "生产")]
    [Permission(Production.Export, Name = "导出生产数据", Category = "生产")]
    [Permission(Production.TrayManual, Name = "托盘手动绑定/解绑", Category = "生产")]
    public static class Production
    {
        public const string Start = "Production.Start";
        public const string ResetCounter = "Production.ResetCounter";
        public const string ManualTrigger = "Production.ManualTrigger";
        public const string Export = "Production.Export";
        public const string TrayManual = "Production.TrayManual";
    }

    [Permission(Alarm.Acknowledge, Name = "确认报警", Category = "报警")]
    [Permission(Alarm.Configure, Name = "配置报警", Category = "报警")]
    public static class Alarm
    {
        public const string Acknowledge = "Alarm.Acknowledge";
        public const string Configure = "Alarm.Configure";
    }

    [Permission(Device.Edit, Name = "编辑设备与点位", Category = "设备")]
    [Permission(Device.ManualWrite, Name = "手动写 PLC", Category = "设备")]
    [Permission(Device.Reload, Name = "重载通讯", Category = "设备")]
    public static class Device
    {
        public const string Edit = "Device.Edit";
        public const string ManualWrite = "Device.ManualWrite";
        public const string Reload = "Device.Reload";
    }

    [Permission(Flow.Edit, Name = "编辑流程草稿", Category = "流程")]
    [Permission(Flow.Publish, Name = "发布流程", Category = "流程")]
    [Permission(Flow.Script, Name = "编辑脚本节点", Category = "流程")]
    [Permission(Flow.Debug, Name = "调试/模拟运行", Category = "流程")]
    [Permission(Flow.Control, Name = "启停流程引擎", Category = "流程")]
    public static class Flow
    {
        public const string Edit = "Flow.Edit";
        public const string Publish = "Flow.Publish";
        public const string Script = "Flow.Script";
        public const string Debug = "Flow.Debug";
        public const string Control = "Flow.Control";
    }

    [Permission(Recipe.Edit, Name = "编辑配方", Category = "配方")]
    [Permission(Recipe.Approve, Name = "审批配方", Category = "配方")]
    [Permission(Recipe.Switch, Name = "切换配方", Category = "配方")]
    public static class Recipe
    {
        public const string Edit = "Recipe.Edit";
        public const string Approve = "Recipe.Approve";
        public const string Switch = "Recipe.Switch";
    }

    [Permission(Mes.Edit, Name = "编辑 MES 接口", Category = "MES")]
    [Permission(Mes.Test, Name = "测试 MES 接口", Category = "MES")]
    public static class Mes
    {
        public const string Edit = "Mes.Edit";
        public const string Test = "Mes.Test";
    }

    [Permission(User.Manage, Name = "管理用户", Category = "用户")]
    [Permission(User.ManageRole, Name = "管理角色与授权", Category = "用户")]
    [Permission(User.ResetPassword, Name = "重置密码", Category = "用户")]
    public static class User
    {
        public const string Manage = "User.Manage";
        public const string ManageRole = "User.ManageRole";
        public const string ResetPassword = "User.ResetPassword";
    }

    [Permission(System.Edit, Name = "修改系统参数", Category = "系统")]
    [Permission(System.ImportTemplate, Name = "导入机型模板", Category = "系统")]
    public static class System
    {
        public const string Edit = "System.Edit";
        public const string ImportTemplate = "System.ImportTemplate";
    }
}

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
