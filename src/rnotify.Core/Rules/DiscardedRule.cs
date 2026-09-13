namespace rnotify.Core.Rules;

/// <summary>
/// Правило, выброшенное при компиляции: где сидело и почему. Отчёт наружу, а не
/// молча (грабля старого rnotif: «правило не работает» без диагностики).
/// </summary>
/// <param name="GroupName">Группа выброшенного правила.</param>
/// <param name="RuleName">Имя правила (null — не задано).</param>
/// <param name="Reason">Человекочитаемая причина (кривой regex, неизвестный match/action/click).</param>
public sealed record DiscardedRule(string GroupName, string? RuleName, string Reason);
