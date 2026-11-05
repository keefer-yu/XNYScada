namespace XNY.Scada.Security;

/// <summary>无操作自动登出。界面层在鼠标/键盘活动时调用 <see cref="NotifyActivity"/>。</summary>
public sealed class IdleLogoutMonitor : IDisposable
{
    /// <summary>认证。</summary>
    private readonly IAuthService _auth;
    /// <summary>用户。</summary>
    private readonly ICurrentUser _user;
    /// <summary>定时器。</summary>
    private readonly Timer _timer;
    /// <summary>最近活动节拍。</summary>
    private long _lastActivityTicks = DateTime.Now.Ticks;

    /// <summary>创建空闲登出监视器。</summary>
    public IdleLogoutMonitor(IAuthService auth, ICurrentUser user, SecurityOptions options)
    {
        _auth = auth;
        _user = user;
        TimeoutMinutes = options.AutoLogoutMinutes;
        _timer = new Timer(_ => Check(), null, TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(10));
    }

    /// <summary>自动已记录超出发生变化时触发。</summary>
    public event EventHandler? AutoLoggedOut;

    /// <summary>空闲超时分钟数。</summary>
    public int TimeoutMinutes { get; set; }

    /// <summary>通知活动。</summary>
    public void NotifyActivity() => Interlocked.Exchange(ref _lastActivityTicks, DateTime.Now.Ticks);

    /// <summary>检查。</summary>
    private async void Check()
    {
        if (TimeoutMinutes <= 0 || !_user.IsAuthenticated) return;
        var idle = DateTime.Now - new DateTime(Interlocked.Read(ref _lastActivityTicks));
        if (idle < TimeSpan.FromMinutes(TimeoutMinutes)) return;
        await _auth.LogoutAsync("超时自动登出");
        AutoLoggedOut?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>释放资源。</summary>
    public void Dispose() => _timer.Dispose();
}
