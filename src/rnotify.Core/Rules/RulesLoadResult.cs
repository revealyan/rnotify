namespace rnotify.Core.Rules;

/// <summary>
/// Результат загрузки конфига: молчаливых дефолтов нет — либо конфиг, либо
/// ошибка наружу.
/// </summary>
/// <param name="Config">Конфиг при успехе; null при ошибке чтения/разбора.</param>
/// <param name="CreatedDefault">Файла не было — создан и записан дефолтный конфиг.</param>
/// <param name="Error">Ошибка чтения/разбора/записи дефолта; файл на диске не тронут.</param>
public sealed record RulesLoadResult(RulesConfig? Config, bool CreatedDefault, Exception? Error)
{
	/// <summary>Конфиг доступен для компиляции.</summary>
	public bool Success => Config is not null;
}
