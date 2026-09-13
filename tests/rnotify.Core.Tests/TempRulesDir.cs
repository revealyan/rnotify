namespace rnotify.Core.Tests;

/// <summary>
/// Временный каталог для тестов правил: уникальное имя (Guid), полное удаление
/// в Dispose — тесты не зависят от файловой системы друг друга.
/// </summary>
internal sealed class TempRulesDir : IDisposable
{
	public TempRulesDir()
	{
		DirPath = Path.Combine(Path.GetTempPath(), $"rnotify-tests-{Guid.NewGuid():N}");
		Directory.CreateDirectory(DirPath);
	}

	/// <summary>Каталог (создан).</summary>
	public string DirPath { get; }

	/// <summary>Путь rules.json внутри каталога.</summary>
	public string RulesPath => Path.Combine(DirPath, "rules.json");

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
