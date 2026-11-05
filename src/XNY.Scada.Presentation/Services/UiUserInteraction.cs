using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using XNY.Scada.Flow.Runtime;

namespace XNY.Scada.Presentation.Services;

/// <summary>流程「人工确认」节点的待确认项，以非模态方式显示在主界面</summary>
public sealed partial class ConfirmRequest : ObservableObject
{
    private readonly TaskCompletionSource<bool> _tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public ConfirmRequest(string title, string message, DateTime deadline)
    {
        Title = title;
        Message = message;
        Deadline = deadline;
        YesCommand = new RelayCommand(() => _tcs.TrySetResult(true));
        NoCommand = new RelayCommand(() => _tcs.TrySetResult(false));
    }

    public string Title { get; }

    public string Message { get; }

    public DateTime Deadline { get; }

    public IRelayCommand YesCommand { get; }

    public IRelayCommand NoCommand { get; }

    public Task<bool> Task => _tcs.Task;

    internal void Complete(bool value) => _tcs.TrySetResult(value);
}

public sealed class UiUserInteraction(IUiDispatcher ui) : IUserInteraction
{
    public event EventHandler<ConfirmRequest>? ConfirmRequested;

    public event EventHandler<ConfirmRequest>? ConfirmFinished;

    public event EventHandler<(string Message, bool IsError)>? Notified;

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

    public void Notify(string message, bool isError) => ui.BeginInvoke(() => Notified?.Invoke(this, (message, isError)));
}
