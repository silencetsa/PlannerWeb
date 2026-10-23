namespace Planer.Core;

/// <summary>Anchor-based recurrence: short months never change the original day.</summary>
public static class Recurrence
{
    public static DateOnly? DateAt(DateOnly start, Repeat repeat, long index)
    {
        if (index < 0) return null;
        if (repeat == Repeat.None) return index == 0 ? start : null;
        if (repeat is Repeat.Weekly or Repeat.Fortnightly)
        {
            long day = start.DayNumber + index * (repeat == Repeat.Weekly ? 7L : 14L);
            return day <= DateOnly.MaxValue.DayNumber && day >= start.DayNumber ? DateOnly.FromDayNumber((int)day) : null;
        }
        long month = (start.Year - 1L) * 12 + start.Month - 1 + index * (repeat == Repeat.Monthly ? 1L : 12L);
        if (month > 9999L * 12 - 1 || month < 0) return null;
        int year = (int)(month / 12) + 1, m = (int)(month % 12) + 1;
        return new(year, m, Math.Min(start.Day, DateTime.DaysInMonth(year, m)));
    }

    public static IEnumerable<(long Index, DateOnly Date)> Between(DateOnly start, Repeat repeat, DateOnly first, DateOnly last)
    {
        if (last < start || last < first) yield break;
        long index = repeat switch
        {
            Repeat.Weekly => Math.Max(0, (first.DayNumber - start.DayNumber) / 7),
            Repeat.Fortnightly => Math.Max(0, (first.DayNumber - start.DayNumber) / 14),
            Repeat.Monthly => Math.Max(0, (first.Year - start.Year) * 12 + first.Month - start.Month),
            Repeat.Yearly => Math.Max(0, first.Year - start.Year),
            _ => 0
        };
        for (; DateAt(start, repeat, index) is { } date && date <= last; index++)
            if (date >= first) yield return (index, date);
    }

    public static bool OccursOn(DateOnly start, Repeat repeat, DateOnly date) => Between(start, repeat, date, date).Any();
}

/// <summary>A schedule segment. Splitting a chain changes future dates without rewriting earlier ones.</summary>
public sealed class TaskSeries
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ChainId { get; set; } = Guid.NewGuid();
    public DateOnly? Start { get; set; }
    public Repeat Repeat { get; set; }
    public DayPart Part { get; set; }
    public string Description { get; set; } = "";
    public bool Important { get; set; }
    public bool Urgent { get; set; }
    public long FirstIndex { get; set; }
    // A part-only change can retain the original calendar anchor and start later in its sequence.
    public long? ActiveFromIndex { get; set; }
    public long BeginsAt => ActiveFromIndex ?? FirstIndex;
    // Exclusive. Null means the schedule continues indefinitely.
    public long? EndIndex { get; set; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public HashSet<long>? ExcludedIndices { get; set; }
}

public enum TaskMoveScope { Occurrence, ThisAndFuture }
public enum TaskDeleteScope { Occurrence, ThisAndFuture }
