using System.Collections.ObjectModel;
using XNY.Scada.Application.Templates;
using XNY.Scada.Device;
using XNY.Scada.Domain.Entities;
using XNY.Scada.Flow.Nodes;
using XNY.Scada.Presentation.ViewModels;
using XNY.Scada.WinForms.Mvvm;

namespace XNY.Scada.WinForms.Pages;

public sealed class UserAdminPage : UserControl
{
    public UserAdminPage(UserAdminViewModel vm)
    {
        var users = new BoundGrid<UserRow>(vm.Users, false,
                new Col("账号", "User.UserName", 110),
                new Col("姓名", "User.DisplayName", 110),
                Col.Fill("角色", nameof(UserRow.RoleNames)),
                new Col("卡号", "User.CardNo", 100),
                new Col("状态", nameof(UserRow.StatusText), 60),
                new Col("最近登录", "User.LastLoginAt", 130, "yyyy-MM-dd HH:mm"),
                new Col("内置", "User.IsBuiltIn", 45))
            .RowColor(u => u.StatusText == "正常" ? null : Ui.Danger)
            .Selected(vm, nameof(vm.SelectedUser));
        var userEditor = Ui.Stack(
            Ui.Title("用户信息"),
            Ui.Lbl("账号"), Ui.Text(vm, nameof(vm.UserName)),
            Ui.Lbl("姓名"), Ui.Text(vm, nameof(vm.DisplayName)),
            Ui.Lbl("卡号（刷卡登录）"), Ui.Text(vm, nameof(vm.CardNo)),
            Ui.Lbl("新密码（新用户必填；留空不修改）"), Ui.Text(vm, nameof(vm.NewPassword)),
            Ui.Check("启用", vm, nameof(vm.UserEnabled)),
            Ui.Title("角色"),
            CheckList(vm.UserRoleItems),
            Ui.Btn("保存用户", vm.SaveUserCommand, ButtonKind.Primary));
        var editorCard = Ui.Card("", Ui.Scroll(userEditor));
        Bind.Visible(editorCard, vm, nameof(vm.SelectedUser));
        var userTab = Ui.Cols(
            ("*", Ui.Rows(
                ("auto", Ui.Bar(
                    Ui.Btn("新增用户", vm.NewUserCommand),
                    Ui.Btn("删除", vm.DeleteUserCommand, ButtonKind.Danger),
                    Ui.Btn("重置密码", vm.ResetPasswordCommand),
                    Ui.Btn("解锁", vm.UnlockCommand))),
                ("*", users))),
            ("360", editorCard));

        var roles = new BoundGrid<RoleEntity>(vm.Roles, false,
                new Col("编码", nameof(RoleEntity.Code), 100),
                Col.Fill("名称", nameof(RoleEntity.Name)),
                new Col("等级", nameof(RoleEntity.Level), 50),
                new Col("内置", nameof(RoleEntity.IsBuiltIn), 45))
            .Selected(vm, nameof(vm.SelectedRole));
        var roleForm = new Panel { Dock = DockStyle.Fill };
        var roleBindings = new List<IDisposable>();
        Bind.Observe(vm, nameof(vm.SelectedRole), () =>
        {
            foreach (Control c in roleForm.Controls) c.Dispose();
            roleForm.Controls.Clear();
            if (vm.SelectedRole == null) return;
            var e = new EntityProxy(vm.SelectedRole);
            roleForm.Controls.Add(Ui.Stack(
                Ui.Lbl("编码"), Ui.Text(e, nameof(RoleEntity.Code)),
                Ui.Lbl("名称"), Ui.Text(e, nameof(RoleEntity.Name)),
                Ui.Lbl("等级（数值越大权限越高，只能管理低于自身等级的角色）"), Ui.Text(e, nameof(RoleEntity.Level)),
                Ui.Lbl("说明"), Ui.Text(e, nameof(RoleEntity.Description))));
        });
        var roleList = Ui.Rows(
            ("auto", Ui.Bar(
                Ui.Btn("新增角色", vm.NewRoleCommand),
                Ui.Btn("保存角色", vm.SaveRoleCommand, ButtonKind.Primary),
                Ui.Btn("删除", vm.DeleteRoleCommand, ButtonKind.Danger))),
            ("*", roles),
            ("230", roleForm));

        var permissions = PermissionTree(vm.PermissionItems);
        var permissionCard = Ui.Card("功能权限", Ui.Rows(
            ("auto", Ui.Bar(
                Ui.Btn("全选（自身拥有）", vm.CheckAllCommand),
                Ui.Btn("全不选", vm.UncheckAllCommand),
                Ui.Btn("保存权限", vm.SaveRolePermissionsCommand, ButtonKind.Primary))),
            ("*", permissions)));
        var scopeCard = Ui.Card("数据权限（可操作设备）", Ui.Rows(
            ("auto", Ui.Lbl("全选或全不选表示不限制", color: Ui.Muted)),
            ("*", Ui.Scroll(CheckList(vm.DataScopeItems)))));
        var roleTab = Ui.Cols(("380", roleList), ("*", permissionCard), ("220", scopeCard));

        Controls.Add(Ui.Tabs(("用户", userTab), ("角色与权限", roleTab)));
    }

