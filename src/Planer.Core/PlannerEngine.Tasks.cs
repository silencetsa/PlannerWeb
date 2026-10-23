namespace Planer.Core;

public sealed partial class PlannerEngine
{
    public PlannerTask? TaskById(Guid id) => State.Tasks.FirstOrDefault(t => t.Id == id);
    public bool IsScheduledSeries(Guid id) => TaskById(id) is { } task && SeriesFor(task) is { Start: not null };

    /// <summary>Only requested dates are materialized; moving one instance cannot regenerate it at its old date.</summary>
    public void EnsureTaskRange(DateOnly first, DateOnly last)
    {
        var missing = MissingOccurrences(first, last).ToList();
        if (missing.Count > 0) Commit(() => State.Tasks.AddRange(missing), notify: false);
    }

    private IEnumerable<PlannerTask> MissingOccurrences(DateOnly first, DateOnly last)
    {
        var existing = State.Tasks.Where(t => t.SeriesId is not null && t.OccurrenceIndex is not null)
            .Select(t => (t.SeriesId, t.OccurrenceIndex)).ToHashSet();
        long order = State.Tasks.Select(t => t.Order).DefaultIfEmpty(0).Max();
        foreach (var series in State.TaskSeries.Where(s => s.Start is not null && (s.EndIndex is null || s.EndIndex > s.BeginsAt)))
        {
            foreach (var (localIndex, date) in Recurrence.Between(series.Start!.Value, series.Repeat, first, last))
            {
                long index = series.FirstIndex + localIndex;
                if (series.EndIndex is { } end && index >= end) break;
                if (index < series.BeginsAt) continue;
                if (existing.Contains((series.Id, index)) || series.ExcludedIndices?.Contains(index) == true) continue;
                yield return new PlannerTask { SeriesId = series.Id, OccurrenceIndex = index, Date = date, Part = series.Part,
                    Description = series.Description, Important = series.Important, Urgent = series.Urgent, Repeat = series.Repeat, Order = ++order };
            }
        }
    }

    private void MaterializeTasks(DateOnly first, DateOnly last) => State.Tasks.AddRange(MissingOccurrences(first, last).ToList());
    private TaskSeries? SeriesFor(PlannerTask task) => State.TaskSeries.FirstOrDefault(s => s.Id == task.SeriesId);
    private bool BelongsToChain(PlannerTask task, Guid chainId) => SeriesFor(task)?.ChainId == chainId;
    private static void ValidateRepeat(Repeat repeat)
    {
        if (!Enum.IsDefined(repeat)) throw new ArgumentException("Выберите повторение из списка");
    }

    private void StartTaskSeries(PlannerTask task, Guid? chainId = null, long firstIndex = 0)
    {
        var series = new TaskSeries { ChainId = chainId ?? Guid.NewGuid(), Start = task.Date, Repeat = task.Repeat, Part = task.Part,
            Description = task.Description, Important = task.Important, Urgent = task.Urgent, FirstIndex = firstIndex };
        var excluded = State.TaskSeries.Where(s => s.ChainId == series.ChainId).SelectMany(s => s.ExcludedIndices ?? [])
            .Where(index => index >= firstIndex).ToHashSet();
        if (excluded.Count > 0) series.ExcludedIndices = excluded;
        State.TaskSeries.Add(series); task.SeriesId = series.Id; task.OccurrenceIndex = firstIndex;
    }

    private void StopChainFrom(Guid chainId, long index)
    {
        foreach (var segment in State.TaskSeries.Where(s => s.ChainId == chainId))
            segment.EndIndex = Math.Min(segment.EndIndex ?? long.MaxValue, Math.Max(segment.BeginsAt, index));
    }

    private void ChangeTaskRepeat(PlannerTask task, Repeat repeat)
    {
        var series = SeriesFor(task); long index = task.OccurrenceIndex ?? 0;
        if (series is not null)
        {
            StopChainFrom(series.ChainId, index);
            State.Tasks.RemoveAll(t => t.Id != task.Id && BelongsToChain(t, series.ChainId) && t.OccurrenceIndex >= index);
        }
        task.Repeat = repeat; task.SeriesId = null; task.OccurrenceIndex = null;
        if (repeat != Repeat.None && (task.Date is not null || series is not null)) StartTaskSeries(task, series?.ChainId, index);
    }

    public void MoveTask(Guid id, DateOnly? date, DayPart? part = null, TaskMoveScope scope = TaskMoveScope.Occurrence) => Commit(() =>
    {
        var task = FindTask(id); var series = SeriesFor(task);
        var nextPart = part ?? (task.Date is null ? DayPart.Evening : task.Part);
        if (scope == TaskMoveScope.ThisAndFuture && series is not null && task.OccurrenceIndex is { } index)
        {
            bool keepCalendarAnchor = date is not null && date == task.Date && series.Start is { } anchor
                && Recurrence.DateAt(anchor, series.Repeat, index - series.FirstIndex) == date;
            // Keep existing instance state (including completion), but re-anchor its future schedule.
            var future = State.Tasks.Where(t => BelongsToChain(t, series.ChainId) && t.OccurrenceIndex >= index).ToList();
            StopChainFrom(series.ChainId, index);
            task.Date = date; task.Part = nextPart;
            StartTaskSeries(task, series.ChainId, index);
            var next = SeriesFor(task)!;
            if (keepCalendarAnchor)
            { next.Start = series.Start; next.FirstIndex = series.FirstIndex; next.ActiveFromIndex = index; }
            foreach (var instance in future.Where(t => t.Id != task.Id))
            {
                if (date is null) { State.Tasks.Remove(instance); continue; }
                instance.Date = Recurrence.DateAt(next.Start!.Value, next.Repeat, instance.OccurrenceIndex!.Value - next.FirstIndex);
                if (instance.Date is null) { State.Tasks.Remove(instance); continue; }
                instance.SeriesId = next.Id; instance.Part = nextPart;
            }
        }
        else
        {
            task.Part = nextPart; task.Date = date;
            if (task.Repeat != Repeat.None && date is not null)
            {
                if (series is null) StartTaskSeries(task);
                else if (series.Start is null) { series.Start = date; series.Part = nextPart; }
            }
        }
        task.Order = State.Tasks.Select(x => x.Order).DefaultIfEmpty(0).Max() + 1;
    });
}
