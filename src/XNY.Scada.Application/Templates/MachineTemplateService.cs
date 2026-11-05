using Microsoft.Extensions.Logging;
using SqlSugar;
using XNY.Scada.Application.Devices;
using XNY.Scada.Application.Flows;
using XNY.Scada.Application.Mes;
using XNY.Scada.Application.Production;
using XNY.Scada.Application.Settings;
using XNY.Scada.Device;
using XNY.Scada.Domain;
using XNY.Scada.Domain.Entities;
using XNY.Scada.Flow.Runtime;
using XNY.Scada.Security;

namespace XNY.Scada.Application.Templates;

/// <summary>机型模板文件的摘要，供界面列出可选模板。</summary>
/// <param name="Path">路径。</param>
/// <param name="MachineType">机型编码。</param>
/// <param name="Name">名称。</param>
/// <param name="Description">说明。</param>
public sealed record TemplateInfo(string Path, string MachineType, string Name, string? Description);

/// <summary>一次模板导入的计数与警告。警告不中断导入。</summary>
/// <param name="Devices">设备数量。</param>
/// <param name="Tags">点位数量。</param>
/// <param name="Flows">流程数量。</param>
/// <param name="Recipes">配方数量。</param>
/// <param name="MesInterfaces">MES 接口数量。</param>
/// <param name="Warnings">导入过程中的警告。</param>
public sealed record TemplateImportResult(int Devices, int Tags, int Flows, int Recipes, int MesInterfaces, IReadOnlyList<string> Warnings);