    /// <summary>勾选列表：集合变化时重建，勾选状态双向同步</summary>
    private static Control CheckList(ObservableCollection<CheckItem> items)
    {
        var panel = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, Dock = DockStyle.Top };
        var bindings = new List<IDisposable>();
        Bind.Observe(items, () =>
        {
            bindings.ForEach(b => b.Dispose());
            bindings.Clear();
            foreach (Control c in panel.Controls) c.Dispose();
            panel.Controls.Clear();
            foreach (var item in items)
            {
                var check = new CheckBox { Text = item.Text, AutoSize = true, Margin = new Padding(0, 2, 0, 2) };
                bindings.Add(Bind.Checked(check, item, nameof(CheckItem.IsChecked)));
                panel.Controls.Add(check);
            }
        });
        return panel;
    }

    /// <summary>功能权限树：按分组展示，勾选分组即勾选组内全部</summary>
    private static TreeView PermissionTree(ObservableCollection<CheckItem> items)
    {
        var tree = new TreeView { CheckBoxes = true, Dock = DockStyle.Fill, BorderStyle = BorderStyle.None, ItemHeight = 22 };
        var bindings = new List<IDisposable>();
        var syncing = false;
        Bind.Observe(items, () =>
        {
            bindings.ForEach(b => b.Dispose());
            bindings.Clear();
            syncing = true;
            tree.BeginUpdate();
            tree.Nodes.Clear();
            foreach (var group in items.GroupBy(i => i.Group))
            {
                var g = tree.Nodes.Add(group.Key);
                foreach (var item in group)
                {
                    var node = g.Nodes.Add(item.Text);
                    node.Tag = item;
                    node.Checked = item.IsChecked;
                    bindings.Add(Bind.Observe(item, nameof(CheckItem.IsChecked), () =>
                    {
                        if (node.Checked == item.IsChecked) return;
                        syncing = true;
                        node.Checked = item.IsChecked;
                        g.Checked = g.Nodes.Cast<TreeNode>().All(n => n.Checked);
                        syncing = false;
                    }, invokeNow: false));
                }
                g.Checked = g.Nodes.Cast<TreeNode>().All(n => n.Checked);
            }
            tree.ExpandAll();
            tree.EndUpdate();
            syncing = false;
        });
        tree.AfterCheck += (_, e) =>
        {
            if (syncing || e.Node == null) return;
            syncing = true;
            if (e.Node.Tag is CheckItem item)
            {
                item.IsChecked = e.Node.Checked;
                if (e.Node.Parent is { } parent) parent.Checked = parent.Nodes.Cast<TreeNode>().All(n => n.Checked);
            }
            else
            {
                foreach (TreeNode child in e.Node.Nodes)
                {
                    child.Checked = e.Node.Checked;
                    ((CheckItem)child.Tag!).IsChecked = e.Node.Checked;
                }
            }
            syncing = false;
        };
        return tree;
    }
}

