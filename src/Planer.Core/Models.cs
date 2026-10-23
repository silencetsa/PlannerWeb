namespace Planer.Core;

public enum DayPart { Morning, Afternoon, Evening }
public enum TaskKind { UrgentImportant, UrgentUnimportant, NotUrgentImportant, NotUrgentUnimportant }
public enum Mood { Happy, Good, Angry, Sad, Neutral }
public enum Repeat { None, Weekly, Fortnightly, Monthly, Yearly }

public sealed class PlannerTask
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Description { get; set; } = "";
    public bool Important { get; set; }
    public bool Urgent { get; set; }
    public bool Completed { get; set; }
    public DateOnly? Date { get; set; }
    public DayPart Part { get; set; } = DayPart.Evening;
    public long Order { get; set; }
    public Repeat Repeat { get; set; }
    public Guid? SeriesId { get; set; }
    public long? OccurrenceIndex { get; set; }
    public TaskKind Kind => (Urgent, Important) switch
    {
        (true, true) => TaskKind.UrgentImportant,
        (true, false) => TaskKind.UrgentUnimportant,
        (false, true) => TaskKind.NotUrgentImportant,
        _ => TaskKind.NotUrgentUnimportant
    };
}

public sealed class Habit
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public string Color { get; set; } = Palette.Elements[3];
    public DateOnly Created { get; set; }
    public long Order { get; set; }
    // Exclusive: finishing today removes today's indicator and all future ones.
    public DateOnly? Ended { get; set; }
    public bool ExistsOn(DateOnly date) => date >= Created && (Ended is null || date < Ended);
    public int ActiveDays(DateOnly today) => Math.Max(0, (Ended ?? today.AddDays(1)).DayNumber - Created.DayNumber);
}

public sealed class HabitCheck
{
    public Guid HabitId { get; set; }
    public DateOnly Date { get; set; }
}

public sealed class PaymentRevision
{
    public DateOnly EffectiveFrom { get; set; } = DateOnly.MinValue;
    public string Name { get; set; } = "";
    public decimal Amount { get; set; }
    public DateOnly Start { get; set; }
    public Repeat Repeat { get; set; }
    public DateOnly? End { get; set; }
    public bool OccursOn(DateOnly date)
    {
        if (date < Start || (End is not null && date > End)) return false;
        return Recurrence.OccursOn(Start, Repeat, date);
    }
}

public sealed class Payment
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateOnly? DeletedFrom { get; set; }
    public List<PaymentRevision> Revisions { get; set; } = [];
    public PaymentRevision? RevisionOn(DateOnly date) => Revisions
        .Where(x => x.EffectiveFrom <= date).OrderByDescending(x => x.EffectiveFrom).FirstOrDefault();
    public bool OccursOn(DateOnly date) => (DeletedFrom is null || date < DeletedFrom) && RevisionOn(date)?.OccursOn(date) == true;
    public bool IsActive(DateOnly today) => (DeletedFrom is null || today < DeletedFrom)
        && RevisionOn(today) is { } r && (r.End is null || r.End > today);
}

public sealed class PaymentCheck
{
    public Guid PaymentId { get; set; }
    public DateOnly Date { get; set; }
}

public sealed record PaymentOccurrence(Guid PaymentId, DateOnly Date, string Name, decimal Amount, bool Completed);
public sealed record HabitIndicator(Guid HabitId, string Name, string Color, bool Completed);
public sealed record MatrixCounts(int[] Scheduled, int[] Unscheduled);

public sealed class PlannerState
{
    public DateOnly? LastRolloverWeek { get; set; }
    public bool NotificationTrackingInitialized { get; set; }
    public bool NotificationsEnabled { get; set; } = true;
    public List<PlannerNotification> Notifications { get; set; } = [];
    public List<PlannerTask> Tasks { get; set; } = [];
    public List<TaskSeries> TaskSeries { get; set; } = [];
    public List<Habit> Habits { get; set; } = [];
    public List<HabitCheck> HabitChecks { get; set; } = [];
    public List<Payment> Payments { get; set; } = [];
    public List<PaymentCheck> PaymentChecks { get; set; } = [];
    public Dictionary<DateOnly, Mood> Moods { get; set; } = [];
    public Dictionary<string, string> Goals { get; set; } = [];
    public Dictionary<string, string> Notes { get; set; } = [];
    public decimal BudgetIncome { get; set; }
    public List<BudgetCategory> BudgetCategories { get; set; } = [];
}

public static class Palette
{
    public static readonly string[] Elements = ["#FF6961", "#FFB480", "#FAF03D", "#66E37E", "#08CAD1", "#59ADF6", "#9D94FF", "#C780E8"];
    public static readonly string[] PaleElements = ["#FFDFDD", "#FFEFE3", "#FDFBD6", "#E0F8E5", "#CAF4F5", "#DBEEFD", "#EAE8FF", "#F3E3FA"];
    public static string PaleHabitColor(string color)
    {
        var index = Array.FindIndex(Elements, c => c.Equals(HabitColor(color), StringComparison.OrdinalIgnoreCase));
        return index >= 0 ? PaleElements[index] : "#F5F5F5";
    }
    // Older databases used the text palette. Resolve its six darker colors for display
    // without rewriting habits, activation dates or historical check marks.
    public static string HabitColor(string storedColor) => storedColor.ToUpperInvariant() switch
    {
        "#CC0A00" => Elements[0], "#FF7C05" => Elements[1], "#FFAA00" => Elements[2],
        "#0DBA30" => Elements[3], "#00ADA2" => Elements[4], "#0B7ADA" => Elements[5],
        _ => storedColor
    };
    public static readonly string[] MoodColors = ["#66E37E", "#F7EF51", "#FF6961", "#56AAF9", "#C780E8"];
    public static readonly string[] TaskBackgrounds = ["#FFDFDD", "#FDFBD6", "#E0F8E5", "#DBEEFD"];
    public static readonly string[] TaskForegrounds = ["#CC0A00", "#FFAA00", "#0DBA30", "#0B7ADA"];
}

public static class Dates
{
    public static DateOnly Monday(DateOnly date) => date.AddDays(-(((int)date.DayOfWeek + 6) % 7));
    public static string WeekKey(DateOnly date) => $"week:{Monday(date):yyyy-MM-dd}";
    public static string MonthKey(DateOnly date) => $"month:{date:yyyy-MM}";
    public static string YearKey(DateOnly date) => $"year:{date.Year}";
}

public interface IPlannerStore
{
    PlannerState Load();
    void Save(PlannerState state);
}

public interface IPlannerClock { DateOnly Today { get; } }
public sealed class SystemPlannerClock : IPlannerClock
{
    public DateOnly Today => DateOnly.FromDateTime(DateTime.Now);
}
