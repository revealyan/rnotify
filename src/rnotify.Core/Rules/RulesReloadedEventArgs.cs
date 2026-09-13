namespace rnotify.Core.Rules;

/// <summary>Конфиг перезагружен: актуальный движок и ошибка, если была.</summary>
public sealed class RulesReloadedEventArgs : EventArgs
{
	/// <summary>Создаёт аргументы события перезагрузки.</summary>
	/// <param name="engine">Актуальный движок: новый при успехе, прежний при ошибке.</param>
	/// <param name="error">Ошибка перезагрузки; null — успех.</param>
	public RulesReloadedEventArgs(RulesEngine engine, Exception? error)
	{
		Engine = engine;
		Error = error;
	}

	/// <summary>Актуальный движок: новый при успехе, прежний при ошибке.</summary>
	public RulesEngine Engine { get; }

	/// <summary>Ошибка перезагрузки; null — успех.</summary>
	public Exception? Error { get; }

	/// <summary>Перезагрузка успешна.</summary>
	public bool Success => Error is null;
}
