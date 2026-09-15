using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using Windows.Management.Deployment;
using Package = Windows.ApplicationModel.Package;

namespace rnotify.Render;

/// <summary>
/// Резолв AUMID → человеческое имя и иконка отправителя (S6.3; AppInfo
/// листенера даёт только AUMID — канон §10c). Цепочка: реестр
/// AppUserModelId (HKCU→HKLM; DisplayName/IconUri — туда пишут Win32-
/// отправители тостов: rhub, Spotify…) → PackageManager (packaged:
/// DisplayName + Logo по префиксу FamilyName!) → фолбэк «хвост AUMID без
/// .exe» (без фейковой капитализации). Кэш под локом: события фида из пула;
/// ImageSource замораживается (Freeze) для кросс-поточности.
/// </summary>
internal static class SenderResolver
{
	private const string _aumRoot = @"Software\Classes\AppUserModelId";

	private static readonly Lock _gate = new();
	private static readonly Dictionary<string, SenderInfo> _cache = new(StringComparer.Ordinal);
	private static Dictionary<string, (string Name, string Logo)>? _packages; // FamilyName! → (DisplayName, Logo)

	/// <summary>Ошибка последнего снимка пакетов (null — снимок удался).</summary>
	internal static Exception? PackagesSnapshotError { get; private set; }

	/// <summary>Имя и (если далось) иконка отправителя.</summary>
	/// <param name="Name">Отображаемое имя («Claude Code (rhub)», «PowerShell»).</param>
	/// <param name="Icon">Иконка (заморожена) или null — карточка покажет 🔔.</param>
	internal sealed record SenderInfo(string Name, ImageSource? Icon);

	/// <summary>AUMID неизвестен — так и пишем (спайк видел AppInfo=null).</summary>
	internal static SenderInfo Unknown { get; } = new("<без AUMID>", null);

	internal static SenderInfo Resolve(string? aumid)
	{
		if (string.IsNullOrEmpty(aumid))
		{
			return Unknown;
		}

		lock (_gate)
		{
			if (_cache.TryGetValue(aumid, out SenderInfo? hit) && hit is not null)
			{
				return hit;
			}
		}

		// Резолв вне лока: PackageManager/реестр могут стоить десятки мс.
		SenderInfo resolved = ResolveSlow(aumid);
		lock (_gate)
		{
			_cache[aumid] = resolved;
		}

		return resolved;
	}

	private static SenderInfo ResolveSlow(string aumid)
	{
		(string? regName, string? regIcon) = FromRegistry(aumid);
		(string? pkgName, string? pkgLogo) = FromPackages(aumid);

		string name = regName ?? pkgName ?? NameFromAumid(aumid);
		ImageSource? icon = TryLoadIcon(regIcon) ?? TryLoadIcon(pkgLogo);
		return new SenderInfo(name, icon);
	}

	// Реестр AppUserModelId: AUMID с бэкслэшами (полные пути powershell) —
	// OpenSubKey трактует их как вложенность, для чтения это прозрачно.
	private static (string? Name, string? IconUri) FromRegistry(string aumid)
	{
		foreach (RegistryHive hive in new[] { RegistryHive.CurrentUser, RegistryHive.LocalMachine })
		{
			using RegistryKey root = RegistryKey.OpenBaseKey(hive, RegistryView.Default);
			using RegistryKey? key = root.OpenSubKey(_aumRoot + @"\" + aumid);
			if (key is null)
			{
				continue;
			}

			string? name = key.GetValue("DisplayName") as string;
			string? icon = key.GetValue("IconUri") as string;
			if (!string.IsNullOrWhiteSpace(name))
			{
				return (name, icon);
			}
		}

		return (null, null);
	}

	// Packaged-отправители: AUMID = FamilyName!AppId. Снимок пакетов — один
	// на процесс (FindPackagesForUser ~сотни мс на живой машине).
	private static (string? Name, string? Logo) FromPackages(string aumid)
	{
		Dictionary<string, (string Name, string Logo)> packages = GetPackagesSnapshot();
		foreach (KeyValuePair<string, (string Name, string Logo)> package in packages)
		{
			if (aumid.StartsWith(package.Key, StringComparison.Ordinal))
			{
				return (package.Value.Name, package.Value.Logo);
			}
		}

		return (null, null);
	}

	[SuppressMessage("Design", "CA1031:Do not catch general exception types",
		Justification = "Внешнее хранилище пакетов: неудавшийся снимок лишь выключает packaged-ветку резолва, отправитель получит фолбэк-имя")]
	private static Dictionary<string, (string Name, string Logo)> GetPackagesSnapshot()
	{
		lock (_gate)
		{
			if (_packages is not null)
			{
				return _packages;
			}
		}

		Dictionary<string, (string Name, string Logo)> snapshot = [];
		try
		{
			PackageManager manager = new();
			foreach (Package package in manager.FindPackagesForUser(string.Empty))
			{
				string familyName = package.Id.FamilyName;
				if (!string.IsNullOrEmpty(familyName) && !string.IsNullOrEmpty(package.DisplayName))
				{
					snapshot[familyName + "!"] = (package.DisplayName, package.Logo?.ToString() ?? string.Empty);
				}
			}
		}
		catch (Exception ex)
		{
			// Снимок не удался (права/упаковка) — packaged-ветка выключена;
			// ошибку храним для диагностики (панель может показать).
			PackagesSnapshotError = ex;
		}

		lock (_gate)
		{
			_packages = snapshot;
		}

		return snapshot;
	}

	private static BitmapImage? TryLoadIcon(string? pathOrUri)
	{
		if (string.IsNullOrWhiteSpace(pathOrUri))
		{
			return null;
		}

		try
		{
			// Значения бывают и путями (C:\…), и file:/// URI (Logo пакета).
			Uri uri = pathOrUri.StartsWith("file:", StringComparison.OrdinalIgnoreCase)
				? new Uri(pathOrUri, UriKind.Absolute)
				: new Uri(Path.GetFullPath(pathOrUri));
			BitmapImage image = new(uri) { CacheOption = BitmapCacheOption.OnLoad, DecodePixelWidth = 36 };
			image.Freeze(); // создаём из пула — карточка читает на Dispatcher
			return image;
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or UriFormatException)
		{
			return null; // нет файла — карточка покажет 🔔
		}
	}

	// Хвост после последнего \/ без .exe; кассу не фейким: «rhub» остаётся rhub.
	// Наружу — как эвристика имени процесса (VictimFocus матчит GetProcessesByName).
	internal static string NameFromAumid(string? aumid)
	{
		if (string.IsNullOrEmpty(aumid))
		{
			return string.Empty;
		}

		int tail = Math.Max(aumid.LastIndexOf('\\'), aumid.LastIndexOf('/'));
		string name = tail >= 0 ? aumid[(tail + 1)..] : aumid;
		return name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? name[..^4] : name;
	}
}