/// <summary>从 configs/machine-templates 读取机型模板，并写入设备、流程、配方、MES 与界面布局。</summary>
/// <param name="db">数据库客户端。</param>
/// <param name="devices">设备。</param>
/// <param name="flows">流程。</param>
/// <param name="recipes">配方。</param>
/// <param name="mes">MES。</param>
/// <param name="parameters">参数。</param>
/// <param name="engine">引擎。</param>
/// <param name="permissions">权限。</param>
/// <param name="audit">审计。</param>
/// <param name="logger">日志记录器。</param>
public sealed class MachineTemplateService(
    ISqlSugarClient db,
    DeviceConfigService devices,
    FlowDefinitionService flows,
    RecipeService recipes,
    MesConfigService mes,
    SystemParamService parameters,
    IFlowEngine engine,
    IPermissionService permissions,
    IAuditService audit,
    ILogger<MachineTemplateService> logger)
{
    /// <summary>机型模板目录。</summary>
    public static string TemplateDirectory => Path.Combine(AppContext.BaseDirectory, "configs", "machine-templates");

    /// <summary>列出。</summary>
    public IReadOnlyList<TemplateInfo> List()
    {
        if (!Directory.Exists(TemplateDirectory)) return [];
        var result = new List<TemplateInfo>();
        foreach (var file in Directory.EnumerateFiles(TemplateDirectory, "*.json"))
        {
            try
            {
                var t = MachineTemplate.Load(file);
                result.Add(new TemplateInfo(file, t.MachineType, t.Name, t.Description));
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "模板解析失败 {File}", file);
            }
        }
        return result;
    }

    /// <summary>
    /// 导入机型模板。replaceDevices=true 时清空现有设备与点位；同编码的流程会生成新版本并发布，配方与 MES 接口同编码覆盖。
    /// </summary>
    public async Task<TemplateImportResult> ImportAsync(MachineTemplate template, bool replaceDevices, bool checkPermission = true)
    {
        if (checkPermission) permissions.Demand(Permissions.System.ImportTemplate);
        var warnings = new List<string>();

        if (replaceDevices)
        {
            await db.Deleteable<TagEntity>().Where(t => t.Id > 0).ExecuteCommandAsync();
            await db.Deleteable<TagGroupEntity>().Where(t => t.Id > 0).ExecuteCommandAsync();
            await db.Deleteable<DeviceEntity>().Where(t => t.Id > 0).ExecuteCommandAsync();
        }

        var tagCount = 0;
        foreach (var d in template.Devices)
        {
            var device = await db.Queryable<DeviceEntity>().FirstAsync(x => x.Name == d.Name) ?? new DeviceEntity { Name = d.Name };
            device.DriverKey = d.DriverKey;
            device.OptionsJson = Json.Serialize(d.Options);
            device.Description = d.Description;
            device.Enabled = true;
            await devices.SaveDeviceAsync(device, checkPermission: false);

            var groups = await devices.GetGroupsAsync(device.Id);
            var tags = new List<TagEntity>();
            foreach (var g in d.Groups)
            {
                var group = groups.FirstOrDefault(x => x.Name == g.Name) ?? new TagGroupEntity { DeviceId = device.Id, Name = g.Name };
                group.ScanIntervalMs = g.ScanIntervalMs;
                await devices.SaveGroupAsync(group, checkPermission: false);
                tags.AddRange(g.Tags.Select(t => new TagEntity
                {
                    GroupId = group.Id, Name = t.Name, Address = t.Address, DataType = t.DataType, Length = t.Length,
                    Scale = t.Scale, Offset = t.Offset, Access = t.Access, Description = t.Description
                }));
            }
            await devices.SaveTagsAsync(device.Id, tags, checkPermission: false);
            tagCount += tags.Count;
        }

        if (template.Alarms.Count > 0)
        {
            await devices.SaveAlarmDefinitionsAsync(template.Alarms.Select(a => new AlarmDefinitionEntity
            {
                TagName = a.TagName, Kind = a.Kind, Threshold = a.Threshold, Level = a.Level, Message = a.Message
            }).ToList(), checkPermission: false);
        }

        foreach (var m in template.MesInterfaces)
        {
            var existing = await db.Queryable<MesInterfaceEntity>().FirstAsync(x => x.Code == m.Code);
            m.Id = existing?.Id ?? 0;
            await mes.SaveAsync(m, checkPermission: false);
        }

        foreach (var r in template.Recipes)
        {
            var recipe = new RecipeEntity { Code = r.Code, Name = r.Name, ProductModel = r.ProductModel, ParamsJson = Json.Serialize(r.Params) };
            var rules = r.Rules.Select(x => new GradingRuleEntity
            {
                BinNo = x.BinNo, BinName = x.BinName, Priority = x.Priority, ConditionsJson = Json.Serialize(x.Conditions),
                Expression = x.Expression, BoxCapacity = x.BoxCapacity, IsNg = x.IsNg, NgCode = x.NgCode
            }).ToList();
            await recipes.ImportApprovedAsync(recipe, rules, r.Activate);
        }

        foreach (var f in template.Flows)
        {
            foreach (var issue in flows.Validate(f).Where(i => i.IsError)) warnings.Add($"{f.Code}: {issue}");
            await flows.ImportAsync(f, publish: true, template.MachineType);
        }

        await parameters.SetAsync("MachineType", template.MachineType, checkPermission: false);
        await parameters.SetAsync("LayoutJson", Json.Serialize(template.Layout), checkPermission: false);
        foreach (var (k, v) in template.Params) await parameters.SetAsync(k, v, checkPermission: false);

        await devices.ApplyAsync(checkPermission: false);
        if (engine.IsRunning) await engine.ReloadAllAsync();
        await audit.LogAsync(AuditActions.TemplateImport, $"template:{template.MachineType}", newValue: template.Name);
        logger.LogInformation("已导入机型模板 {Name}", template.Name);
        return new TemplateImportResult(template.Devices.Count, tagCount, template.Flows.Count, template.Recipes.Count, template.MesInterfaces.Count, warnings);
    }

    /// <summary>把当前配置导出为模板（便于复制到其他机台）</summary>
    public async Task<MachineTemplate> ExportAsync()
    {
        var t = new MachineTemplate
        {
            MachineType = parameters.MachineType,
            Name = parameters.Get("MachineName", parameters.MachineType),
            Layout = parameters.Layout
        };
        foreach (var d in await devices.GetDevicesAsync())
        {
            var td = new TemplateDevice { Name = d.Name, DriverKey = d.DriverKey, Description = d.Description, Options = Json.Deserialize<Dictionary<string, string>>(d.OptionsJson) ?? new() };
            var tags = await devices.GetTagsAsync(d.Id);
            foreach (var g in await devices.GetGroupsAsync(d.Id))
            {
                td.Groups.Add(new TemplateTagGroup
                {
                    Name = g.Name, ScanIntervalMs = g.ScanIntervalMs,
                    Tags = tags.Where(x => x.GroupId == g.Id).Select(x => new TemplateTag
                    {
                        Name = x.Name, Address = x.Address, DataType = x.DataType, Length = x.Length, Scale = x.Scale, Offset = x.Offset, Access = x.Access, Description = x.Description
                    }).ToList()
                });
            }
            t.Devices.Add(td);
        }
        t.Alarms = (await devices.GetAlarmDefinitionsAsync()).Select(a => new TemplateAlarm { TagName = a.TagName, Kind = a.Kind, Threshold = a.Threshold, Level = a.Level, Message = a.Message }).ToList();
        foreach (var r in await recipes.ListAsync())
        {
            t.Recipes.Add(new TemplateRecipe
            {
                Code = r.Code, Name = r.Name, ProductModel = r.ProductModel, Params = Json.ToDictionary(r.ParamsJson), Activate = r.IsActive,
                Rules = (await recipes.GetRulesAsync(r.Id)).Select(x => new TemplateGradingRule
                {
                    BinNo = x.BinNo, BinName = x.BinName, Priority = x.Priority, Expression = x.Expression, BoxCapacity = x.BoxCapacity, IsNg = x.IsNg, NgCode = x.NgCode,
                    Conditions = Json.Deserialize<List<GradingCondition>>(x.ConditionsJson) ?? []
                }).ToList()
            });
        }
        t.MesInterfaces = await mes.ListAsync();
        foreach (var p in await flows.GetPublishedAsync()) t.Flows.Add(p.Document);
        return t;
    }

    /// <summary>首次启动时若无任何设备，自动导入当前机型对应的模板</summary>
    public async Task EnsureInitializedAsync()
    {
        if (await db.Queryable<DeviceEntity>().AnyAsync()) return;
        var info = List().FirstOrDefault(t => t.MachineType == parameters.MachineType) ?? List().FirstOrDefault();
        if (info == null) return;
        logger.LogInformation("首次启动，自动导入模板 {Name}", info.Name);
        await ImportAsync(MachineTemplate.Load(info.Path), replaceDevices: true, checkPermission: false);
    }
}
