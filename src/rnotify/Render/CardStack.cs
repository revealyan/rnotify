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

	private readonly List<Entry> _cards = []; // порядок: старейшая → свежая
	private bool _disposed;

	/// <summary>Жизнь стека для панели диагностики.</summary>
	internal event EventHandler<CardTraceEventArgs>? Trace;

	// FocusHandler хранится в записи: замыкание на карточку нужно и для отписки.
	private sealed record Entry(CardWindow Card, NotificationRecord Record, EventHandler FocusHandler);

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
		Entry entry = new(card, record, handler);
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
		foreach (Entry entry in _cards)
		{
			Detach(entry);
			entry.Card.Close();
		}

		_cards.Clear();
	}
}
