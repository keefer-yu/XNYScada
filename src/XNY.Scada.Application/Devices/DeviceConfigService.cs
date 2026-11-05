using System.Globalization;
using System.Text;
using SqlSugar;
using XNY.Scada.Device;
using XNY.Scada.Device.Runtime;
using XNY.Scada.Domain;
using XNY.Scada.Domain.Entities;
using XNY.Scada.Security;

namespace XNY.Scada.Application.Devices;

/// <summary>设备、点位组、点位、报警定义的配置管理；修改后需调用 <see cref="ApplyAsync"/> 重载通讯</summary>
/// <param name="db">数据库客户端。</param>
/// <param name="deviceManager">设备管理器。</param>
/// <param name="tags">点位。</param>
/// <param name="alarmService">报警服务。</param>
/// <param name="permissions">权限。</param>
/// <param name="audit">审计。</param>
public sealed class DeviceConfigService(
    ISqlSugarClient db,
    IDeviceManager deviceManager,
    ITagService tags,
    AlarmService alarmService,
    IPermissionService permissions,
    IAuditService audit)
{
    /// <summary>配置发生变化时触发。</summary>
    public event EventHandler? ConfigChanged;

    /// <summary>获取设备。</summary>
    public Task<List<DeviceEntity>> GetDevicesAsync() => db.Queryable<DeviceEntity>().OrderBy(d => d.Id).ToListAsync();

    /// <summary>获取组。</summary>
    public Task<List<TagGroupEntity>> GetGroupsAsync(long deviceId) => db.Queryable<TagGroupEntity>().Where(g => g.DeviceId == deviceId).ToListAsync();

    /// <summary>获取点位。</summary>
    public Task<List<TagEntity>> GetTagsAsync(long deviceId) => db.Queryable<TagEntity>().Where(t => t.DeviceId == deviceId).OrderBy(t => t.Id).ToListAsync();

    /// <summary>获取报警定义。</summary>
    public Task<List<AlarmDefinitionEntity>> GetAlarmDefinitionsAsync() => db.Queryable<AlarmDefinitionEntity>().ToListAsync();

    /// <summary>保存设备。</summary>
    public async Task<DeviceEntity> SaveDeviceAsync(DeviceEntity device, bool checkPermission = true)
    {
        if (checkPermission) permissions.Demand(Permissions.Device.Edit);
        if (string.IsNullOrWhiteSpace(device.Name) || device.Name.Contains('.')) throw new InvalidOperationException("设备名不能为空且不能包含 '.'");
        if (await db.Queryable<DeviceEntity>().AnyAsync(d => d.Name == device.Name && d.Id != device.Id))
            throw new InvalidOperationException($"设备名 {device.Name} 已存在");
        string? old = null;
        if (device.Id == 0)
        {
            device.Id = await db.Insertable(device).ExecuteReturnBigIdentityAsync();
            await db.Insertable(new TagGroupEntity { DeviceId = device.Id, Name = "Default", ScanIntervalMs = 100 }).ExecuteCommandAsync();
        }
        else
        {
            var e = await db.Queryable<DeviceEntity>().InSingleAsync(device.Id);
            old = Json.Serialize(new { e.Name, e.DriverKey, e.OptionsJson, e.Enabled });
            device.UpdatedAt = DateTime.Now;
            await db.Updateable(device).IgnoreColumns(d => d.CreatedAt).ExecuteCommandAsync();
        }
        if (checkPermission)
            await audit.LogAsync(AuditActions.DeviceChange, $"device:{device.Name}", old, Json.Serialize(new { device.Name, device.DriverKey, device.OptionsJson, device.Enabled }));
        ConfigChanged?.Invoke(this, EventArgs.Empty);
        return device;
    }

    /// <summary>删除设备。</summary>
    public async Task DeleteDeviceAsync(long deviceId)
    {
        permissions.Demand(Permissions.Device.Edit);
        var d = await db.Queryable<DeviceEntity>().InSingleAsync(deviceId);
        await db.Deleteable<TagEntity>().Where(t => t.DeviceId == deviceId).ExecuteCommandAsync();
        await db.Deleteable<TagGroupEntity>().Where(t => t.DeviceId == deviceId).ExecuteCommandAsync();
        await db.Deleteable<DeviceEntity>().In(deviceId).ExecuteCommandAsync();
        await audit.LogAsync(AuditActions.DeviceChange, $"device:{d?.Name}", "存在", "已删除");
        ConfigChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>保存组。</summary>
    public async Task<TagGroupEntity> SaveGroupAsync(TagGroupEntity group, bool checkPermission = true)
    {
        if (checkPermission) permissions.Demand(Permissions.Device.Edit);
        if (group.Id == 0) group.Id = await db.Insertable(group).ExecuteReturnBigIdentityAsync();
        else await db.Updateable(group).ExecuteCommandAsync();
        return group;
    }

    /// <summary>整体保存设备的点位表（增删改）</summary>
    public async Task SaveTagsAsync(long deviceId, IReadOnlyList<TagEntity> list, bool checkPermission = true)
    {
        if (checkPermission) permissions.Demand(Permissions.Device.Edit);
        var dup = list.GroupBy(t => t.Name, StringComparer.OrdinalIgnoreCase).FirstOrDefault(g => g.Count() > 1);
        if (dup != null) throw new InvalidOperationException($"点位名重复: {dup.Key}");
        if (list.Any(t => string.IsNullOrWhiteSpace(t.Name) || string.IsNullOrWhiteSpace(t.Address)))
            throw new InvalidOperationException("点位名和地址不能为空");

        var groups = await GetGroupsAsync(deviceId);
        var defaultGroup = groups.FirstOrDefault() ?? await SaveGroupAsync(new TagGroupEntity { DeviceId = deviceId, Name = "Default", ScanIntervalMs = 100 }, false);
        var existing = await GetTagsAsync(deviceId);
        var keepIds = list.Where(t => t.Id > 0).Select(t => t.Id).ToHashSet();
        var removed = existing.Where(e => !keepIds.Contains(e.Id)).Select(e => e.Id).ToList();
        if (removed.Count > 0) await db.Deleteable<TagEntity>().In(removed).ExecuteCommandAsync();

        foreach (var t in list)
        {
            t.DeviceId = deviceId;
            if (t.GroupId == 0 || groups.All(g => g.Id != t.GroupId)) t.GroupId = defaultGroup.Id;
            if (t.Scale == 0) t.Scale = 1;
            t.UpdatedAt = DateTime.Now;
        }
        var inserts = list.Where(t => t.Id == 0).ToList();
        var updates = list.Where(t => t.Id > 0).ToList();
        if (inserts.Count > 0) await db.Insertable(inserts).ExecuteCommandAsync();
        if (updates.Count > 0) await db.Updateable(updates).ExecuteCommandAsync();
        if (checkPermission)
            await audit.LogAsync(AuditActions.DeviceChange, $"device:{deviceId}:tags", $"{existing.Count} 个", $"{list.Count} 个（新增 {inserts.Count}，删除 {removed.Count}）");
        ConfigChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>保存报警定义。</summary>
    public async Task SaveAlarmDefinitionsAsync(IReadOnlyList<AlarmDefinitionEntity> defs, bool checkPermission = true)
    {
        if (checkPermission) permissions.Demand(Permissions.Alarm.Configure);
        await db.Deleteable<AlarmDefinitionEntity>().Where(a => a.Id > 0).ExecuteCommandAsync();
        foreach (var d in defs) d.Id = 0;
        if (defs.Count > 0) await db.Insertable(defs.ToList()).ExecuteCommandAsync();
        if (checkPermission) await audit.LogAsync(AuditActions.DeviceChange, "alarm-definitions", newValue: $"{defs.Count} 条");
        await alarmService.LoadAsync();
    }

    /// <summary>重载通讯，使配置生效</summary>
    public async Task ApplyAsync(bool checkPermission = true)
    {
        if (checkPermission) permissions.Demand(Permissions.Device.Reload);
        await deviceManager.ReloadAsync();
        await alarmService.LoadAsync();
    }

    /// <summary>手动写 PLC（界面层负责电子签名）</summary>
    public async Task<DeviceResult> ManualWriteAsync(string tagFullName, string text, string? signer)
    {
        permissions.Demand(Permissions.Device.ManualWrite);
        var tag = tags.FindTag(tagFullName) ?? throw new InvalidOperationException($"点位不存在: {tagFullName}");
        var value = TagValueConverter.Parse(tag.DataType, text);
        var old = tags.GetValue(tagFullName)?.Value;
        var r = await tags.WriteAsync(tagFullName, value);
        await audit.LogAsync(AuditActions.ManualWrite, $"tag:{tagFullName}", old, $"{text} ({(r.IsSuccess ? "成功" : r.Message)})", signer);
        return r;
    }

    // ---- CSV 导入导出 ----

    /// <summary>CSV标题。</summary>
    private const string CsvHeader = "Name,Address,DataType,Length,Scale,Offset,Access,Group,Description";

    /// <summary>导出点位CSV。</summary>
    public async Task ExportTagsCsvAsync(long deviceId, string path)
    {
        var groups = (await GetGroupsAsync(deviceId)).ToDictionary(g => g.Id, g => g.Name);
        var sb = new StringBuilder(CsvHeader).AppendLine();
        foreach (var t in await GetTagsAsync(deviceId))
        {
            sb.AppendLine(string.Join(",", t.Name, t.Address, t.DataType, t.Length,
                t.Scale.ToString(CultureInfo.InvariantCulture), t.Offset.ToString(CultureInfo.InvariantCulture), t.Access,
                groups.GetValueOrDefault(t.GroupId, "Default"), Escape(t.Description)));
        }
        await File.WriteAllTextAsync(path, sb.ToString(), new UTF8Encoding(true));
    }

    /// <summary>从 CSV 导入点位（同名覆盖），返回导入条数</summary>
    public async Task<int> ImportTagsCsvAsync(long deviceId, string path)
    {
        permissions.Demand(Permissions.Device.Edit);
        var lines = await File.ReadAllLinesAsync(path);
        var groups = await GetGroupsAsync(deviceId);
        var existing = (await GetTagsAsync(deviceId)).ToDictionary(t => t.Name, StringComparer.OrdinalIgnoreCase);
        var count = 0;
        foreach (var line in lines.Skip(1).Where(l => !string.IsNullOrWhiteSpace(l)))
        {
            var c = line.Split(',');
            if (c.Length < 3) throw new InvalidOperationException($"CSV 格式错误: {line}");
            var name = c[0].Trim();
            var tag = existing.GetValueOrDefault(name) ?? new TagEntity { Name = name };
            tag.Address = c[1].Trim();
            tag.DataType = Enum.Parse<TagDataType>(c[2].Trim(), true);
            tag.Length = c.Length > 3 && int.TryParse(c[3], out var len) ? len : 0;
            tag.Scale = c.Length > 4 && double.TryParse(c[4], NumberStyles.Float, CultureInfo.InvariantCulture, out var s) ? s : 1;
            tag.Offset = c.Length > 5 && double.TryParse(c[5], NumberStyles.Float, CultureInfo.InvariantCulture, out var o) ? o : 0;
            tag.Access = c.Length > 6 && Enum.TryParse<TagAccess>(c[6].Trim(), true, out var a) ? a : TagAccess.ReadWrite;
            var groupName = c.Length > 7 ? c[7].Trim() : "Default";
            var group = groups.FirstOrDefault(g => g.Name == groupName);
            if (group == null)
            {
                group = await SaveGroupAsync(new TagGroupEntity { DeviceId = deviceId, Name = groupName, ScanIntervalMs = 100 }, false);
                groups.Add(group);
            }
            tag.GroupId = group.Id;
            tag.Description = c.Length > 8 ? string.Join(",", c.Skip(8)).Trim('"') : null;
            existing[name] = tag;
            count++;
        }
        await SaveTagsAsync(deviceId, existing.Values.ToList());
        return count;
    }

    /// <summary>转义。</summary>
    private static string Escape(string? s) => string.IsNullOrEmpty(s) ? "" : s.Contains(',') ? $"\"{s}\"" : s;
}
