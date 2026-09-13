namespace rnotify.Core.Rules;

/// <summary>Реакция на клик по своей карточке (применяет рендер Э4).</summary>
public enum ClickAction
{
	/// <summary>Просто закрыть карточку (дефолт).</summary>
	Close = 0,

	/// <summary>Поднять окно отправителя (эвристика) и закрыть карточку.</summary>
	Focus = 1,
}
