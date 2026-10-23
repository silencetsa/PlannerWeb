using System.Globalization;
using Planer.Core;
using Planer.Data;
using PlannerWeb.Models;

namespace PlannerWeb.Services;

public sealed class PlannerService(WebDatabase database, IConfiguration configuration)
{
    private readonly IPlannerClock _clock = new ZonedClock(configuration["Planner:TimeZone"] ?? "Asia/Yekaterinburg");
    public DateOnly Today => _clock.Today;
    public DateTime LocalNow => ((ZonedClock)_clock).Now;
    public async Task<T> WithEngine<T>(string owner, Func<PlannerEngine, T> action)
    {
        await database.Gate.WaitAsync();
        try { return action(new PlannerEngine(new UserPlannerStore(database, owner), _clock)); }
        finally { database.Gate.Release(); }
    }
    public static DateOnly? Date(string? value)
    {
        if (string.IsNullOrEmpty(value)) return null;
        if (!DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) || date.Year is < 1900 or > 2200)
            throw new ArgumentException("Выберите дату от 1900 до 2200 года");
        return date;
    }
    public static object Execute(PlannerEngine e, PlannerCommand c)
    {
        if (c.Text.Length > 10000) throw new ArgumentException("Не более 10000 знаков");
        Guid Id() => c.Id ?? throw new ArgumentException("Не выбран элемент");
        DateOnly RequiredDate() => Date(c.Date) ?? throw new ArgumentException("Выберите дату");
        Repeat RepeatValue() => Enum.IsDefined((Repeat)c.Repeat) ? (Repeat)c.Repeat : throw new ArgumentException("Выберите повторение");
        decimal Money() => BudgetMoney.TryParse(c.Amount, out var amount) ? amount : throw new ArgumentException("Введите сумму, не более двух знаков после запятой");
        DayPart? Part() => c.Part is null ? null : Enum.IsDefined((DayPart)c.Part.Value) ? (DayPart)c.Part.Value : throw new ArgumentException("Выберите часть дня");
        switch (c.Action)
        {
            case "task.save": if (c.Id is { } id) e.EditTask(id, c.Text, c.Important, c.Urgent, RepeatValue()); else e.AddTask(c.Text, c.Important, c.Urgent, Date(c.Date), Part() ?? DayPart.Evening, RepeatValue()); break;
            case "task.toggle": e.ToggleTask(Id()); break;
            case "task.delete": if (c.Scope is < 0 or > 1) throw new ArgumentException("Выберите область удаления"); e.DeleteTask(Id(), (TaskDeleteScope)c.Scope); break;
            case "task.move": if (c.Scope is < 0 or > 1) throw new ArgumentException("Выберите область переноса"); e.MoveTask(Id(), Date(c.Date), Part(), (TaskMoveScope)c.Scope); break;
            case "mood": if (!Enum.IsDefined((Mood)c.Mood)) throw new ArgumentException("Выберите настроение"); e.ToggleMood(RequiredDate(), (Mood)c.Mood); break;
            case "habit.add": e.AddHabit(c.Text, c.Color); break;
            case "habit.finish": e.FinishHabit(Id()); break;
            case "habit.toggle": e.ToggleHabit(Id(), RequiredDate()); break;
            case "payment.save": if (c.Id is { } pid) e.EditPayment(pid, c.Text, Money(), RequiredDate(), RepeatValue(), Date(c.End)); else e.AddPayment(c.Text, Money(), RequiredDate(), RepeatValue(), Date(c.End)); break;
            case "payment.finish": e.FinishPayment(Id()); break;
            case "payment.delete": e.DeletePayment(Id()); break;
            case "payment.toggle": e.TogglePayment(Id(), RequiredDate()); break;
            case "goal": case "note":
                if (!System.Text.RegularExpressions.Regex.IsMatch(c.Key, @"^(week:\d{4}-\d{2}-\d{2}|month:\d{4}-\d{2}|year:\d{4})$")) throw new ArgumentException("Неверный период");
                if (c.Action == "goal") e.SetGoal(c.Key, c.Text); else e.SetNote(c.Key, c.Text); break;
            case "income": e.SetBudgetIncome(Money()); break;
            case "category.save": e.SaveBudgetCategory(c.Id, c.Text, Money()); break;
            case "category.delete": if (!e.State.BudgetCategories.Any(x => x.Id == Id())) throw new KeyNotFoundException(); e.DeleteBudgetCategory(Id()); break;
            case "notifications": e.SetNotificationsEnabled(c.Important); break;
            default: throw new ArgumentException("Неизвестное действие");
        }
        return new { budget = e.Budget, categories = e.State.BudgetCategories.OrderBy(x => x.Order).ToArray() };
    }
}
public sealed class ZonedClock(string zone) : IPlannerClock
{
    private readonly TimeZoneInfo _zone = TimeZoneInfo.FindSystemTimeZoneById(zone);
    public DateTime Now => TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, _zone);
    public DateOnly Today => DateOnly.FromDateTime(Now);
}
