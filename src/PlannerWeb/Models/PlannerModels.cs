using System.Globalization;
using Planer.Core;

namespace PlannerWeb.Models;

public sealed class PlannerPage(PlannerEngine engine, string screen, DateOnly selected, int yearMode)
{
    public PlannerEngine Engine { get; } = engine;
    public string Screen { get; } = screen;
    public DateOnly Selected { get; } = selected;
    public int YearMode { get; } = yearMode;
    public static CultureInfo Ru { get; } = CultureInfo.GetCultureInfo("ru-RU");
    public DateOnly First => Screen switch { "month" => new(Selected.Year, Selected.Month, 1), "year" => new(Selected.Year, 1, 1), "day" => Engine.Today, _ => Dates.Monday(Selected) };
    public DateOnly Last => Screen switch { "month" => First.AddMonths(1).AddDays(-1), "year" => First.AddYears(1).AddDays(-1), "day" => First, _ => First.AddDays(6) };
    public string Period => Screen switch { "month" => First.ToString("MMMM yyyy", Ru), "year" => First.Year.ToString(), "day" => First.ToString("d MMMM, dddd", Ru), "habits" => "Привычки", "payments" => "Платежи", "finances" => "Финансы", _ => $"{First.ToString("d MMM", Ru)} — {Last.ToString("d MMM", Ru)}" };
    public string Key => Screen switch { "month" => Dates.MonthKey(Selected), "year" => Dates.YearKey(Selected), _ => Dates.WeekKey(Selected) };
    public string GoalTitle => Screen switch { "month" => "Цель месяца", "year" => "Цель года", _ => "Цель недели" };
    public string ParentTitle => Screen == "week" ? "Цель месяца" : "Цель года";
    public string ParentGoal => Engine.Goal(Screen == "week" ? Dates.MonthKey(Selected) : Dates.YearKey(Selected));
    public string Url(string? screen = null, DateOnly? date = null, int? mode = null) => $"/?screen={screen ?? Screen}&date={(date ?? Selected):yyyy-MM-dd}&yearMode={mode ?? YearMode}";
    public DateOnly Step(int n) => Screen switch { "month" => Selected.AddMonths(n), "year" => Selected.AddYears(n), _ => Selected.AddDays(n * 7) };
    public static string Money(decimal amount) => amount.ToString(amount % 1 == 0 ? "N0" : "N2", Ru) + " ₽";
    public static readonly string[] Repeats = ["Нет", "Неделя", "Две недели", "Месяц", "Год"];
    public static readonly string[] Groups = ["Без повторения", "Каждую неделю", "Каждые 2 недели", "Каждый месяц", "Каждый год"];
    public static int FrequencyRank(Repeat repeat) => repeat switch { Repeat.Weekly => 5, Repeat.Fortnightly => 4, Repeat.Monthly => 3, Repeat.Yearly => 2, _ => 1 };
    public static readonly string[] MoodOutline = ["GreenPanel.png", "YellowPanelSoft.png", "RedPanel.png", "BluePanel.png", "PurplePanelWarm.png"];
    public static readonly string[] MoodFilled = ["GreenAccent.png", "YellowPanel.png", "RedAccent.png", "BlueAccent.png", "PurplePanel.png"];
    public static string Iso(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}
public sealed record DayModel(PlannerPage Page, DateOnly Date, bool Compact = false);
public sealed class PlannerCommand
{
    public string Action { get; set; } = "";
    public Guid? Id { get; set; }
    public string? Date { get; set; }
    public int? Part { get; set; }
    public int Repeat { get; set; }
    public bool Important { get; set; }
    public bool Urgent { get; set; }
    public string Text { get; set; } = "";
    public string Amount { get; set; } = "";
    public string? End { get; set; }
    public string Color { get; set; } = "";
    public string Key { get; set; } = "";
    public int Scope { get; set; }
    public int Mood { get; set; }
}
