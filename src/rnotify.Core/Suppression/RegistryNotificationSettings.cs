using Microsoft.Win32;

namespace rnotify.Core.Suppression;

/// <summary>
/// Прод-реализация над HKCU: перечисление отправителей, чтение/запись/удаление
/// DWORD в корне куста и у отправителей. Отправителей перечисляем без записи:
/// blanket в <see cref="NativeBannerSuppressor"/> открывает ключи сам.
/// </summary>
public sealed class RegistryNotificationSettings : INotificationSettingsRegistry
{
	private const string _rootPath = @"Software\Microsoft\Windows\CurrentVersion\Notifications\Settings";

	/// <inheritdoc/>
	public IReadOnlyList<string> GetSenderKeys()
	{
		using RegistryKey? root = Registry.CurrentUser.OpenSubKey(_rootPath);
		return root?.GetSubKeyNames() ?? [];
	}

	/// <inheritdoc/>
	public int? GetRootDword(string valueName)
	{
		using RegistryKey? root = Registry.CurrentUser.OpenSubKey(_rootPath);
		return root?.GetValue(valueName) is int value ? value : null;
	}

	/// <inheritdoc/>
	public void SetRootDword(string valueName, int value)
	{
		using RegistryKey root = Registry.CurrentUser.CreateSubKey(_rootPath, writable: true);
		root.SetValue(valueName, value, RegistryValueKind.DWord);
	}

	/// <inheritdoc/>
	public void DeleteRootValue(string valueName)
	{
		using RegistryKey? root = Registry.CurrentUser.OpenSubKey(_rootPath, writable: true);
		root?.DeleteValue(valueName, throwOnMissingValue: false);
	}

	/// <inheritdoc/>
	public int? GetSenderDword(string senderKey, string valueName)
	{
		using RegistryKey? root = Registry.CurrentUser.OpenSubKey(_rootPath);
		using RegistryKey? sender = root?.OpenSubKey(senderKey);
		return sender?.GetValue(valueName) is int value ? value : null;
	}

	/// <inheritdoc/>
	public void SetSenderDword(string senderKey, string valueName, int value)
	{
		using RegistryKey root = Registry.CurrentUser.CreateSubKey(_rootPath, writable: true);
		using RegistryKey sender = root.CreateSubKey(senderKey, writable: true);
		sender.SetValue(valueName, value, RegistryValueKind.DWord);
	}

	/// <inheritdoc/>
	public void DeleteSenderValue(string senderKey, string valueName)
	{
		using RegistryKey? root = Registry.CurrentUser.OpenSubKey(_rootPath, writable: true);
		using RegistryKey? sender = root?.OpenSubKey(senderKey, writable: true);
		sender?.DeleteValue(valueName, throwOnMissingValue: false);
	}

	/// <inheritdoc/>
	public string? GetSenderString(string senderKey, string valueName)
	{
		using RegistryKey? root = Registry.CurrentUser.OpenSubKey(_rootPath);
		using RegistryKey? sender = root?.OpenSubKey(senderKey);
		return sender?.GetValue(valueName) as string;
	}

	/// <inheritdoc/>
	public void SetSenderString(string senderKey, string valueName, string value)
	{
		using RegistryKey root = Registry.CurrentUser.CreateSubKey(_rootPath, writable: true);
		using RegistryKey sender = root.CreateSubKey(senderKey, writable: true);
		sender.SetValue(valueName, value, RegistryValueKind.String);
	}
}
