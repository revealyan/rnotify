namespace rnotify.Core.Suppression;

/// <summary>
/// Снимок состояния реестра до применения формулы Э1: null = значения не было.
/// Живёт в маркере suppression.json — по нему строится и штатный возврат, и
/// авторепейр после краха (один код, <see cref="NativeBannerSuppressor"/>).
/// </summary>
/// <param name="GlobalToastsEnabled">Прежний NOC_GLOBAL_SETTING_TOASTS_ENABLED в корне куста.</param>
/// <param name="AppShowBanner">Прежние ShowBanner по AUMID (включая дозаписанных blanket'ом на лету).</param>
/// <param name="AppSoundFile">Прежние SoundFile по AUMID (S8.1: звуковой blanket; null — не было, "" — юзер сам заглушил).</param>
public sealed record SuppressionSnapshot(
	int? GlobalToastsEnabled,
	IReadOnlyDictionary<string, int?> AppShowBanner,
	IReadOnlyDictionary<string, string?> AppSoundFile);
