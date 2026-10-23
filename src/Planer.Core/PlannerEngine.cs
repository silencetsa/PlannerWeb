namespace Planer.Core;

/// <summary>All planner rules. Independent of WinUI, deterministic with an injected clock.</summary>
public sealed partial class PlannerEngine
{
    private readonly IPlannerStore _store;
    private readonly IPlannerClock _clock;
    public PlannerState State { get; private set; }
    public DateOnly Today => _clock.Today;
    public event EventHandler? Changed;

    public PlannerEngine(IPlannerStore store, IPlannerClock clock)
    {
        _store = store;
        _clock = clock;
        State = store.Load();
        InitializeNotificationTracking();
        ReconcileWeek();
    }

    private void Commit(Action action, bool notify = true)
    {
        try { action(); _store.Save(State); }
        catch { State = _store.Load(); throw; }
        if (notify) Changed?.Invoke(this, EventArgs.Empty);
    }

    public bool ReconcileWeek()
    {
        var week = Dates.Monday(Today);
        if (State.LastRolloverWeek is not null && week <= State.LastRolloverWeek) return false;
        Commit(() =>
        {
            // Materialize missed occurrences before rollover, including time spent with the app closed.
            MaterializeTasks(DateOnly.MinValue, week.AddDays(-1));
            var overdue = State.Tasks.Where(t => !t.Completed && t.Date is not null && t.Date < week).ToList();
            foreach (var task in overdue) task.Date = null;
            if (overdue.Count > 0) QueueNotification(new() { Key = $"rollover:{week:yyyy-MM-dd}", Kind = PlannerNotificationKind.Rollover,
                Destination = PlannerDestination.Week, Created = Today, Body = RolloverMessage });
            State.LastRolloverWeek = week;
        });
        return true;
    }

    public PlannerTask AddTask(string description, bool important, bool urgent, DateOnly? date, DayPart part = DayPart.Evening, Repeat repeat = Repeat.None)
    {
        ValidateDescription(description);
        ValidateRepeat(repeat);
        var task = new PlannerTask { Description = description.Trim(), Important = important, Urgent = urgent, Date = date, Part = part, Repeat = repeat,
            Order = State.Tasks.Select(t => t.Order).DefaultIfEmpty(0).Max() + 1 };
        Commit(() => { State.Tasks.Add(task); if (repeat != Repeat.None && date is not null) StartTaskSeries(task); });
        return task;
    }

    public void EditTask(Guid id, string description, bool important, bool urgent, Repeat? repeat = null)
    {
        ValidateDescription(description);
        if (repeat is { } value) ValidateRepeat(value);
        Commit(() =>
        {
            var task = FindTask(id); task.Description = description.Trim(); task.Important = important; task.Urgent = urgent;
            if (repeat is { } next && task.Repeat != next) ChangeTaskRepeat(task, next);
        });
    }
    public void ToggleTask(Guid id) => Commit(() => { var task = FindTask(id); task.Completed = !task.Completed; });
    public void DeleteTask(Guid id, TaskDeleteScope scope = TaskDeleteScope.ThisAndFuture) => Commit(() =>
    {
        if (!Enum.IsDefined(scope)) throw new ArgumentException("Выберите область удаления");
        var task = FindTask(id);
        if (SeriesFor(task) is { } series && task.OccurrenceIndex is { } index)
        {
            if (scope == TaskDeleteScope.Occurrence)
            {
                (series.ExcludedIndices ??= []).Add(index);
                State.Tasks.Remove(task);
            }
            else
            {
                StopChainFrom(series.ChainId, index);
                State.Tasks.RemoveAll(t => BelongsToChain(t, series.ChainId) && t.OccurrenceIndex >= index);
            }
        }
        else State.Tasks.Remove(task);
    });
    private PlannerTask FindTask(Guid id) => State.Tasks.First(t => t.Id == id);
    private static void ValidateDescription(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException("Введите описание задачи");
        if (value.Length > 10000) throw new ArgumentException("Описание — не более 10000 знаков");
    }

    public IEnumerable<PlannerTask> TasksOn(DateOnly date, DayPart? part = null)
    {
        EnsureTaskRange(date, date);
        return State.Tasks.Where(t => t.Date == date && (part is null || t.Part == part)).OrderBy(t => t.Order);
    }
    public IEnumerable<PlannerTask> Backlog => State.Tasks.Where(t => t.Date is null).OrderBy(t => t.Order);
    public MatrixCounts Matrix(DateOnly first, DateOnly last)
    {
        EnsureTaskRange(first, last);
        int[] scheduled = new int[4], unscheduled = new int[4];
        foreach (var task in State.Tasks)
        {
            if (task.Date is null) unscheduled[(int)task.Kind]++;
            else if (task.Date >= first && task.Date <= last) scheduled[(int)task.Kind]++;
        }
        return new(scheduled, unscheduled);
    }
    public void ToggleMood(DateOnly date, Mood mood) => Commit(() =>
    {
        if (State.Moods.TryGetValue(date, out var current) && current == mood) State.Moods.Remove(date);
        else State.Moods[date] = mood;
    });
    public Mood? MoodOn(DateOnly date) => State.Moods.TryGetValue(date, out var mood) ? mood : null;

