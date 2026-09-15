using System.Collections.ObjectModel;
using System.IO;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using rnotify.Core.Rules;

namespace rnotify.Rules;

/// <summary>
/// Редактор rules.json (S7.3). Слева группы (вкл/имя/цвет — с живым превью
/// акцента), справа правила выбранной группы. Значения редактируются
/// напрямую в json-контрактах (POCO без INPC — биндинги пишут по change);
/// структурные операции (добавить/убрать) пересобирают ItemsSource. Сохранение
/// — RulesStore.Save (атомарно), хот-релоад движка подхватывает файл, панель
/// скажет «перезагружено»/выбраковки. Валидация значений — на движке
/// (доктрина S3.1: кривое правило = выбраковка со строкой, не блок).
/// </summary>
public partial class RulesWindow : Window
{
	private readonly RulesStore _store;
	private RulesConfig _config = new();

	// Группа с живым превью цвета (сам конфиг — POCO без INPC).
	public sealed class GroupItem : INotifyPropertyChanged
	{
		private string? _name;
		private string? _color;
		private bool _enabled = true;

		public required ObservableCollection<RuleConfig> Rules { get; init; }

		public string? Name { get => _name; set { _name = value; Changed(nameof(Name)); } }

		public bool Enabled { get => _enabled; set { _enabled = value; Changed(nameof(Enabled)); } }

		public string? Color
		{
			get => _color;
			set
			{
				_color = value;
				Changed(nameof(Color));
				Changed(nameof(ColorBrush));
			}
		}

		public Color ColorBrush => TryParse(Color, out Color parsed) ? parsed : Colors.Transparent;

		public event PropertyChangedEventHandler? PropertyChanged;

