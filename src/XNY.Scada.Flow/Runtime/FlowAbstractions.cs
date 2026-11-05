using XNY.Scada.Flow.Model;

namespace XNY.Scada.Flow.Runtime;

/// <summary>已发布流。</summary>
/// <param name="Code">编码。</param>
/// <param name="Name">名称。</param>
/// <param name="Version">版本号。</param>
/// <param name="Enabled">是否启用。</param>
/// <param name="Document">文档。</param>
public sealed record PublishedFlow(string Code, string Name, int Version, bool Enabled, FlowDocument Document);

/// <summary>流程定义存储，由应用层基于数据库实现</summary>
public interface IFlowDefinitionStore
{
    /// <summary>获取已发布。</summary>
    Task<IReadOnlyList<PublishedFlow>> GetPublishedAsync();

    /// <summary>获取已发布。</summary>
    Task<PublishedFlow?> GetPublishedAsync(string code);
}

/// <summary>流程实例日志落库</summary>
public interface IFlowInstanceLogSink
{
    /// <summary>写入。</summary>
    Task WriteAsync(FlowInstance instance);
}

/// <summary>流程与操作员交互（弹窗确认），由界面层实现</summary>
public interface IUserInteraction
{
    /// <summary>确认。</summary>
    Task<bool> ConfirmAsync(string title, string message, TimeSpan timeout, bool defaultResult, CancellationToken ct);

    /// <summary>通知。</summary>
    void Notify(string message, bool isError);
}

/// <summary>空用户交互。</summary>
public sealed class NullUserInteraction : IUserInteraction
{
    /// <summary>确认。</summary>
    public Task<bool> ConfirmAsync(string title, string message, TimeSpan timeout, bool defaultResult, CancellationToken ct) => Task.FromResult(defaultResult);

    /// <summary>通知。</summary>
    public void Notify(string message, bool isError)
    {
    }
}

/// <summary>流程运行时可访问的系统参数（如 MachineType、Station）</summary>
public interface IFlowEnvironment
{
    /// <summary>获取参数。</summary>
    object? GetParam(string name);
}
