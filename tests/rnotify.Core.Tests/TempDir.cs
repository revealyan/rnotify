namespace rnotify.Core.Tests;

/// <summary>
/// Временный каталог для тестов вне правил (маркер Э1, settings.json):
/// уникальное имя (Guid), полное удаление в Dispose.
/// </summary>
internal sealed class TempDir : IDisposable
{
	public TempDir()
	{
		DirPath = Path.Combine(Path.GetTempPath(), $"rnotify-tests-{Guid.NewGuid():N}");
		Directory.CreateDirectory(DirPath);
	}

	/// <summary>Каталог (создан).</summary>
	public string DirPath { get; }

	/// <summary>Путь именованного файла внутри каталога.</summary>
	public string PathFor(string fileName) => Path.Combine(DirPath, fileName);

	public void Dispose()
	{
		try
		{
			Directory.Delete(DirPath, recursive: true);
		}
		catch (IOException)
		{
			// Занятый антивирусом файл не роняет тест — temp и так почистится системой.
		}
	}
}
