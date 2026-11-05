namespace XNY.Scada.Security;

/// <summary>无操作自动登出。界面层在鼠标/键盘活动时调用 <see cref="NotifyActivity"/>。</summary>
public sealed class IdleLogoutMonitor : IDisposable
{
    private readonly IAuthService _auth;
    private readonly ICurrentUser _user;
    private readonly Timer _timer;
    private long _lastActivityTicks = DateTime.Now.Ticks;

    public IdleLogoutMonitor(IAuthService auth, ICurrentUser user, SecurityOptions options)
    {
        _auth = auth;
        _user = user;
        TimeoutMinutes = options.AutoLogoutMinutes;
        _timer = new Timer(_ => Check(), null, TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(10));
    }

    public event EventHandler? AutoLoggedOut;

    public int TimeoutMinutes { get; set; }

    public void NotifyActivity() => Interlocked.Exchange(ref _lastActivityTicks, DateTime.Now.Ticks);

    private async void Check()
    {
        if (TimeoutMinutes <= 0 || !_user.IsAuthenticated) return;
        var idle = DateTime.Now - new DateTime(Interlocked.Read(ref _lastActivityTicks));
        if (idle < TimeSpan.FromMinutes(TimeoutMinutes)) return;
        await _auth.LogoutAsync("超时自动登出");
        AutoLoggedOut?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose() => _timer.Dispose();
}
