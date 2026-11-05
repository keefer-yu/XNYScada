using System.Collections;
using XNY.Scada.Device;
using XNY.Scada.Domain.Entities;
using XNY.Scada.Presentation.ViewModels;
using XNY.Scada.WinForms.Mvvm;

namespace XNY.Scada.WinForms.Pages;

/// <summary>设备配置页面。</summary>
public sealed class DeviceConfigPage : UserControl
{
    /// <summary>创建设备配置页面。</summary>
    public DeviceConfigPage(DeviceConfigViewModel vm)
    {
        var devices = new ListBox { Dock = DockStyle.Fill, IntegralHeight = false, ItemHeight = 20 };
        Bind.List(devices, vm.Devices, d => $"{d.Name}    [{d.DriverKey}]", vm, nameof(vm.SelectedDevice));
        var pending = new Panel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(0xFF, 0xF7, 0xE6), Padding = new Padding(6), Height = 80 };
        pending.Controls.Add(Ui.Stack(Ui.Lbl("配置已修改，需应用后通讯才生效"), Ui.Btn("应用配置（重启通讯）", vm.ApplyCommand, ButtonKind.Primary)));
        Bind.Visible(pending, vm, nameof(vm.HasPendingChanges));
        var left = Ui.Rows(
            ("auto", Ui.Bar(Ui.Btn("新增", vm.NewDeviceCommand), Ui.Btn("删除", vm.DeleteDeviceCommand, ButtonKind.Danger))),
            ("*", devices),
            ("auto", pending));

        var options = Ui.Stack();
        var optionBindings = new List<IDisposable>();
        Bind.Observe(vm.DriverOptions, () =>
        {
            optionBindings.ForEach(b => b.Dispose());
            optionBindings.Clear();
            options.SuspendLayout();
            foreach (Control c in options.Controls) c.Dispose();
            options.Controls.Clear();
            options.RowStyles.Clear();
            options.RowCount = 0;
            foreach (var o in vm.DriverOptions)
            {
                Control editor;
                if (o.IsEnum)
                {
                    var combo = new ComboBox();
                    optionBindings.Add(Bind.Combo(combo, o.Choices, o, nameof(DriverOptionItem.Value)));
                    editor = combo;
                }
                else if (o.IsBool)
                {
                    var check = new CheckBox { AutoSize = true };
                    optionBindings.Add(Bind.Checked(check, o, nameof(DriverOptionItem.Value)));
                    editor = check;
                }
                else
                {
                    var text = new TextBox();
                    optionBindings.Add(Bind.TextBox(text, o, nameof(DriverOptionItem.Value)));
                    editor = text;
                }
                Ui.AddToStack(options, Ui.Form(120, (o.DisplayName, editor)));
            }
            options.ResumeLayout();
        });

        var help = new Label { AutoSize = true, MaximumSize = new Size(330, 0), ForeColor = Ui.Muted };
        Bind.Text(help, vm, nameof(vm.AddressHelp));
        var form = Ui.Stack(
            Ui.Title("设备参数"),
            Ui.Form(120,
                ("设备名称", Ui.Text(vm, nameof(vm.DeviceName))),
                ("驱动 / PLC 类型", Ui.Combo(vm.Drivers, vm, nameof(vm.DriverKey), 200,
                    d => $"{((DriverDescriptor)d).Vendor} · {((DriverDescriptor)d).DisplayName}", d => ((DriverDescriptor)d).Key)),
                ("启用", Ui.Check("", vm, nameof(vm.DeviceEnabled))),
                ("说明", Ui.Text(vm, nameof(vm.DeviceDescription)))),
            Ui.Title("通讯参数"),
            options,
            Ui.Bar(Ui.Btn("保存设备", vm.SaveDeviceCommand, ButtonKind.Primary), Ui.Btn("测试连接", vm.TestConnectionCommand)),
            Ui.Title("地址格式说明"),
            help);

