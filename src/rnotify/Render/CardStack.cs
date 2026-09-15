using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using rnotify.Core.Listener;
using rnotify.Core.Rules;

namespace rnotify.Render;

/// <summary>
/// Стек карточек: свежая — снизу в зоне нативного баннера, старые уезжают
/// вверх шагом 140 DIP, максимум 3 (нативное поведение); 4-я вытесняет
/// старейшую. Контракт: все вызовы — на Dispatcher (события фида приходят
/// из пула — MainWindow маршалит). Столы: оверлей-карточка (Topmost,
/// WS_EX_NOACTIVATE, ShowActivated=False) видна на всех виртуальных столах
/// сама — слежение и пересоздание не нужны (канон §9, факт 14.09);
/// EVENT_SYSTEM_DESKTOP_SWITCH на 26200.9168 не прилетает вовсе.
/// </summary>
internal sealed class CardStack : IDisposable
{
	internal const int MaxCards = 3;
	internal const int StackStepDip = 140;
	/// <summary>Потолок подкладки: дальше старейшая таки закрывается (S8.1).</summary>
	internal const int MaxTucked = 8;
	/// <summary>Слизняк каждой подложенной карточки над предыдущей (DIP).</summary>
	internal const int TuckPeekDip = 6;
	/// <summary>Высота карточки (DIP) — для позиционирования подкладки.</summary>
	internal const int CardHeightDip = 124;

	private readonly List<Entry> _cards = []; // порядок: старейшая → свежая
	private readonly List<Entry> _tucked = []; // подкладка под плашку: старейшая → свежая (S8.1)
	private bool _disposed;

	/// <summary>Жизнь стека для панели диагностики.</summary>
	internal event EventHandler<CardTraceEventArgs>? Trace;

	/// <summary>Слот освободился (любое закрытие) — догонялка докладывает следующую (S6.4).</summary>
	internal event EventHandler? SlotFreed;

	/// <summary>Подкладка изменилась — плашка обновляет счётчик (S8.1).</summary>
	internal event EventHandler? TuckedChanged;

	/// <summary>Сколько карточек на экране (Dispatcher-only).</summary>
	internal int VisibleCount => _cards.Count;

	/// <summary>Есть ли на экране догоняющие (плашка «скипнуть всё» живёт, пока есть — S6.4).</summary>
	internal bool HasCatchUp => _cards.Exists(e => e.IsCatchUp) || _tucked.Exists(e => e.IsCatchUp);

	/// <summary>Сколько карточек подложено под плашку (S8.1).</summary>
	internal int TuckedCount => _tucked.Count;

	/// <summary>Закрыть все догоняющие карточки (скип: очередь уже слита, гасим и прочитанное-непрочитанное на экране).</summary>
	internal void CloseCatchUp()
	{
		foreach (Entry entry in _cards.Where(e => e.IsCatchUp).ToArray())
		{
			Detach(entry);
			entry.Card.Dismiss();
		}

		_cards.RemoveAll(e => e.IsCatchUp);
		Relayout();
	}

	// FocusHandler хранится в записи: замыкание на карточку нужно и для отписки.
	private sealed record Entry(CardWindow Card, NotificationRecord Record, EventHandler FocusHandler, bool IsCatchUp = false);

