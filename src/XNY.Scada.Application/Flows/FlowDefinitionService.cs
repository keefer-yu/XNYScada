using SqlSugar;
using XNY.Scada.Domain;
using XNY.Scada.Domain.Entities;
using XNY.Scada.Flow;
using XNY.Scada.Flow.Model;
using XNY.Scada.Flow.Nodes;
using XNY.Scada.Flow.Runtime;
using XNY.Scada.Security;

namespace XNY.Scada.Application.Flows;

/// <summary>
/// 流程定义管理：草稿保存、校验、发布（版本不可变）、回滚、导入导出。
/// 同时作为流程引擎的 <see cref="IFlowDefinitionStore"/>。
/// </summary>
/// <param name="db">数据库客户端。</param>
/// <param name="registry">注册。</param>
/// <param name="permissions">权限。</param>
/// <param name="currentUser">当前用户。</param>
/// <param name="audit">审计。</param>
/// <param name="engine">引擎。</param>
public sealed class FlowDefinitionService(
    ISqlSugarClient db,
    INodeRegistry registry,
    IPermissionService permissions,
    ICurrentUser currentUser,
    IAuditService audit,
    Lazy<IFlowEngine> engine) : IFlowDefinitionStore
{
    /// <summary>流发生变化时触发。</summary>
    public event EventHandler<string>? FlowChanged;

    /// <summary>列出。</summary>
    public Task<List<FlowDefinitionEntity>> ListAsync() => db.Queryable<FlowDefinitionEntity>().OrderBy(f => f.Code).ToListAsync();

    /// <summary>获取版本。</summary>
    public Task<List<FlowVersionEntity>> GetVersionsAsync(string code) =>
        db.Queryable<FlowVersionEntity>().Where(v => v.FlowCode == code).OrderBy(v => v.Version, OrderByType.Desc).ToListAsync();

    /// <summary>获取最新版本（草稿优先）用于编辑</summary>
    public async Task<FlowDocument?> GetLatestAsync(string code)
    {
        var v = await db.Queryable<FlowVersionEntity>().Where(x => x.FlowCode == code).OrderBy(x => x.Version, OrderByType.Desc).FirstAsync();
        return v == null ? null : WithVersion(FlowDocument.FromJson(v.Json), v.Version);
    }

    /// <summary>获取版本。</summary>
    public async Task<FlowDocument?> GetVersionAsync(string code, int version)
    {
        var v = await db.Queryable<FlowVersionEntity>().FirstAsync(x => x.FlowCode == code && x.Version == version);
        return v == null ? null : WithVersion(FlowDocument.FromJson(v.Json), v.Version);
    }

    /// <summary>校验。</summary>
    public IReadOnlyList<ValidationIssue> Validate(FlowDocument doc)
    {
        var issues = FlowValidator.Validate(doc, registry).ToList();
        foreach (var n in doc.Nodes)
        {
            var perm = registry.Find(n.Type)?.RequiredPermission;
            if (perm != null && !permissions.Has(perm)) issues.Add(new ValidationIssue(true, n.Id, $"当前用户无权使用节点 {n.Type}（需要 {perm}）"));
        }
        return issues;
    }

    /// <summary>保存草稿：若最新版本已发布则生成新版本，否则覆盖草稿</summary>
    public async Task<int> SaveDraftAsync(FlowDocument doc, string? remark = null)
    {
        permissions.Demand(Permissions.Flow.Edit);
        if (string.IsNullOrWhiteSpace(doc.Code)) throw new FlowException("流程编码不能为空");
        EnsureScriptPermission(doc);

        var def = await db.Queryable<FlowDefinitionEntity>().FirstAsync(f => f.Code == doc.Code);
        if (def == null)
        {
            def = new FlowDefinitionEntity { Code = doc.Code, Name = doc.Name, Description = doc.Description };
            def.Id = await db.Insertable(def).ExecuteReturnBigIdentityAsync();
        }
        else
        {
            def.Name = doc.Name;
            def.Description = doc.Description;
            def.UpdatedAt = DateTime.Now;
        }

        var latest = await db.Queryable<FlowVersionEntity>().Where(v => v.FlowCode == doc.Code).OrderBy(v => v.Version, OrderByType.Desc).FirstAsync();
        int version;
        if (latest != null && latest.Status == FlowVersionStatus.Draft)
        {
            version = latest.Version;
            doc.Version = version;
            latest.Json = doc.ToJson();
            latest.Remark = remark ?? latest.Remark;
            latest.UpdatedAt = DateTime.Now;
            await db.Updateable(latest).ExecuteCommandAsync();
        }
        else
        {
            version = (latest?.Version ?? 0) + 1;
            doc.Version = version;
            await db.Insertable(new FlowVersionEntity
            {
                FlowId = def.Id, FlowCode = doc.Code, Version = version, Json = doc.ToJson(), Status = FlowVersionStatus.Draft, Remark = remark
            }).ExecuteCommandAsync();
        }
        def.LatestVersion = version;
        await db.Updateable(def).ExecuteCommandAsync();
        await audit.LogAsync(AuditActions.FlowSave, $"flow:{doc.Code}:v{version}", newValue: remark);
        FlowChanged?.Invoke(this, doc.Code);
        return version;
    }

    /// <summary>发布指定版本，旧发布版本归档；引擎在流程空闲后切换</summary>
    public async Task PublishAsync(string code, int version, string? signer, string? remark)
    {
        permissions.Demand(Permissions.Flow.Publish);
        var v = await db.Queryable<FlowVersionEntity>().FirstAsync(x => x.FlowCode == code && x.Version == version)
                ?? throw new FlowException($"流程 {code} 版本 {version} 不存在");
        var doc = FlowDocument.FromJson(v.Json);
        var errors = Validate(doc).Where(i => i.IsError).ToList();
        if (errors.Count > 0) throw new FlowException("流程校验未通过：\n" + string.Join("\n", errors));

        var def = await db.Queryable<FlowDefinitionEntity>().FirstAsync(f => f.Code == code);
        var oldVersion = def.PublishedVersion;
        await db.Updateable<FlowVersionEntity>()
            .SetColumns(x => new FlowVersionEntity { Status = FlowVersionStatus.Archived })
            .Where(x => x.FlowCode == code && x.Status == FlowVersionStatus.Published)
            .ExecuteCommandAsync();
        v.Status = FlowVersionStatus.Published;
        v.PublishedAt = DateTime.Now;
        v.PublishedBy = signer ?? currentUser.UserName;
        v.Remark = remark ?? v.Remark;
        await db.Updateable(v).ExecuteCommandAsync();
        def.PublishedVersion = version;
        def.UpdatedAt = DateTime.Now;
        await db.Updateable(def).ExecuteCommandAsync();

        await audit.LogAsync(AuditActions.FlowPublish, $"flow:{code}", $"v{oldVersion}", $"v{version}", signer, remark);
        if (engine.Value.IsRunning) await engine.Value.ReloadAsync(code);
        FlowChanged?.Invoke(this, code);
    }

    /// <summary>设置启用。</summary>
    public async Task SetEnabledAsync(string code, bool enabled)
    {
        permissions.Demand(Permissions.Flow.Control);
        await db.Updateable<FlowDefinitionEntity>().SetColumns(f => f.Enabled == enabled).Where(f => f.Code == code).ExecuteCommandAsync();
        await audit.LogAsync(AuditActions.FlowPublish, $"flow:{code}", newValue: enabled ? "启用" : "停用");
        if (engine.Value.IsRunning) await engine.Value.ReloadAsync(code);
        FlowChanged?.Invoke(this, code);
    }

    /// <summary>删除。</summary>
    public async Task DeleteAsync(string code)
    {
        permissions.Demand(Permissions.Flow.Publish);
        await db.Deleteable<FlowVersionEntity>().Where(v => v.FlowCode == code).ExecuteCommandAsync();
        await db.Deleteable<FlowDefinitionEntity>().Where(f => f.Code == code).ExecuteCommandAsync();
        await audit.LogAsync(AuditActions.FlowPublish, $"flow:{code}", "存在", "已删除");
        if (engine.Value.IsRunning) await engine.Value.ReloadAsync(code);
        FlowChanged?.Invoke(this, code);
    }

    /// <summary>导入流程（用于机型模板），可选择直接发布。导入不校验当前用户权限，由调用方控制。</summary>
    internal async Task ImportAsync(FlowDocument doc, bool publish, string machineType)
    {
        var def = await db.Queryable<FlowDefinitionEntity>().FirstAsync(f => f.Code == doc.Code);
        if (def == null)
        {
            def = new FlowDefinitionEntity { Code = doc.Code, Name = doc.Name, Description = doc.Description, MachineType = machineType };
            def.Id = await db.Insertable(def).ExecuteReturnBigIdentityAsync();
        }
        var version = def.LatestVersion + 1;
        doc.Version = version;
        await db.Updateable<FlowVersionEntity>()
            .SetColumns(x => new FlowVersionEntity { Status = FlowVersionStatus.Archived })
            .Where(x => x.FlowCode == doc.Code && x.Status == FlowVersionStatus.Published)
            .ExecuteCommandAsync();
        await db.Insertable(new FlowVersionEntity
        {
            FlowId = def.Id, FlowCode = doc.Code, Version = version, Json = doc.ToJson(),
            Status = publish ? FlowVersionStatus.Published : FlowVersionStatus.Draft,
            PublishedAt = publish ? DateTime.Now : null, PublishedBy = publish ? currentUser.UserName : null, Remark = "模板导入"
        }).ExecuteCommandAsync();
        def.LatestVersion = version;
        if (publish) def.PublishedVersion = version;
        def.Name = doc.Name;
        def.MachineType = machineType;
        await db.Updateable(def).ExecuteCommandAsync();
        FlowChanged?.Invoke(this, doc.Code);
    }

    /// <summary>确保脚本权限。</summary>
    private void EnsureScriptPermission(FlowDocument doc)
    {
        foreach (var n in doc.Nodes)
        {
            var perm = registry.Find(n.Type)?.RequiredPermission;
            if (perm != null) permissions.Demand(perm);
        }
    }

    /// <summary>附带版本。</summary>
    private static FlowDocument WithVersion(FlowDocument doc, int version)
    {
        doc.Version = version;
        return doc;
    }

    // ---- IFlowDefinitionStore ----

    /// <summary>获取已发布。</summary>
    public async Task<IReadOnlyList<PublishedFlow>> GetPublishedAsync()
    {
        var defs = await db.Queryable<FlowDefinitionEntity>().Where(f => f.PublishedVersion > 0).ToListAsync();
        var result = new List<PublishedFlow>();
        foreach (var d in defs)
        {
            var p = await LoadPublishedAsync(d);
            if (p != null) result.Add(p);
        }
        return result;
    }

    /// <summary>获取已发布。</summary>
    public async Task<PublishedFlow?> GetPublishedAsync(string code)
    {
        var d = await db.Queryable<FlowDefinitionEntity>().FirstAsync(f => f.Code == code && f.PublishedVersion > 0);
        return d == null ? null : await LoadPublishedAsync(d);
    }

    /// <summary>加载已发布。</summary>
    private async Task<PublishedFlow?> LoadPublishedAsync(FlowDefinitionEntity d)
    {
        var v = await db.Queryable<FlowVersionEntity>().FirstAsync(x => x.FlowCode == d.Code && x.Version == d.PublishedVersion);
        if (v == null) return null;
        var doc = FlowDocument.FromJson(v.Json);
        doc.Version = v.Version;
        return new PublishedFlow(d.Code, d.Name, v.Version, d.Enabled, doc);
    }
}

/// <summary>流实例日志接收器。</summary>
/// <param name="db">数据库客户端。</param>
public sealed class FlowInstanceLogSink(ISqlSugarClient db) : IFlowInstanceLogSink
{
    /// <summary>写入。</summary>
    public Task WriteAsync(FlowInstance instance) =>
        db.Insertable(new FlowInstanceLogEntity
        {
            FlowCode = instance.FlowCode,
            Version = instance.Document.Version,
            InstanceId = instance.Id,
            Status = instance.Status,
            TriggerSource = instance.TriggerSource,
            StartedAt = instance.StartedAt,
            EndedAt = instance.EndedAt,
            DurationMs = instance.DurationMs,
            Error = instance.Error,
            TraceJson = instance.TraceJson()
        }).ExecuteCommandAsync();
}