        var groupsSource = new ObservableBindingList<TagGroupEntity>(vm.Groups);
        var groups = new BoundGrid<TagGroupEntity>(vm.Groups, true,
            new Col("ID", nameof(TagGroupEntity.Id), 50),
            Col.Fill("名称", nameof(TagGroupEntity.Name)) with { Editable = true },
            Col.Edit("扫描周期 ms", nameof(TagGroupEntity.ScanIntervalMs), 100));
        var tags = new BoundGrid<TagEntity>(vm.Tags, true,
                Col.Edit("名称", nameof(TagEntity.Name), 130),
                Col.Edit("地址", nameof(TagEntity.Address), 110),
                Col.Choice("类型", nameof(TagEntity.DataType), (IList)vm.DataTypes, 85),
                Col.Edit("长度", nameof(TagEntity.Length), 50),
                Col.Edit("系数", nameof(TagEntity.Scale), 55),
                Col.Edit("偏移", nameof(TagEntity.Offset), 55),
                Col.Choice("读写", nameof(TagEntity.Access), (IList)vm.AccessModes, 90),
                Col.Choice("扫描组", nameof(TagEntity.GroupId), groupsSource, 110) with
                {
                    ValueMember = nameof(TagGroupEntity.Id), DisplayMember = nameof(TagGroupEntity.Name)
                },
                Col.Fill("说明", nameof(TagEntity.Description)) with { Editable = true })
            .Selected(vm, nameof(vm.SelectedTag));
        var tagTab = Ui.Rows(
            ("auto", Ui.Bar(
                Ui.Btn("添加点位", vm.AddTagCommand),
                Ui.Btn("删除点位", vm.RemoveTagCommand),
                Ui.Btn("保存点位", vm.SaveTagsCommand, ButtonKind.Primary),
                Ui.Btn("导入 CSV", vm.ImportCsvCommand),
                Ui.Btn("导出 CSV", vm.ExportCsvCommand))),
            ("120", Ui.Cols(("*", groups), ("auto", Ui.Stack(Ui.Btn("新增扫描组", vm.AddGroupCommand), Ui.Btn("保存扫描组", vm.SaveGroupsCommand))))),
            ("*", tags));

        var alarms = new BoundGrid<AlarmDefinitionEntity>(vm.AlarmDefinitions, true,
                Col.Edit("点位", nameof(AlarmDefinitionEntity.TagName), 180),
                Col.Choice("类型", nameof(AlarmDefinitionEntity.Kind), (IList)vm.AlarmKinds, 80),
                Col.Edit("阈值", nameof(AlarmDefinitionEntity.Threshold), 70),
                Col.Choice("级别", nameof(AlarmDefinitionEntity.Level), (IList)vm.AlarmLevels, 90),
                Col.Fill("报警内容", nameof(AlarmDefinitionEntity.Message)) with { Editable = true },
                Col.Edit("启用", nameof(AlarmDefinitionEntity.Enabled), 50))
            .Selected(vm, nameof(vm.SelectedAlarm));
        var alarmTab = Ui.Rows(
            ("auto", Ui.Bar(
                Ui.Btn("添加", vm.AddAlarmCommand),
                Ui.Btn("删除", vm.RemoveAlarmCommand),
                Ui.Btn("保存报警定义", vm.SaveAlarmsCommand, ButtonKind.Primary),
                Ui.Lbl("点位填写全名：设备名.点位名；Bit 类型在值为 true 时报警", color: Ui.Muted))),
            ("*", alarms));

        Controls.Add(Ui.Cols(
            ("240", Ui.Card("设备列表", left)),
            ("360", Ui.Card("设备", Ui.Scroll(form))),
            ("*", Ui.Tabs(("点位", tagTab), ("报警定义", alarmTab)))));
    }
}