    public Habit AddHabit(string name, string color)
    {
        name = name.Trim();
        if (string.IsNullOrWhiteSpace(name) || new System.Globalization.StringInfo(name).LengthInTextElements > 13)
            throw new ArgumentException("Название привычки — от 1 до 13 знаков");
        if (!Palette.Elements.Contains(color, StringComparer.OrdinalIgnoreCase)) throw new ArgumentException("Выберите цвет из палитры");
        if (State.Habits.Count(h => h.Ended is null) >= 3) throw new InvalidOperationException("Не более 3 активных привычек");
        var habit = new Habit { Name = name, Color = color, Created = Today, Order = State.Habits.Select(h => h.Order).DefaultIfEmpty(0).Max() + 1 };
        Commit(() => State.Habits.Add(habit));
        return habit;
    }
    public void FinishHabit(Guid id) => Commit(() =>
    {
        var habit = State.Habits.First(h => h.Id == id);
        if (habit.Ended is not null) throw new InvalidOperationException("Привычка уже завершена");
        habit.Ended = Today;
        State.HabitChecks.RemoveAll(c => c.HabitId == id && c.Date >= Today);
    });
    public void ToggleHabit(Guid id, DateOnly date) => Commit(() =>
    {
        var habit = State.Habits.First(h => h.Id == id);
        if (!habit.ExistsOn(date)) throw new InvalidOperationException("В этот день привычка не была активна");
        var check = State.HabitChecks.FirstOrDefault(c => c.HabitId == id && c.Date == date);
        if (check is not null) State.HabitChecks.Remove(check);
        else State.HabitChecks.Add(new() { HabitId = id, Date = date });
        QueueHabitMilestone(habit);
    });
    public IEnumerable<HabitIndicator> HabitsOn(DateOnly date) => State.Habits.Where(h => h.ExistsOn(date))
        .OrderBy(h => h.Created).ThenBy(h => h.Order).ThenBy(h => h.Id).Select(h => new HabitIndicator(h.Id, h.Name, Palette.HabitColor(h.Color),
            State.HabitChecks.Any(c => c.HabitId == h.Id && c.Date == date)));

    public Payment AddPayment(string name, decimal amount, DateOnly start, Repeat repeat, DateOnly? end)
    {
        ValidatePayment(name, amount, start, end);
        var payment = new Payment { Revisions = [new() { Name = name.Trim(), Amount = amount, Start = start, Repeat = repeat, End = end }] };
        Commit(() => State.Payments.Add(payment));
        return payment;
    }
    public void EditPayment(Guid id, string name, decimal amount, DateOnly start, Repeat repeat, DateOnly? end)
    {
        var current = State.Payments.First(p => p.Id == id).RevisionOn(Today)!;
        bool stopped = current.End is { } currentEnd && currentEnd <= Today && end is { } newEnd && newEnd <= Today;
        ValidatePayment(name, amount, start, end, allowStoppedBeforeStart: stopped);
        Commit(() =>
        {
            var payment = State.Payments.First(p => p.Id == id);
            // Replacing a same-day revision keeps a single authoritative definition per date.
            payment.Revisions.RemoveAll(r => r.EffectiveFrom == Today);
            payment.Revisions.Add(new() { EffectiveFrom = Today, Name = name.Trim(), Amount = amount, Start = start, Repeat = repeat, End = end });
        });
    }
    public void FinishPayment(Guid id)
    {
        var r = State.Payments.First(p => p.Id == id).RevisionOn(Today)!;
        // A future series may be ended before its first occurrence. Keep it visible
        // in the completed list, with today's end date, rather than deleting it.
        Commit(() =>
        {
            var payment = State.Payments.First(p => p.Id == id);
            payment.Revisions.RemoveAll(x => x.EffectiveFrom == Today);
            payment.Revisions.Add(new() { EffectiveFrom = Today, Name = r.Name, Amount = r.Amount, Start = r.Start, Repeat = r.Repeat, End = Today });
        });
    }
    public void DeletePayment(Guid id) => Commit(() => State.Payments.First(p => p.Id == id).DeletedFrom = Today);
    public void TogglePayment(Guid id, DateOnly date) => Commit(() =>
    {
        if (!State.Payments.First(p => p.Id == id).OccursOn(date)) throw new InvalidOperationException("На эту дату платежа нет");
        var check = State.PaymentChecks.FirstOrDefault(c => c.PaymentId == id && c.Date == date);
        if (check is not null) State.PaymentChecks.Remove(check);
        else State.PaymentChecks.Add(new() { PaymentId = id, Date = date });
    });
    public IEnumerable<PaymentOccurrence> PaymentsOn(DateOnly date) => State.Payments.Where(p => p.OccursOn(date)).Select(p =>
    {
        var r = p.RevisionOn(date)!;
        return new PaymentOccurrence(p.Id, date, r.Name, r.Amount, State.PaymentChecks.Any(c => c.PaymentId == p.Id && c.Date == date));
    });
    private static void ValidatePayment(string name, decimal amount, DateOnly start, DateOnly? end, bool allowStoppedBeforeStart = false)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Введите название платежа");
        if (amount <= 0 || amount > 999999999 || decimal.Round(amount, 2) != amount) throw new ArgumentException("Сумма должна быть положительной, с точностью до копеек");
        if (end is not null && end < start && !allowStoppedBeforeStart) throw new ArgumentException("Окончание не может быть раньше начала");
    }

    public string Goal(string key) => State.Goals.GetValueOrDefault(key, "");
    public void SetGoal(string key, string value) => Commit(() => State.Goals[key] = value, notify: false);
    public string Note(string key) => State.Notes.GetValueOrDefault(key, "");
    public void SetNote(string key, string value) => Commit(() => State.Notes[key] = value, notify: false);
}
