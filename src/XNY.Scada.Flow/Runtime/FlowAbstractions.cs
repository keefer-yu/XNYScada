using XNY.Scada.Flow.Model;

namespace XNY.Scada.Flow.Runtime;

public sealed record PublishedFlow(string Code, string Name, int Version, bool Enabled, FlowDocument Document);

/// <summary>流程定义存储，由应用层基于数据库实现</summary>
public interface IFlowDefinitionStore
{
    Task<IReadOnlyList<PublishedFlow>> GetPublishedAsync();

    Task<PublishedFlow?> GetPublishedAsync(string code);
}

/// <summary>流程实例日志落库</summary>
public interface IFlowInstanceLogSink
{
    Task WriteAsync(FlowInstance instance);
}

/// <summary>流程与操作员交互（弹窗确认），由界面层实现</summary>
public interface IUserInteraction
{
    Task<bool> ConfirmAsync(string title, string message, TimeSpan timeout, bool defaultResult, CancellationToken ct);

    void Notify(string message, bool isError);
}

public sealed class NullUserInteraction : IUserInteraction
{
    public Task<bool> ConfirmAsync(string title, string message, TimeSpan timeout, bool defaultResult, CancellationToken ct) => Task.FromResult(defaultResult);

    public void Notify(string message, bool isError)
    {
    }
}

/// <summary>流程运行时可访问的系统参数（如 MachineType、Station）</summary>
public interface IFlowEnvironment
{
    object? GetParam(string name);
}