/// <summary>诊断页面。</summary>
public sealed class DiagnosticsPage : UserControl
{
    /// <summary>创建诊断页面。</summary>
    public DiagnosticsPage(DiagnosticsViewModel vm)
    {
        var devices = new BoundGrid<DeviceStatusItem>(vm.Devices, false,
                new Col("状态", nameof(DeviceStatusItem.StatusText), 60),
                new Col("设备", nameof(DeviceStatusItem.Name), 120),
                new Col("驱动", nameof(DeviceStatusItem.DriverKey), 120),
                new Col("读取次数", nameof(DeviceStatusItem.ReadCount), 90),
                new Col("错误次数", nameof(DeviceStatusItem.ErrorCount), 80),
                new Col("扫描耗时ms", nameof(DeviceStatusItem.LastScanMs), 90),
                new Col("上次连接", nameof(DeviceStatusItem.LastConnectedAt), 110, "MM-dd HH:mm:ss"),
                Col.Fill("最近错误", nameof(DeviceStatusItem.LastError)))
            .RowColor(d => !d.Enabled ? Color.Gray : d.Connected ? null : Ui.Danger);
        var tags = new BoundGrid<TagValueItem>(vm.FilteredTags, false,
                new Col("点位", nameof(TagValueItem.FullName), 220),
                new Col("地址", nameof(TagValueItem.Address), 100),
                new Col("类型", nameof(TagValueItem.DataType), 70),
                new Col("读写", nameof(TagValueItem.Access), 80),
                Col.Fill("值", nameof(TagValueItem.Value)),
                new Col("质量", nameof(TagValueItem.Quality), 70),
                new Col("时间", nameof(TagValueItem.Timestamp), 100, "HH:mm:ss.fff"))
            .RowColor(t => t.Quality == "Good" ? null : Ui.Danger)
            .Selected(vm, nameof(vm.SelectedTag));
        var selected = Ui.Lbl("", bold: true);
        Bind.Observe(vm, nameof(vm.SelectedTag), () => selected.Text = vm.SelectedTag?.FullName ?? "（未选择）");

        Controls.Add(Ui.Rows(
            ("220", Ui.Card("设备通讯状态", Ui.Rows(("auto", Ui.Bar(Ui.Btn("重新加载通讯", vm.ReloadCommand))), ("*", devices)))),
            ("*", Ui.Card("点位实时值", Ui.Rows(
                ("auto", Ui.Bar(
                    Ui.Lbl("过滤"), Ui.Text(vm, nameof(vm.Filter), 200),
                    Ui.Lbl("   选中点位"), selected,
                    Ui.Btn("直读", vm.ReadCommand),
                    Ui.Lbl("写入值"), Ui.Text(vm, nameof(vm.WriteValue), 120),
                    Ui.Btn("手动写入（签名）", vm.WriteCommand, ButtonKind.Danger))),
                ("*", tags))))));
    }
}

/// <summary>MES页面。</summary>
public sealed class MesPage : UserControl
{
    /// <summary>创建MES页面。</summary>
    public MesPage(MesViewModel vm)
    {
        var list = new ListBox { Dock = DockStyle.Fill, IntegralHeight = false, ItemHeight = 20 };
        Bind.List(list, vm.Interfaces, m => $"{m.Code}  {m.Name}", vm, nameof(vm.Selected));
        var left = Ui.Rows(
            ("auto", Ui.Bar(Ui.Btn("新增", vm.NewCommand), Ui.Btn("删除", vm.DeleteCommand, ButtonKind.Danger))),
            ("*", list));

        // 接口配置表单绑定到选中的实体，选中变化时重建
        var editorHost = new Panel { Dock = DockStyle.Fill };
        var editorBindings = new List<IDisposable>();
        Bind.Observe(vm, nameof(vm.Selected), () =>
        {
            editorBindings.ForEach(b => b.Dispose());
            editorBindings.Clear();
            foreach (Control c in editorHost.Controls) c.Dispose();
            editorHost.Controls.Clear();
            if (vm.Selected is { } m) editorHost.Controls.Add(BuildEditor(vm, m));
        });

        var test = Ui.Cols(
            ("*", Ui.Rows(("auto", Ui.Lbl("测试参数 JSON")), ("*", Ui.Code(vm, nameof(vm.TestArgs))), ("auto", Ui.Bar(Ui.Btn("发送测试", vm.TestCommand, ButtonKind.Primary))))),
            ("*", Ui.Rows(("auto", Ui.Lbl("测试结果")), ("*", Ui.Code(vm, nameof(vm.TestResult), readOnly: true)))));

        var logs = new BoundGrid<MesCallLogEntity>(vm.Logs, false,
                new Col("时间", nameof(MesCallLogEntity.CreatedAt), 130, "MM-dd HH:mm:ss.fff"),
                new Col("接口", nameof(MesCallLogEntity.InterfaceCode), 110),
                new Col("成功", nameof(MesCallLogEntity.Success), 45),
                new Col("耗时ms", nameof(MesCallLogEntity.ElapsedMs), 70),
                Col.Fill("错误", nameof(MesCallLogEntity.Error)))
            .RowColor(l => l.Success ? null : Ui.Danger)
            .Selected(vm, nameof(vm.SelectedLog));
        var request = Ui.Code(readOnly: true);
        var response = Ui.Code(readOnly: true);
        Bind.Observe(vm, nameof(vm.SelectedLog), () =>
        {
            request.Text = (vm.SelectedLog?.Request ?? "").ReplaceLineEndings("\r\n");
            response.Text = (vm.SelectedLog?.Response ?? "").ReplaceLineEndings("\r\n");
        });
        var logTab = Ui.Rows(
            ("auto", Ui.Bar(Ui.Date(vm, nameof(vm.LogFrom)), Ui.Date(vm, nameof(vm.LogTo)), Ui.Btn("查询", vm.QueryLogsCommand, ButtonKind.Primary))),
            ("*", logs),
            ("160", Ui.Cols(("*", request), ("*", response))));

        var outbox = new BoundGrid<MesOutboxEntity>(vm.Outbox, false,
            new Col("缓存时间", nameof(MesOutboxEntity.CreatedAt), 110, "MM-dd HH:mm:ss"),
            new Col("接口", nameof(MesOutboxEntity.InterfaceCode), 110),
            new Col("状态", nameof(MesOutboxEntity.Status), 70),
            new Col("重试", nameof(MesOutboxEntity.RetryCount), 50),
            new Col("发送时间", nameof(MesOutboxEntity.SentAt), 110, "MM-dd HH:mm:ss"),
            Col.Fill("错误", nameof(MesOutboxEntity.LastError)));
        var outboxTab = Ui.Rows(
            ("auto", Ui.Bar(
                Ui.Combo(vm.OutboxFilters, vm, nameof(vm.OutboxFilter), 100),
                Ui.Btn("立即补传", vm.FlushOutboxCommand, ButtonKind.Primary),
                Ui.Btn("失败项重新排队", vm.RequeueFailedCommand))),
            ("*", outbox));

        Controls.Add(Ui.Cols(
            ("260", Ui.Card("MES 接口", left)),
            ("*", Ui.Tabs(("接口配置", editorHost), ("接口测试", test), ("调用日志", logTab), ("离线补传", outboxTab)))));
    }

