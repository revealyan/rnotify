using System.Windows;
using System.Windows.Controls;
using Windows.ApplicationModel;
using rnotify.Core;

namespace rnotify;

/// <summary>
/// Живая проверка MSIX-identity: окно показывает Package.Current.Id и сверяет
/// с <see cref="ProductIdentity"/>. Запуск вне пакета (F5) — так и пишет.
/// </summary>
public partial class MainWindow : Window
{
	public MainWindow()
	{
		InitializeComponent();
		Loaded += (_, _) => ShowIdentity();
	}

	private void ShowIdentity()
	{
		try
		{
			PackageId id = Package.Current.Id;
			AddRow("Режим", "packaged (MSIX)");
			AddRow("Name", id.Name);
			AddRow("Version", $"{id.Version.Major}.{id.Version.Minor}.{id.Version.Build}.{id.Version.Revision}");
			AddRow("Publisher", id.Publisher);
			AddRow("FamilyName", id.FamilyName);
			AddRow(
				"Совпадение с ProductIdentity",
				string.Equals(id.Name, ProductIdentity.Name, StringComparison.Ordinal)
				&& string.Equals(id.Publisher, ProductIdentity.Publisher, StringComparison.Ordinal)
					? "да"
					: "НЕТ — манифест и код разошлись");
		}
		catch (InvalidOperationException)
		{
			// Package.Current вне пакета (unpackage F5-запуск) кидает — это норма.
			AddRow("Режим", "не в пакете (unpackaged, F5)");
		}
	}

	private void AddRow(string key, string value)
	{
		var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 2) };
		row.Children.Add(new TextBlock { Text = key, Width = 180, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
		row.Children.Add(new TextBlock { Text = value, TextWrapping = TextWrapping.Wrap });
		IdentityPanel.Children.Add(row);
	}
}
