namespace XNY.Scada.Flow;

/// <summary>流异常。</summary>
/// <param name="message">消息。</param>
/// <param name="inner">内部。</param>
public class FlowException(string message, Exception? inner = null) : Exception(message, inner);