public sealed class AuditPage : UserControl
{
    public AuditPage(AuditViewModel vm)
    {
        var logs = new BoundGrid<AuditLogEntity>(vm.Logs, false,
                new Col("时间", nameof(AuditLogEntity.CreatedAt), 150, "yyyy-MM-dd HH:mm:ss"),
                new Col("用户", nameof(AuditLogEntity.UserName), 90),
                new Col("动作", nameof(AuditLogEntity.Action), 120),
                new Col("对象", nameof(AuditLogEntity.Target), 220),
                new Col("签名人", nameof(AuditLogEntity.Signer), 80),
                new Col("原因", nameof(AuditLogEntity.Reason), 120),
                Col.Fill("新值", nameof(AuditLogEntity.NewValue)),
                new Col("机器", nameof(AuditLogEntity.Machine), 100))
            .Selected(vm, nameof(vm.Selected));
        var oldValue = Ui.Code(readOnly: true);
        var newValue = Ui.Code(readOnly: true);
        Bind.Observe(vm, nameof(vm.Selected), () =>
        {
            oldValue.Text = (vm.Selected?.OldValue ?? "").ReplaceLineEndings("\r\n");
            newValue.Text = (vm.Selected?.NewValue ?? "").ReplaceLineEndings("\r\n");
        });

        Controls.Add(Ui.Rows(
            ("auto", Ui.Bar(
                Ui.Lbl("从"), Ui.Date(vm, nameof(vm.From)),
                Ui.Lbl("到"), Ui.Date(vm, nameof(vm.To)),
                Ui.Lbl("用户"), Ui.Text(vm, nameof(vm.UserName), 100),
                Ui.Lbl("动作"), Ui.Combo(vm.Actions, vm, nameof(vm.Action), 140, a => a as string is { Length: > 0 } s ? s : "（全部）"),
                Ui.Lbl("关键字"), Ui.Text(vm, nameof(vm.Keyword), 150),
                Ui.Btn("查询", vm.QueryCommand, ButtonKind.Primary),
                Ui.Btn("导出 CSV", vm.ExportCommand))),
            ("*", Ui.Card("审计日志", logs)),
            ("150", Ui.Cols(("*", Ui.Card("原值", oldValue)), ("*", Ui.Card("新值", newValue))))));
    }
}

