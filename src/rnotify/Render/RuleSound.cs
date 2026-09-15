using System.Runtime.InteropServices;

namespace rnotify.Render;

/// <summary>
/// Звук правила (S8.1, архитектура §9): путь к .wav, играем PlaySound
/// (winmm) асинхронно; нет файла/не задан — тишина (SND_NODEFAULT — без
/// системного «динь» вместо пропавшего файла). Вызов на Dispatcher при
/// показе карточки; winmm сам дергает волновое устройство мимо очереди UI.
/// </summary>
internal static class RuleSound
{
	private const uint _sndFilename = 0x00020000;
	private const uint _sndAsync = 0x0001;
	private const uint _sndNodefault = 0x0002;

	/// <summary>Проиграть wav вердикта; null/пусто/битый путь — молча.</summary>
	internal static void Play(string? path)
	{
		if (string.IsNullOrWhiteSpace(path))
		{
			return;
		}

		_ = PlaySoundW(path, nint.Zero, _sndFilename | _sndAsync | _sndNodefault);
	}

	[DllImport("winmm.dll", SetLastError = true, EntryPoint = "PlaySoundW")]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static extern bool PlaySoundW([MarshalAs(UnmanagedType.LPWStr)] string file, nint module, uint flags);
}
