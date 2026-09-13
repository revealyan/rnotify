using Microsoft.UI.Xaml;

namespace rnotify.SpikeWinUI;

/// <summary>
/// Старт спайка: без аргументов — джойстик (ручной прогон); «card» — только
/// карточка с авто-TTL (PS-автоматизация render-card-check.ps1).
/// </summary>
public partial class App : Application
{
	public App()
	{
		InitializeComponent();
	}

	protected override void OnLaunched(LaunchActivatedEventArgs args)
	{
		base.OnLaunched(args);
		// Своего Program.Main нет (авто-Main целей WinUI) — аргументы из среды.
		List<string> cli = [.. Environment.GetCommandLineArgs().Skip(1)];

		if (cli.Contains("card", StringComparer.OrdinalIgnoreCase))
		{
			bool sticky = cli.Contains("--sticky", StringComparer.OrdinalIgnoreCase);
			int ttl = 5;
			int i = cli.IndexOf("--ttl");
			if (i >= 0 && i + 1 < cli.Count && int.TryParse(cli[i + 1], out int parsed))
			{
				ttl = parsed;
			}

			// ExitCodeOnLastWindowClose: закрылась карточка — вышел процесс.
			CardWindow card = new(sticky ? null : ttl);
			card.ShowNoActivate();
		}
		else
		{
			new JoystickWindow().Activate();
		}
	}
}
