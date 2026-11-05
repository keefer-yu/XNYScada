namespace XNY.Scada.Flow;

public class FlowException(string message, Exception? inner = null) : Exception(message, inner);
