using System.Windows;

namespace rnotify.SpikeWpf;

/// <summary>
/// Точка входа спайка S4.1 (WPF-карточка). Режимы: без аргументов — джойстик
/// (ручной прогон); аргумент <c>card</c> — только карточка с авто-TTL (для
/// PS-автоматизации render-card-check.ps1), <c>--ttl N</c> — секунды,
/// <c>--sticky</c> — без авто-закрытия.
/// </summary>
public partial class App : Application
{
	protected override void OnStartup(StartupEventArgs e)
	{
		base.OnStartup(e);
		List<string> args = [.. e.Args];

		if (args.Contains("card", StringComparer.OrdinalIgnoreCase))
		{
			// Автоматический режим: карточка → TTL → выход процесса.
			bool sticky = args.Contains("--sticky", StringComparer.OrdinalIgnoreCase);
			int ttl = 5;
			int ttlIndex = args.IndexOf("--ttl");
			if (ttlIndex >= 0 && ttlIndex + 1 < args.Count && int.TryParse(args[ttlIndex + 1], out int parsed))
			{
				ttl = parsed;
			}

			CardWindow card = new() { AutoTtlSeconds = sticky ? null : ttl };
			card.Closed += (_, _) => Shutdown();
			card.Show();
		}
		else
		{
			new JoystickWindow().Show();
		}
	}
}
