namespace Planer.Core;

public sealed record HabitStreakInfo(int Days, DateOnly? Start, DateOnly? LastMarked);

public sealed partial class PlannerEngine
{
    public HabitStreakInfo HabitStreak(Guid id)
    {
        var habit = State.Habits.First(h => h.Id == id);
        bool finished = habit.Ended is { } ending && ending <= Today;
        var end = finished ? (habit.Ended!.Value > DateOnly.MinValue ? habit.Ended.Value.AddDays(-1) : DateOnly.MinValue) : Today;
        var checks = State.HabitChecks.Where(c => c.HabitId == id && c.Date <= Today && habit.ExistsOn(c.Date))
            .Select(c => c.Date).ToHashSet();
        // An unfinished current day does not break yesterday's run.
        if (!finished && !checks.Contains(end) && end > DateOnly.MinValue) end = end.AddDays(-1);
        if (!checks.Contains(end)) return new(0, null, null);
        var start = end;
        while (start > habit.Created && checks.Contains(start.AddDays(-1))) start = start.AddDays(-1);
        return new(end.DayNumber - start.DayNumber + 1, start, end);
    }
}
