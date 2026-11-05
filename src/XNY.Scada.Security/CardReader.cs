using System.IO.Ports;
using System.Text;
using Microsoft.Extensions.Logging;

namespace XNY.Scada.Security;

/// <summary>刷卡器。HID 读卡器以键盘方式输入，由登录界面直接接收；串口读卡器由此类读取。</summary>
public interface ICardReader : IDisposable
{
    /// <summary>卡只读发生变化时触发。</summary>
    event EventHandler<string>? CardRead;

    /// <summary>是否打开。</summary>
    bool IsOpen { get; }

    /// <summary>打开。</summary>
    void Open();
}

/// <summary>串口卡阅读器。</summary>
/// <param name="options">配置项。</param>
/// <param name="logger">日志记录器。</param>
public sealed class SerialCardReader(SecurityOptions options, ILogger<SerialCardReader> logger) : ICardReader
{
    /// <summary>端口号。</summary>
    private SerialPort? _port;
    /// <summary>缓冲区。</summary>
    private readonly StringBuilder _buffer = new();

    /// <summary>卡只读发生变化时触发。</summary>
    public event EventHandler<string>? CardRead;

    /// <summary>是否打开。</summary>
    public bool IsOpen => _port?.IsOpen == true;

    /// <summary>打开。</summary>
    public void Open()
    {
        if (string.IsNullOrWhiteSpace(options.CardReaderPort) || IsOpen) return;
        try
        {
            _port = new SerialPort(options.CardReaderPort, options.CardReaderBaudRate);
            _port.DataReceived += OnData;
            _port.Open();
            logger.LogInformation("刷卡器已打开 {Port}", options.CardReaderPort);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "刷卡器打开失败 {Port}", options.CardReaderPort);
        }
    }

    /// <summary>处理数据。</summary>
    private void OnData(object sender, SerialDataReceivedEventArgs e)
    {
        if (_port == null) return;
        _buffer.Append(_port.ReadExisting());
        var text = _buffer.ToString();
        var idx = text.IndexOfAny(['\r', '\n']);
        if (idx < 0) return;
        var card = text[..idx].Trim();
        _buffer.Clear().Append(text[(idx + 1)..].TrimStart('\r', '\n'));
        if (card.Length > 0) CardRead?.Invoke(this, card);
    }

    /// <summary>释放资源。</summary>
    public void Dispose()
    {
        if (_port == null) return;
        _port.DataReceived -= OnData;
        _port.Dispose();
    }
}
