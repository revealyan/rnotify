namespace rnotify.Render;

/// <summary>Строка трейса стека — в панель диагностики (канал верификации владельца).</summary>
internal sealed class CardTraceEventArgs(string message) : EventArgs
{
	internal string Message { get; } = message;
}