	/// <summary>Показать карточку по show-вердикту (свежая — снизу).</summary>
	internal void Show(NotificationRecord record, RuleVerdict verdict, SenderResolver.SenderInfo sender, bool isCatchUp = false)
	{
		Color? accent = TryParseAccent(verdict.AccentHex, out string? accentError);
		if (accentError is not null)
		{
			Trace?.Invoke(this, new CardTraceEventArgs(
				$"цвет группы «{verdict.AccentHex}» не разобран: {accentError} — карточка без полосы"));
		}

		CardWindow card = new(record, verdict, accent, sender, offsetDip: 0);
		EventHandler handler = (_, _) => OnFocusRequested(card);
		Entry entry = new(card, record, handler, isCatchUp);
		card.FocusRequested += handler;
		card.Closed += OnCardClosed;
		_cards.Add(entry);
		card.Show();
		Trace?.Invoke(this, new CardTraceEventArgs($"показ id {record.Id} ({DescribeTtl(verdict)})"));

		if (_cards.Count > MaxCards)
		{
			// S8.1 (идея владельца): свежая всегда видна; старейшая видимая
			// УХОДИТ ПОД ПЛАШКУ и чуть торчит — ничего не пропадает молча.
			Entry oldest = _cards[0];
			_cards.RemoveAt(0);
			_tucked.Add(oldest);
			if (_tucked.Count > MaxTucked)
			{
				Entry overflow = _tucked[0];
				_tucked.RemoveAt(0);
				Detach(overflow);
				Trace?.Invoke(this, new CardTraceEventArgs($"подложена и забыта id {overflow.Record.Id} (потолок {MaxTucked})"));
				overflow.Card.Dismiss();
			}

			Trace?.Invoke(this, new CardTraceEventArgs($"подложена под плашку id {oldest.Record.Id} (всего {_tucked.Count})"));
		}

		Relayout();
		LayoutTucked();
		RestackZ(); // подложенная НОВЕЕ видимой — без пересборки накрывает её (грабля прогона)
		TuckedChanged?.Invoke(this, EventArgs.Empty);
	}

	// Видимые — наверх z-полосы (подложенные остаются за ними, плашку
	// поднимает MainWindow своим BringToFront).
	private void RestackZ()
	{
		foreach (Entry entry in _cards)
		{
			entry.Card.BringToTop();
		}
	}

	/// <summary>Уведомление снесено из Центра (юзером или вытеснением хранилища) — карточку погасить.</summary>
	internal void CloseById(uint id)
	{
		Entry? entry = _cards.Find(e => e.Record.Id == id);
		if (entry is null)
		{
			return; // Removed без карточки — штатно (mute/delete/backlog)
		}

		Trace?.Invoke(this, new CardTraceEventArgs($"закрыта по Removed id {id}"));
		Detach(entry);
		_cards.Remove(entry);
		entry.Card.Dismiss();
		Relayout();
	}

	// Единая точка закрытия (TTL/клик/✕): оставшиеся сползают вниз. Вытеснение
	// и снос по Id/столам уже сняли запись сами — здесь только честный Close.
	private void OnCardClosed(object? sender, EventArgs e)
	{
		if (sender is not CardWindow card)
		{
			return;
		}

		int index = _cards.FindIndex(x => ReferenceEquals(x.Card, card));
		if (index >= 0)
		{
			Detach(_cards[index]);
			_cards.RemoveAt(index);
			Relayout();
			SlotFreed?.Invoke(this, EventArgs.Empty);
		}
		else
		{
			// Подложенная закрылась сама (TTL/клик): вынимаем из стопки.
			int tuckedIndex = _tucked.FindIndex(x => ReferenceEquals(x.Card, card));
			if (tuckedIndex < 0)
			{
				return;
			}

			Detach(_tucked[tuckedIndex]);
			_tucked.RemoveAt(tuckedIndex);
			LayoutTucked();
			TuckedChanged?.Invoke(this, EventArgs.Empty);
		}

		// Свободный слот — НОВЕЙШАЯ из скрытых съезжает в видимые (правка
		// живого прогона: «закрываю 6 — должна заехать 3, не 1»: свежее
		// релевантнее, принцип «показываем самые свежие»). Вставка в НАЧАЛО
		// списка (она старше выживших видимых) + обязательная пересборка
		// геометрии — без неё карточка «числится видимой» на старом месте.
		if (_cards.Count < MaxCards && _tucked.Count > 0)
		{
			Entry promote = _tucked[^1];
			_tucked.RemoveAt(_tucked.Count - 1);
			_cards.Insert(0, promote);
			Trace?.Invoke(this, new CardTraceEventArgs($"из подкладки наверх id {promote.Record.Id}"));
		}

		Relayout();
		LayoutTucked();
		RestackZ(); // повышенная должна встать ПОВЕРД оставшихся подложенных

		// Событие на КАЖДОМ изменении подкладки: счётчик плашки живой
		// (грабля прогона: после повышения счётчик подвисал).
		TuckedChanged?.Invoke(this, EventArgs.Empty);
	}

