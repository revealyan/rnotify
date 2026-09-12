using Microsoft.Win32;

namespace rnotify;

/// <summary>Прежнее значение ключа для точного отката на Exit (доктрина Э1).</summary>
internal sealed record RegistryRestoreItem(
	string SubkeyPath,
	string ValueName,
	object? OldValue,
	RegistryValueKind Kind,
	bool Existed);