    /// <summary>MesInterfaceEntity 为普通实体（无属性通知），用包装对象做双向绑定</summary>
    private static Control BuildEditor(MesViewModel vm, MesInterfaceEntity m)
    {
        var e = new EntityProxy(m);
        var form = Ui.Stack(
            Ui.Lbl("编码（流程 MesCall 节点引用）"), Ui.Text(e, "Code"),
            Ui.Lbl("名称"), Ui.Text(e, "Name"),
            Ui.Lbl("URL"), Ui.Text(e, "Url"),
            Ui.Lbl("方法（SOAP 表示 WebService：POST text/xml，响应 XML 自动转为 JSON 路径）"), Ui.Combo(vm.Methods, e, "Method"),
            Ui.Lbl("请求头 JSON（SOAP 需添加 SOAPAction）"), Ui.CodeBox(e, "HeadersJson", 60),
            Ui.Lbl("响应映射 JSON：{ \"变量名\": \"data.result\" }"), Ui.CodeBox(e, "ResponseMapJson", 90),
            Ui.Lbl("成功判定表达式（基于映射字段，如 Code == 0；空则 HTTP 2xx 即成功）"), Ui.Text(e, "SuccessExpression"),
            Ui.Bar(Ui.Lbl("超时 ms"), Ui.Text(e, "TimeoutMs", 70), Ui.Lbl("重试次数"), Ui.Text(e, "RetryCount", 50)),
            Ui.Check("断网时缓存补传（仅用于上传类接口）", e, "CacheWhenOffline"),
            Ui.Check("启用（禁用时直接返回成功，用于离线生产）", e, "Enabled"),
            Ui.Btn("保存接口", vm.SaveCommand, ButtonKind.Primary));
        return Ui.Cols(
            ("*", Ui.Scroll(form)),
            ("*", Ui.Rows(("auto", Ui.Lbl("请求体模板（${变量名} 占位，JSON 自动转义 / SOAP 自动 XML 转义）")), ("*", Ui.Code(e, "RequestTemplate")))));
    }
}