	// Подкладка (правка живого прогона S8.1): ВСЕ скрытые в ОДНОЙ точке —
	// слезинка над тройкой, друг за другом (сколько их — скажет плашка);
	// ничего не уезжает вверх при докладке.
	private void LayoutTucked()
	{
		double slot3Top = ScreenPicker.WorkArea().Bottom - 12 - CardHeightDip - 2 * StackStepDip;
		foreach (Entry entry in _tucked)
		{
			AnimateToTop(entry.Card, slot3Top - TuckPeekDip);
		}
	}

	/// <summary>Закрыть всё: видимые и подложенные (скип «скрыть всё», S8.1).</summary>
	internal void CloseAll()
	{
		foreach (Entry entry in _cards.Concat(_tucked).ToArray())
		{
			Detach(entry);
			entry.Card.Dismiss();
		}

		_cards.Clear();
		_tucked.Clear();
		Relayout();
		TuckedChanged?.Invoke(this, EventArgs.Empty);
	}

	private void OnFocusRequested(CardWindow card)
	{
		Entry? entry = _cards.Find(x => ReferenceEquals(x.Card, card));
		_ = VictimFocus.TryFocus(entry?.Record.Aumid, out string detail);
		Trace?.Invoke(this, new CardTraceEventArgs($"фокус (id {entry?.Record.Id}): {detail}")); // отказ эвристики не меняет поведение
	}

	// Пересборка позиций: индекс от свежей (0 — низ зоны) × шаг вверх.
	private void Relayout()
	{
		for (int i = 0; i < _cards.Count; i++)
		{
			AnimateTo(_cards[i].Card, (_cards.Count - 1 - i) * StackStepDip);
		}
	}

	// Сдвиг окна к новому offset: анимация Window.Top 167 мс (CubicEase-out);
	// From берём из эффективного значения — цепочка анимаций не накапливает
	// ошибку. При рывках на layered-окнах — деградация до мгновенного Top.
	private static void AnimateTo(CardWindow card, int offsetDip)
	{
		double target = ScreenPicker.WorkArea().Bottom - card.Height - 12 - offsetDip;
		AnimateToTop(card, target);
	}

	// Абсолютный Top (подкладка позиционируется от плашки, не от низа зоны).
	private static void AnimateToTop(CardWindow card, double target)
	{
		double from = card.Top;
		if (Math.Abs(target - from) < 0.5)
		{
			return;
		}

		DoubleAnimation move = new(from, target, TimeSpan.FromMilliseconds(167)) { EasingFunction = new CubicEase() };
		card.BeginAnimation(Window.TopProperty, move);
	}

	private void Detach(Entry entry)
	{
		entry.Card.FocusRequested -= entry.FocusHandler;
		entry.Card.Closed -= OnCardClosed;
	}

	private static string DescribeTtl(RuleVerdict verdict)
		=> verdict.Ttl is { } ttl ? $"ttl {ttl.TotalSeconds:0}с" : "sticky";

	private static Color? TryParseAccent(string? hex, out string? error)
	{
		error = null;
		if (string.IsNullOrWhiteSpace(hex))
		{
			return null; // catch-all и группы без цвета — без полосы, это не ошибка
		}

		try
		{
			if (ColorConverter.ConvertFromString(hex) is Color color)
			{
				return color;
			}
		}
		catch (FormatException ex)
		{
			error = ex.Message;
		}
		catch (NotSupportedException ex)
		{
			error = ex.Message;
		}

		return null;
	}

	public void Dispose()
	{
		if (_disposed)
		{
			return;
		}

		_disposed = true;
		foreach (Entry entry in _cards)
		{
			Detach(entry);
			entry.Card.Close();
		}

		_cards.Clear();
	}
}