public sealed class SystemPage : UserControl
{
    public SystemPage(SystemViewModel vm)
    {
        var machine = Ui.Stack(
            Ui.Title("机台"),
            Ui.Form(140,
                ("机型", Ui.Combo(vm.MachineTypeChoices, vm, nameof(vm.MachineType), 200,
                    o => ((KeyValuePair<string, string>)o).Value, o => ((KeyValuePair<string, string>)o).Key)),
                ("机台名称", Ui.Text(vm, nameof(vm.MachineName))),
                ("工位", Ui.Text(vm, nameof(vm.Station)))),
            Ui.Title("主监控布局"),
            Ui.Bar(Ui.Check("显示托盘矩阵（组盘/拆盘）", vm, nameof(vm.ShowTray)),
                Ui.Lbl("行"), Ui.Text(vm, nameof(vm.TrayRows), 50), Ui.Lbl("列"), Ui.Text(vm, nameof(vm.TrayCols), 50)),
            Ui.Bar(Ui.Check("显示档位料箱（分档）", vm, nameof(vm.ShowBins)), Ui.Lbl("档位数"), Ui.Text(vm, nameof(vm.BinCount), 50)),
            Ui.Bar(Ui.Check("显示 NG 统计（NG 挑选）", vm, nameof(vm.ShowNg)), Ui.Lbl("NG 料盒数"), Ui.Text(vm, nameof(vm.NgBoxCount), 50)),
            Ui.Lbl("关键点位（每行一个全名，显示在主监控）"),
            Ui.CodeBox(vm, nameof(vm.KeyTags), 90),
            Ui.Lbl("手动流程按钮（每行一个流程编码）"),
            Ui.CodeBox(vm, nameof(vm.ManualFlows), 70),
            Ui.Btn("保存机台配置", vm.SaveLayoutCommand, ButtonKind.Primary));
        machine.MaximumSize = new Size(720, 0);

        var templates = new BoundGrid<TemplateInfo>(vm.Templates, false,
            new Col("机型", nameof(TemplateInfo.MachineType), 130),
            new Col("名称", nameof(TemplateInfo.Name), 200),
            Col.Fill("说明", nameof(TemplateInfo.Description)),
            Col.Button("导入", vm.ImportTemplateCommand, 70));
        var templateTab = Ui.Rows(
            ("auto", Ui.Bar(
                Ui.Check("导入时清空现有设备与点位", vm, nameof(vm.ReplaceDevices)),
                Ui.Btn("从文件导入…", vm.ImportTemplateFileCommand),
                Ui.Btn("导出当前配置为模板", vm.ExportTemplateCommand))),
            ("auto", Ui.Lbl("机型模板包含：系统参数、界面布局、设备与点位、报警定义、配方与分档规则、MES 接口、全部流程。新机台导入模板后仅需修改 PLC 地址与参数即可投产。", color: Ui.Muted)),
            ("*", templates));

        var parameters = new BoundGrid<SystemParamEntity>(vm.Params, false,
                new Col("分组", nameof(SystemParamEntity.Group), 90),
                new Col("键", nameof(SystemParamEntity.Key), 160),
                Col.Fill("值", nameof(SystemParamEntity.Value)),
                new Col("说明", nameof(SystemParamEntity.Description), 160))
            .Selected(vm, nameof(vm.SelectedParam));
        var paramEditor = new Panel { Dock = DockStyle.Fill };
        Bind.Observe(vm, nameof(vm.SelectedParam), () =>
        {
            foreach (Control c in paramEditor.Controls) c.Dispose();
            paramEditor.Controls.Clear();
            if (vm.SelectedParam == null) return;
            var e = new EntityProxy(vm.SelectedParam);
            e.PropertyChanged += (_, _) => parameters.Invalidate();
            paramEditor.Controls.Add(Ui.Stack(
                Ui.Lbl("键（流程中以 {Param:键} 引用）"), Ui.Text(e, nameof(SystemParamEntity.Key)),
                Ui.Lbl("分组"), Ui.Text(e, nameof(SystemParamEntity.Group)),
                Ui.Lbl("说明"), Ui.Text(e, nameof(SystemParamEntity.Description)),
                Ui.Lbl("值"), Ui.CodeBox(e, nameof(SystemParamEntity.Value), 200)));
        });
        var paramTab = Ui.Cols(
            ("*", parameters),
            ("360", Ui.Rows(
                ("auto", Ui.Bar(
                    Ui.Btn("新增", vm.AddParamCommand),
                    Ui.Btn("保存", vm.SaveParamCommand, ButtonKind.Primary),
                    Ui.Btn("删除", vm.DeleteParamCommand, ButtonKind.Danger))),
                ("*", paramEditor))));

        var drivers = new BoundGrid<DriverDescriptor>(new ObservableCollection<DriverDescriptor>(vm.Drivers), false,
            new Col("厂商", nameof(DriverDescriptor.Vendor), 80),
            new Col("驱动键", nameof(DriverDescriptor.Key), 130),
            Col.Fill("名称", nameof(DriverDescriptor.DisplayName)),
            Col.Fill("实现", "DriverType.FullName"));
        var nodes = new BoundGrid<NodeDescriptor>(new ObservableCollection<NodeDescriptor>(vm.NodeTypes), false,
            new Col("分类", nameof(NodeDescriptor.Category), 60),
            new Col("类型", nameof(NodeDescriptor.Type), 120),
            new Col("名称", nameof(NodeDescriptor.DisplayName), 110),
            Col.Fill("说明", nameof(NodeDescriptor.Description)));
        var pluginTab = Ui.Cols(
            ("*", Ui.Card("已加载 PLC 驱动（plugins 目录可扩展）", drivers)),
            ("*", Ui.Card("已加载流程节点（plugins 目录可扩展）", nodes)));

        Controls.Add(Ui.Tabs(
            ("机台与界面", Ui.Scroll(machine)),
            ("机型模板", templateTab),
            ("系统参数", paramTab),
            ("驱动与节点", pluginTab)));
    }
}
