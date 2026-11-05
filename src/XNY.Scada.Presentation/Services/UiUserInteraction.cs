using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using XNY.Scada.Flow.Runtime;

namespace XNY.Scada.Presentation.Services;

/// <summary>流程「人工确认」节点的待确认项，以非模态方式显示在主界面</summary>
public sealed partial class ConfirmRequest : ObservableObject
{
    /// <summary>任务完成源。</summary>
    private readonly TaskCompletionSource<bool> _tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>创建确认请求。</summary>
    public ConfirmRequest(string title, string message, DateTime deadline)
    {
        Title = title;
        Message = message;
        Deadline = deadline;
        YesCommand = new RelayCommand(() => _tcs.TrySetResult(true));
        NoCommand = new RelayCommand(() => _tcs.TrySetResult(false));
    }

    /// <summary>标题。</summary>
    public string Title { get; }

    /// <summary>消息。</summary>
    public string Message { get; }

    /// <summary>截止时间。</summary>
    public DateTime Deadline { get; }

    /// <summary>是命令。</summary>
    public IRelayCommand YesCommand { get; }

    /// <summary>不命令。</summary>
    public IRelayCommand NoCommand { get; }

    /// <summary>任务。</summary>
    public Task<bool> Task => _tcs.Task;

    /// <summary>完成。</summary>
    internal void Complete(bool value) => _tcs.TrySetResult(value);
}

/// <summary>界面用户交互。</summary>
/// <param name="ui">界面。</param>
public sealed class UiUserInteraction(IUiDispatcher ui) : IUserInteraction
{
    /// <summary>确认已请求发生变化时触发。</summary>
    public event EventHandler<ConfirmRequest>? ConfirmRequested;

    /// <summary>确认已完成发生变化时触发。</summary>
    public event EventHandler<ConfirmRequest>? ConfirmFinished;

    /// <summary>已通知发生变化时触发。</summary>
    public event EventHandler<(string Message, bool IsError)>? Notified;

    /// <summary>确认。</summary>
    public async Task<bool> ConfirmAsync(string title, string message, TimeSpan timeout, bool defaultResult, CancellationToken ct)
    {
        var request = new ConfirmRequest(title, message, DateTime.Now + timeout);
        ui.BeginInvoke(() => ConfirmRequested?.Invoke(this, request));
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(timeout);
        await using var reg = timeoutCts.Token.Register(() => request.Complete(defaultResult));
        try
        {
            return await request.Task;
        }
        finally
        {
            ui.BeginInvoke(() => ConfirmFinished?.Invoke(this, request));
        }
    }

    /// <summary>通知。</summary>
    public void Notify(string message, bool isError) => ui.BeginInvoke(() => Notified?.Invoke(this, (message, isError)));
}
