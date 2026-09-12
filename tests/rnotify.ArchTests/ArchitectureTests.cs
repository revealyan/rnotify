using System.Reflection;
using NetArchTest.Rules;
using rnotify.Core;
using Xunit;

namespace rnotify.ArchTests;

/// <summary>
/// Границы каркаса: ядро не знает UI, приложение знает ядро, направление
/// ссылки только app → Core. «Новая граница = тест на неё» (кит строгости).
/// </summary>
public sealed class ArchitectureTests
{
	private static Assembly CoreAssembly => typeof(ProductIdentity).Assembly;

	private static Assembly AppAssembly => typeof(global::rnotify.App).Assembly;

	[Fact]
	public void Ядро_не_зависит_от_UI_стеков()
	{
		var result = Types.InAssembly(CoreAssembly)
			.ShouldNot()
			.HaveDependencyOnAny(
				"PresentationFramework", "PresentationCore", "WindowsBase",
				"System.Windows.Forms", "Microsoft.WindowsAppSDK", "Microsoft.WinUI")
			.GetResult();

		Assert.True(result.IsSuccessful, "ядро тянет UI-стек: " + string.Join(", ", result.FailingTypeNames ?? []));
	}

	[Fact]
	public void Приложение_ссылается_на_ядро()
	{
		// Рефлексией по ссылке сборки: NetArchTest видит только сигнатурные
		// зависимости (поля/параметры), а ядро пока используется в телах методов.
		var references = AppAssembly.GetReferencedAssemblies()
			.Select(a => a.Name);

		Assert.Contains("rnotify.Core", references, StringComparer.Ordinal);
	}

	[Fact]
	public void Ядро_не_ссылается_на_приложение()
	{
		// Рефлексией, а не NetArchTest: префикс "rnotify" словил бы само приложение.
		var references = CoreAssembly.GetReferencedAssemblies()
			.Select(a => a.Name);

		Assert.DoesNotContain("rnotify", references, StringComparer.Ordinal);
	}

	[Fact]
	public void Типы_ядра_живут_в_пространстве_ядра()
	{
		Assert.All(CoreAssembly.GetTypes(), type =>
			Assert.StartsWith("rnotify.Core", type.Namespace ?? string.Empty, StringComparison.Ordinal));
	}
}
