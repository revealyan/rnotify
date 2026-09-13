namespace rnotify.Core.Rules;

/// <summary>
/// Владелец «последнего рабочего» движка: стартовая загрузка в конструкторе,
/// перезагрузки по требованию (их зовёт <see cref="RulesMonitor"/> из дебаунса).
/// Успех — подмена <see cref="Current"/> и событие; ошибка — Current не
/// трогаем, ошибка уходит тем же событием: опечатка в json не выключает
/// правила пользователя (грабля старого rnotif).
/// </summary>
public sealed class RulesReloader
{
	private readonly RulesStore _store;
	private readonly Lock _reloadGate = new();
	private volatile RulesEngine _current;

	/// <summary>Создаёт релоадер и грузит конфиг первый раз; Current всегда не-null.</summary>
	/// <param name="store">Хранилище правил.</param>
	public RulesReloader(RulesStore store)
	{
		_store = store;
		RulesLoadResult load = store.LoadOrDefault();
		// Битый/отсутствующий файл → дефолт в памяти; CreatedDefault=true скажет окну про файл.
		_current = RulesEngine.Compile(load.Config ?? RulesStore.CreateDefault());
		StartupError = load.Error;
	}

	/// <summary>Ошибка стартовой загрузки (работаем на дефолте); null — всё чисто.</summary>
	public Exception? StartupError { get; }

	/// <summary>Актуальный движок; подменяется только успешной перезагрузкой (volatile — Decide с любого потока).</summary>
	public RulesEngine Current => _current;

	/// <summary>Конфиг перезагружен: новый движок при успехе, прежний + ошибка при неудаче.</summary>
	public event EventHandler<RulesReloadedEventArgs>? Reloaded;

	/// <summary>Перезагрузка из файла; не бросает — все исходы уходят событием.</summary>
	public void ReloadNow()
	{
		RulesReloadedEventArgs args;
		lock (_reloadGate)
		{
			RulesLoadResult load = _store.LoadOrDefault();
			if (load.Config is null)
			{
				args = new RulesReloadedEventArgs(_current, load.Error!);
			}
			else
			{
				RulesEngine engine = RulesEngine.Compile(load.Config);
				_current = engine;
				args = new RulesReloadedEventArgs(engine, load.Error);
			}
		}

		// Событие вне лока: хендлеры не сериализуют перезагрузки и не дедлокают.
		Reloaded?.Invoke(this, args);
	}
}
