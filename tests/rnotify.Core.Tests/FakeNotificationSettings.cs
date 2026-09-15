using rnotify.Core.Suppression;

namespace rnotify.Core.Tests;

/// <summary>
/// In-memory куст Settings: подключ = отправитель, значения int; отсутствие
/// ключа/значения = null. ThrowOnNthSenderWrite (1-based) — инъекция сбоя
/// посреди blanket для проверки отката Apply.
/// </summary>
internal sealed class FakeNotificationSettings : INotificationSettingsRegistry
{
	private readonly Dictionary<string, int> _root = [];
	private readonly Dictionary<string, Dictionary<string, int>> _senders = [];
	private readonly Dictionary<string, Dictionary<string, string>> _senderStrings = [];

	/// <summary>Сколько SetSenderDword выполнено (включая бросивший — до инъекции).</summary>
	public int SenderWriteCount;

	/// <summary>Какой по счёту вызов SetSenderDword бросает IOException (null — не бросает).</summary>
	public int? ThrowOnNthSenderWrite;

	/// <summary>Засеять отправителя с прежним ShowBanner (null — значения нет).</summary>
	public void SeedSender(string aumid, int? showBanner)
	{
		Dictionary<string, int> values = [];
		if (showBanner is int value)
		{
			values[NativeBannerSuppressor.ShowBannerName] = value;
		}
		_senders[aumid] = values;
	}

	/// <summary>Засеять глобальное значение в корне (null — значения нет).</summary>
	public void SeedRoot(string valueName, int? value)
	{
		if (value is int v)
		{
			_root[valueName] = v;
		}
		else
		{
			_root.Remove(valueName);
		}
	}

	/// <inheritdoc/>
	public IReadOnlyList<string> GetSenderKeys() => [.. _senders.Keys];

	/// <inheritdoc/>
	public int? GetRootDword(string valueName) => _root.TryGetValue(valueName, out int value) ? value : null;

	/// <inheritdoc/>
	public void SetRootDword(string valueName, int value) => _root[valueName] = value;

	/// <inheritdoc/>
	public void DeleteRootValue(string valueName) => _root.Remove(valueName);

	/// <inheritdoc/>
	public int? GetSenderDword(string senderKey, string valueName)
		=> _senders.TryGetValue(senderKey, out Dictionary<string, int>? values)
			&& values.TryGetValue(valueName, out int value) ? value : null;

	/// <inheritdoc/>
	public void SetSenderDword(string senderKey, string valueName, int value)
	{
		SenderWriteCount++;
		if (ThrowOnNthSenderWrite == SenderWriteCount)
		{
			throw new IOException("инъекция сбоя посреди blanket");
		}

		_senders.TryAdd(senderKey, []);
		_senders[senderKey][valueName] = value;
	}

	/// <inheritdoc/>
	public void DeleteSenderValue(string senderKey, string valueName)
	{
		if (_senders.TryGetValue(senderKey, out Dictionary<string, int>? values))
		{
			values.Remove(valueName);
		}

		if (_senderStrings.TryGetValue(senderKey, out Dictionary<string, string>? strings))
		{
			strings.Remove(valueName);
		}
	}

	/// <inheritdoc/>
	public string? GetSenderString(string senderKey, string valueName)
		=> _senderStrings.TryGetValue(senderKey, out Dictionary<string, string>? values)
			&& values.TryGetValue(valueName, out string? value) && value is not null ? value : null;

	/// <inheritdoc/>
	public void SetSenderString(string senderKey, string valueName, string value)
	{
		_senderStrings.TryAdd(senderKey, []);
		_senderStrings[senderKey][valueName] = value;
	}

	/// <summary>Засеять строковое значение (SoundFile: "" — юзер сам заглушил).</summary>
	public void SeedSenderString(string aumid, string valueName, string value)
	{
		_senderStrings.TryAdd(aumid, []);
		_senderStrings[aumid][valueName] = value;
	}
}
