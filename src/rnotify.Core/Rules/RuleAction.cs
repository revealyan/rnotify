namespace rnotify.Core.Rules;

/// <summary>Действие правила над уведомлением.</summary>
public enum RuleAction
{
	/// <summary>Показать своим рендером (Э4); до него — только пометка в панели.</summary>
	Show = 0,

	/// <summary>Не показывать; копия в Центре уведомлений живёт.</summary>
	Mute = 1,

	/// <summary>Снести из хранилища Центра и не показывать.</summary>
	Delete = 2,
}
