using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using rnotify.Core.Listener;
using rnotify.Core.Rules;

namespace rnotify.Render;

/// <summary>
/// Стек карточек: свежая — снизу в зоне нативного баннера, старые уезжают
/// вверх шагом 140 DIP, максимум 3 (нативное поведение); 4-я вытесняет
/// старейшую. Контракт: все вызовы — на Dispatcher (события фида приходят
/// из пула — MainWindow маршалит). Смена стола: событие приходит ДО
/// завершения переключения, поэтому пересоздание sticky — по дебаунсу
/// 250 мс (канон §9; в спайке дебаунса не было — порт с обязательной
/// добавкой). TTL-карточки не пересоздаются: пересоздание перезапустило бы
/// таймер, продлевая жизнь коротким уведомлениям.
/// </summary>
internal sealed class CardStack : IDisposable
{
	internal const int MaxCards = 3;
	internal const int StackStepDip = 140;
	private static readonly TimeSpan _switchDebounceDelay = TimeSpan.FromMilliseconds(250);

	private readonly List<Entry> _cards = []; // порядок: старейшая → свежая
	private readonly DesktopSwitchWatcher _watcher = new();
	private readonly DispatcherTimer _debounceTimer;
	private readonly Dispatcher _dispatcher;
	private bool _disposed;

	/// <summary>Жизнь стека для панели диагностики.</summary>
	internal event EventHandler<CardTraceEventArgs>? Trace;

	// FocusHandler хранится в записи: замыкание на карточку нужно и для отписки.
	private sealed record Entry(CardWindow Card, NotificationRecord Record, RuleVerdict Verdict, EventHandler FocusHandler);

	public CardStack()
	{
		_dispatcher = Dispatcher.CurrentDispatcher; // стек создаётся на UI-потоке
		_debounceTimer = new DispatcherTimer { Interval = _switchDebounceDelay };
		_debounceTimer.Tick += (_, _) =>
		{
			_debounceTimer.Stop();
			RecreateStickyForDesktop();
		};
		_watcher.DesktopSwitched += OnDesktopSwitched;
	}

	/// <summary>Показать карточку по show-вердикту (свежая — снизу).</summary>
	internal void Show(NotificationRecord record, RuleVerdict verdict)
	{
		Color? accent = TryParseAccent(verdict.AccentHex, out string? accentError);
		if (accentError is not null)
		{
			Trace?.Invoke(this, new CardTraceEventArgs(
				$"цвет группы «{verdict.AccentHex}» не разобран: {accentError} — карточка без полосы"));
		}

		CardWindow card = new(record, verdict, accent, offsetDip: 0);
		EventHandler handler = (_, _) => OnFocusRequested(card);
		Entry entry = new(card, record, verdict, handler);
		card.FocusRequested += handler;
		card.Closed += OnCardClosed;
		_cards.Add(entry);
		card.Show();
		Trace?.Invoke(this, new CardTraceEventArgs($"показ id {record.Id} ({DescribeTtl(verdict)})"));

		if (_cards.Count > MaxCards)
		{
			Entry oldest = _cards[0];
			_cards.RemoveAt(0);
			Detach(oldest);
			Trace?.Invoke(this, new CardTraceEventArgs($"вытеснена id {oldest.Record.Id} (лимит {MaxCards})"));
			oldest.Card.Dismiss();
		}

		Relayout();
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
		if (index < 0)
		{
			return;
		}

		Detach(_cards[index]);
		_cards.RemoveAt(index);
		Relayout();
	}

	private void OnFocusRequested(CardWindow card)
	{
		Entry? entry = _cards.Find(x => ReferenceEquals(x.Card, card));
		_ = VictimFocus.TryFocus(entry?.Record.Aumid, out string detail);
		Trace?.Invoke(this, new CardTraceEventArgs($"фокус (id {entry?.Record.Id}): {detail}")); // отказ эвристики не меняет поведение
	}

	// Событие стола — в потоке насоса; пересоздание строго по дебаунсу.
	private void OnDesktopSwitched(object? sender, EventArgs e)
		=> _ = _dispatcher.BeginInvoke(() =>
		{
			_debounceTimer.Stop();
			_debounceTimer.Start();
		});

	private void RecreateStickyForDesktop()
	{
		List<Entry> sticky = [.. _cards.Where(e => e.Card.IsSticky)];
		if (sticky.Count == 0)
		{
			return;
		}

		foreach (Entry entry in sticky)
		{
			Detach(entry);
			_cards.Remove(entry);
			entry.Card.Close(); // хард, без fade: окно осталось на старом столе, не видно
		}

		Relayout();
		foreach (Entry entry in sticky)
		{
			Show(entry.Record, entry.Verdict); // тот же снапшот вердикта
		}

		Trace?.Invoke(this, new CardTraceEventArgs(
			$"столы: пересоздано sticky {sticky.Count} (дебаунс {_switchDebounceDelay.TotalMilliseconds:0} мс)"));
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
		double from = card.Top;
		double target = SystemParameters.WorkArea.Bottom - card.Height - 12 - offsetDip;
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
		_debounceTimer.Stop();
		_watcher.DesktopSwitched -= OnDesktopSwitched;
		_watcher.Dispose();
		foreach (Entry entry in _cards)
		{
			Detach(entry);
			entry.Card.Close();
		}

		_cards.Clear();
	}
}
