namespace rnotify.Core.Suppression;

/// <summary>Строка трейса подавления — в панель диагностики (канал верификации владельца).</summary>
public sealed class SuppressionTraceEventArgs(string message) : EventArgs
{
	/// <summary>Сообщение строки.</summary>
	public string Message { get; } = message;
}
