using System.IO.Ports;
using System.Text;
using Microsoft.Extensions.Logging;

namespace XNY.Scada.Security;

/// <summary>刷卡器。HID 读卡器以键盘方式输入，由登录界面直接接收；串口读卡器由此类读取。</summary>
public interface ICardReader : IDisposable
{
    event EventHandler<string>? CardRead;

    bool IsOpen { get; }

    void Open();
}

public sealed class SerialCardReader(SecurityOptions options, ILogger<SerialCardReader> logger) : ICardReader
{
    private SerialPort? _port;
    private readonly StringBuilder _buffer = new();

    public event EventHandler<string>? CardRead;

    public bool IsOpen => _port?.IsOpen == true;

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

    public void Dispose()
    {
        if (_port == null) return;
        _port.DataReceived -= OnData;
        _port.Dispose();
    }
}