		private void Changed(string property) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));

		public static bool TryParse(string? hex, out Color color)
		{
			try
			{
				color = (Color)ColorConverter.ConvertFromString(hex ?? string.Empty);
				return true;
			}
			catch (FormatException)
			{
				color = Colors.Transparent;
				return false;
			}
			catch (NotSupportedException)
			{
				color = Colors.Transparent;
				return false;
			}
		}
	}

	private RulesWindow(RulesStore store)
	{
		_store = store;
		InitializeComponent();
		Title = Strings.RulesTitle;
		AddGroupButton.Content = Strings.RulesAddGroup;
		RemoveGroupButton.Content = Strings.RulesRemoveGroup;
		AddRuleButton.Content = Strings.RulesAddRule;
		RemoveRuleButton.Content = Strings.RulesRemoveRule;
		SaveButton.Content = Strings.RulesSave;
		CancelButton.Content = Strings.RulesCancel;
		Reload();
	}

	/// <summary>Одна на приложение — открыть/поднять.</summary>
	internal static void ShowSingle(RulesStore store)
	{
		RulesWindow? existing = Application.Current.Windows.OfType<RulesWindow>().FirstOrDefault();
		if (existing is not null)
		{
			existing.Show();
			existing.Activate();
			return;
		}

		new RulesWindow(store).Show();
	}

	private void Reload()
	{
		_config = _store.LoadOrDefault().Config ?? new RulesConfig();
		GroupsList.ItemsSource = new ObservableCollection<GroupItem>(
			_config.Groups.Select(g => new GroupItem
			{
				Name = g.Name,
				Enabled = g.Enabled ?? true,
				Color = g.Color,
				Rules = new ObservableCollection<RuleConfig>(g.Rules),
			}));
		if (GroupsList.Items.Count > 0)
		{
			GroupsList.SelectedIndex = 0;
		}

		RebuildRulesPanel();
	}

	private void OnGroupSelected(object sender, SelectionChangedEventArgs e) => RebuildRulesPanel();

	// Правила выбранной группы: панель пересобирается (структурные операции
	// проще полного пересбора, чем точечная синхронизация POCO-списков).
	private void RebuildRulesPanel()
	{
		RulesPanel.Children.Clear();
		if (GroupsList.SelectedItem is not GroupItem group)
		{
			RulesPanel.Children.Add(new TextBlock { Text = Strings.RulesNoGroup, Foreground = Brushes.Gray, Margin = new Thickness(4) });
			return;
		}

		if (group.Rules.Count == 0)
		{
			RulesPanel.Children.Add(new TextBlock { Text = Strings.RulesNoRule, Foreground = Brushes.Gray, Margin = new Thickness(4) });
		}

		foreach (RuleConfig rule in group.Rules)
		{
			RulesPanel.Children.Add(BuildRuleCard(rule));
		}
	}

	// Карточка правила: сетка подпись+ввод, 4 ряда; компактно, без MVVM.
	private static Border BuildRuleCard(RuleConfig rule)
	{
		Grid fields = new();
		for (int row = 0; row < 4; row++)
		{
			fields.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
		}

		AddField(fields, 0, 0, "name", TextBoxFor(rule.Name, v => rule.Name = v));
		AddField(fields, 0, 2, "app", TextBoxFor(rule.App, v => rule.App = v));
		AddField(fields, 1, 0, "title", TextBoxFor(rule.Title, v => rule.Title = v));
		AddField(fields, 1, 2, "body", TextBoxFor(rule.Body, v => rule.Body = v));
		AddCombo(fields, 2, 0, "match", [null, "regex", "contains", "exact"], rule.Match, v => rule.Match = v);
		AddCombo(fields, 2, 1, "action", [null, "show", "mute", "delete"], rule.Action, v => rule.Action = v);
		AddField(fields, 2, 2, "ttl", TextBoxFor(rule.Ttl, v => rule.Ttl = v));
		AddCombo(fields, 3, 0, "click", [null, "close", "focus"], rule.Click, v => rule.Click = v);
		AddCheck(fields, 3, 1, "killNative", rule.KillNative ?? false, v => rule.KillNative = v);
		AddCheck(fields, 3, 2, "overFullscreen", rule.OverFullscreen ?? false, v => rule.OverFullscreen = v);

		Border card = new()
		{
			BorderBrush = new SolidColorBrush(Color.FromArgb(0x44, 0xFF, 0xFF, 0xFF)),
			BorderThickness = new Thickness(1),
			CornerRadius = new CornerRadius(6),
			Padding = new Thickness(8),
			Margin = new Thickness(0, 0, 0, 8),
			Child = fields,
		};
		return card;
	}

	// TextBox с проводкой записи (пустое → null: json чище, дефолт двигка);
	// грабля первой редакции: колбэк не подключался — правки терялись.
	private static TextBox TextBoxFor(string? value, Action<string?> write)
	{
		TextBox box = new() { Text = value ?? string.Empty, Margin = new Thickness(0, 1, 8, 4) };
		box.TextChanged += (_, _) => write(box.Text.Length == 0 ? null : box.Text);
		return box;
	}

	private static ComboBox ComboFor(IReadOnlyList<string?> options, string? current, Action<string?> write)
	{
		ComboBox combo = new() { Margin = new Thickness(0, 1, 8, 4) };
		foreach (string? option in options)
		{
			_ = combo.Items.Add(option ?? $"({Strings.RulesMatchAuto})");
			if (string.Equals(option, current, StringComparison.Ordinal))
			{
				combo.SelectedIndex = combo.Items.Count - 1;
			}
		}

		combo.SelectionChanged += (_, _) =>
		{
			int index = combo.SelectedIndex;
			write(index < 0 ? null : options[index]);
		};
		return combo;
	}

	private static void AddField(Grid grid, int row, int col, string label, FrameworkElement input)
	{
		grid.Children.Add(new TextBlock { Text = label, Foreground = Brushes.Gray, FontSize = 11, VerticalAlignment = VerticalAlignment.Center });
		Grid.SetRow(grid.Children[^1], row);
		Grid.SetColumn(grid.Children[^1], col * 2);
		grid.Children.Add(input);
		Grid.SetRow(input, row);
		Grid.SetColumn(input, col * 2 + 1);
		grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
		grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
	}

	private static void AddCombo(Grid grid, int row, int col, string label, IReadOnlyList<string?> options, string? current, Action<string?> write)
		=> AddField(grid, row, col, label, ComboFor(options, current, write));

	private static void AddCheck(Grid grid, int row, int col, string label, bool value, Action<bool?> write)
	{
		CheckBox check = new() { Content = label, IsChecked = value, Margin = new Thickness(0, 2, 8, 4) };
		check.Checked += (_, _) => write(true);
		check.Unchecked += (_, _) => write(false);
		grid.Children.Add(check);
		Grid.SetRow(check, row);
		Grid.SetColumn(check, col * 2);
	}

	private void OnAddGroup(object sender, RoutedEventArgs e)
	{
		if (GroupsList.ItemsSource is ObservableCollection<GroupItem> groups)
		{
			GroupItem group = new() { Name = "Новая группа", Rules = [] };
			groups.Add(group);
			GroupsList.SelectedItem = group;
		}
	}

	private void OnRemoveGroup(object sender, RoutedEventArgs e)
	{
		if (GroupsList.ItemsSource is ObservableCollection<GroupItem> groups && GroupsList.SelectedItem is GroupItem selected)
		{
			int index = groups.IndexOf(selected);
			groups.Remove(selected);
			GroupsList.SelectedIndex = Math.Min(index, groups.Count - 1);
			RebuildRulesPanel();
		}
	}

	private void OnAddRule(object sender, RoutedEventArgs e)
	{
		if (GroupsList.SelectedItem is GroupItem group)
		{
			RuleConfig rule = new() { Name = "rule" };
			group.Rules.Add(rule);
			RebuildRulesPanel();
		}
	}

	private void OnRemoveRule(object sender, RoutedEventArgs e)
	{
		if (GroupsList.SelectedItem is GroupItem group && group.Rules.Count > 0)
		{
			group.Rules.RemoveAt(group.Rules.Count - 1); // v1: снимаем последнюю (выбора строк нет — карта целиком редактируется)
			RebuildRulesPanel();
		}
	}

	private void OnSave(object sender, RoutedEventArgs e)
	{
		// Снимок UI → конфиг (группы-обёртки → json-контракты).
		List<RuleGroupConfig> groups = [];
		if (GroupsList.ItemsSource is ObservableCollection<GroupItem> items)
		{
			foreach (GroupItem item in items)
			{
				groups.Add(new RuleGroupConfig
				{
					Name = item.Name,
					Enabled = item.Enabled,
					Color = item.Color,
					Rules = item.Rules.ToList(),
				});
			}
		}

		RulesConfig config = new() { Groups = groups };
		try
		{
			_store.Save(config);
			StatusText.Text = Strings.RulesSaved; // хот-релоад скажет в панели сам
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
		{
			StatusText.Text = ex.Message;
		}
	}

	private void OnCancel(object sender, RoutedEventArgs e) => Close();

	private void OnWindowKeyDown(object sender, KeyEventArgs e)
	{
		if (e.Key == Key.Escape)
		{
			Close();
		}
	}
}
