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

				// Сдвиг вверх (DIP) — сцены «стопкой» рядом с нативным баннером.
				int offset = 0;
				int oi = cli.IndexOf("--offset");
				if (oi >= 0 && oi + 1 < cli.Count && int.TryParse(cli[oi + 1], out int parsedOffset))
				{
					offset = parsedOffset;
				}

			// ExitCodeOnLastWindowClose: закрылась карточка — вышел процесс.
			// Флаги бисекции рендера: --noex/--noentrance/--nobackdrop/--nopresenter/--nozone.
			bool noEx = cli.Contains("--noex", StringComparer.OrdinalIgnoreCase);
			bool noEntrance = cli.Contains("--noentrance", StringComparer.OrdinalIgnoreCase);
			bool noBackdrop = cli.Contains("--nobackdrop", StringComparer.OrdinalIgnoreCase);
			bool noPresenter = cli.Contains("--nopresenter", StringComparer.OrdinalIgnoreCase);
			bool noZone = cli.Contains("--nozone", StringComparer.OrdinalIgnoreCase);
			CardWindow card = new(
				sticky ? null : ttl,
				addNoActivateStyle: !noEx,
				addEntrance: !noEntrance,
				addBackdrop: !noBackdrop,
				addPresenter: !noPresenter,
				placeAtZone: !noZone,
				offsetDip: offset);
			card.ShowNoActivate();
		}
		else
		{
			new JoystickWindow().Activate();
		}
	}
}
