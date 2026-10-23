using System.Text.Json;
using Planer.Core;
using Planer.Data;
using Planer_App.Views;

int passed = 0, failed = 0;
void Test(string name, Action action)
{
    try { action(); Console.WriteLine($"PASS {name}"); passed++; }
    catch (Exception ex) { Console.WriteLine($"FAIL {name}: {ex}"); failed++; }
}
void Equal<T>(T expected, T actual)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new Exception($"Expected {expected}; actual {actual}");
}
void Throws<T>(Action action) where T : Exception
{
    try { action(); } catch (T) { return; }
    throw new Exception($"Expected {typeof(T).Name}");
}
(PlannerEngine Engine, TestClock Clock, MemoryStore Store) Setup(DateOnly? date = null)
{
    var clock = new TestClock { Today = date ?? new(2026, 10, 13) };
    var store = new MemoryStore();
    return (new PlannerEngine(store, clock), clock, store);
}

Test("Week starts Monday, including Sunday and year boundaries", () =>
{
    Equal(new DateOnly(2026, 10, 12), Dates.Monday(new(2026, 10, 18)));
    Equal(new DateOnly(2025, 12, 29), Dates.Monday(new(2026, 1, 1)));
});
Test("Rollover moves overdue open tasks, preserves completed and future tasks", () =>
{
    var (e, clock, _) = Setup(new(2026, 10, 11));
    var open = e.AddTask("open", true, true, clock.Today);
    var done = e.AddTask("done", false, true, clock.Today); e.ToggleTask(done.Id);
    var future = e.AddTask("future", true, false, new(2026, 10, 19));
    clock.Today = new(2026, 10, 12); e.ReconcileWeek();
    Equal<DateOnly?>(null, open.Date); Equal<DateOnly?>(new(2026, 10, 11), done.Date); Equal<DateOnly?>(new(2026, 10, 19), future.Date);
});
Test("Manual backdating survives same-week refresh and restart", () =>
{
    var (e, clock, store) = Setup();
    var t = e.AddTask("remembered task", true, false, null);
    e.MoveTask(t.Id, new(2026, 10, 7)); e.ReconcileWeek();
    e = new PlannerEngine(store, clock);
    Equal<DateOnly?>(new(2026, 10, 7), e.State.Tasks.Single().Date);
    clock.Today = new(2026, 10, 19); e.ReconcileWeek(); Equal<DateOnly?>(null, e.State.Tasks.Single().Date);
});
Test("Rollover after multiple closed weeks and persisted completion", () =>
{
    var (e, clock, store) = Setup(new(2026, 9, 1));
    var t = e.AddTask("old", false, false, clock.Today);
    clock.Today = new(2026, 10, 13); e = new PlannerEngine(store, clock);
    Equal(1, e.Backlog.Count()); e.MoveTask(t.Id, new(2026, 9, 1)); e.ToggleTask(t.Id);
    clock.Today = new(2026, 11, 1); e = new PlannerEngine(store, clock); Equal(0, e.Backlog.Count());
});
Test("Monthly drag keeps part, backlog defaults to evening", () =>
{
    var (e, _, _) = Setup();
    var t = e.AddTask("task", false, false, new(2026, 10, 13), DayPart.Morning);
    e.MoveTask(t.Id, new(2026, 10, 14)); Equal(DayPart.Morning, t.Part);
    e.MoveTask(t.Id, null); e.MoveTask(t.Id, new(2026, 10, 15)); Equal(DayPart.Evening, t.Part);
});
Test("Matrix distinguishes current period and global backlog, includes done", () =>
{
    var (e, _, _) = Setup();
    var t = e.AddTask("done", true, true, new(2026, 10, 13)); e.ToggleTask(t.Id);
    e.AddTask("later", true, true, new(2026, 11, 1)); e.AddTask("backlog", false, false, null);
    var m = e.Matrix(new(2026, 10, 12), new(2026, 10, 18)); Equal(1, m.Scheduled[0]); Equal(1, m.Unscheduled[3]);
});
Test("Mood is exclusive, replaceable, and removable", () =>
{
    var (e, clock, _) = Setup();
    e.ToggleMood(clock.Today, Mood.Happy); e.ToggleMood(clock.Today, Mood.Sad); Equal<Mood?>(Mood.Sad, e.MoodOn(clock.Today));
    e.ToggleMood(clock.Today, Mood.Sad); Equal<Mood?>(null, e.MoodOn(clock.Today));
});
Test("At most 3 active habits; finishing frees a slot without reactivation", () =>
{
    var (e, clock, _) = Setup();
    var first = e.AddHabit("first", Palette.Elements[0]); e.AddHabit("second", Palette.Elements[1]); e.AddHabit("third", Palette.Elements[2]);
    Throws<InvalidOperationException>(() => e.AddHabit("fourth", Palette.Elements[3]));
    e.FinishHabit(first.Id); e.AddHabit("new", Palette.Elements[3]); Equal(3, e.HabitsOn(clock.Today).Count());
    Throws<InvalidOperationException>(() => e.FinishHabit(first.Id));
});
Test("Habit history excludes finish day, preserves earlier success and active days", () =>
{
    var (e, clock, _) = Setup(new(2026, 10, 12));
    var h = e.AddHabit("walk", Palette.Elements[3]); e.ToggleHabit(h.Id, clock.Today);
    clock.Today = new(2026, 10, 13); e.ToggleHabit(h.Id, clock.Today); e.ToggleHabit(h.Id, new(2026, 10, 14)); e.FinishHabit(h.Id);
    Equal(0, e.HabitsOn(clock.Today).Count()); Equal(true, e.HabitsOn(new(2026, 10, 12)).Single().Completed);
    Equal(1, e.State.HabitChecks.Count); Equal(1, h.ActiveDays(clock.Today));
});
Test("Monthly and yearly payments clamp to last day and restore anchor", () =>
{
    var (e, _, _) = Setup();
    var p = e.AddPayment("monthly", 100, new(2026, 1, 31), Repeat.Monthly, null);
    Equal(true, p.OccursOn(new(2026, 2, 28))); Equal(true, p.OccursOn(new(2026, 3, 31))); Equal(false, p.OccursOn(new(2026, 3, 28)));
    var leap = e.AddPayment("yearly", 100, new(2024, 2, 29), Repeat.Yearly, null);
    Equal(true, leap.OccursOn(new(2025, 2, 28))); Equal(true, leap.OccursOn(new(2028, 2, 29)));
});
Test("Payment end is inclusive; one occurrence completion does not finish series", () =>
{
    var (e, _, _) = Setup();
    var p = e.AddPayment("bill", 805, new(2026, 10, 6), Repeat.Weekly, new(2026, 10, 13));
    Equal(true, p.OccursOn(new(2026, 10, 13))); Equal(false, p.OccursOn(new(2026, 10, 20)));
    e.TogglePayment(p.Id, new(2026, 10, 6)); Equal(false, e.PaymentsOn(new(2026, 10, 13)).Single().Completed);
});
Test("Payment edit changes today and future, preserves historical amount and check", () =>
{
    var (e, _, _) = Setup();
    var p = e.AddPayment("old", 100, new(2026, 10, 6), Repeat.Weekly, null); e.TogglePayment(p.Id, new(2026, 10, 6));
    e.EditPayment(p.Id, "new", 200, new(2026, 10, 6), Repeat.Weekly, null);
    Equal(100m, e.PaymentsOn(new(2026, 10, 6)).Single().Amount); Equal(true, e.PaymentsOn(new(2026, 10, 6)).Single().Completed);
    Equal(200m, e.PaymentsOn(new(2026, 10, 13)).Single().Amount);
    e.DeletePayment(p.Id); Equal(1, e.PaymentsOn(new(2026, 10, 6)).Count()); Equal(0, e.PaymentsOn(new(2026, 10, 13)).Count());
});
Test("Finishing a series today includes today's payment", () =>
{
    var (e, clock, _) = Setup();
    var p = e.AddPayment("bill", 100, new(2026, 10, 6), Repeat.Weekly, null); e.FinishPayment(p.Id);
    Equal(true, p.OccursOn(clock.Today)); Equal(false, p.IsActive(clock.Today)); Equal(false, p.OccursOn(clock.Today.AddDays(7)));
});
Test("Finishing an unstarted series keeps it in completed payments with today's end date", () =>
{
    var (e, clock, store) = Setup();
    var p = e.AddPayment("future", 100, clock.Today.AddDays(10), Repeat.Monthly, null);
    e.FinishPayment(p.Id); e = new PlannerEngine(store, clock); p = e.State.Payments.Single();
    Equal<DateOnly?>(null, p.DeletedFrom); Equal<DateOnly?>(clock.Today, p.RevisionOn(clock.Today)!.End);
    Equal(false, p.IsActive(clock.Today)); Equal(false, p.OccursOn(clock.Today.AddDays(10)));
});
Test("Stopped future payment can be renamed while retaining its end date", () =>
{
    var (e, clock, _) = Setup();
    var p = e.AddPayment("future", 100, clock.Today.AddDays(10), Repeat.Monthly, null);
    e.FinishPayment(p.Id); e.EditPayment(p.Id, "renamed", 200, clock.Today.AddDays(10), Repeat.Monthly, clock.Today);
    Equal("renamed", p.RevisionOn(clock.Today)!.Name); Equal(false, p.IsActive(clock.Today));
});
Test("Validation rejects invalid task, amount, dates and overlong habit", () =>
{
    var (e, clock, _) = Setup();
    Throws<ArgumentException>(() => e.AddTask(" ", false, false, null));
    Throws<ArgumentException>(() => e.AddHabit(new string('a', 14), Palette.Elements[0]));
    Throws<ArgumentException>(() => e.AddPayment("bill", -1, clock.Today, Repeat.None, null));
    Throws<ArgumentException>(() => e.AddPayment("bill", 1, clock.Today, Repeat.None, clock.Today.AddDays(-1)));
});
Test("SQLite restart roundtrip: tasks, habits, moods, payment history, notes and goals", () =>
{
    var testDir = Path.Combine(Path.GetTempPath(), "PlanerTests", Guid.NewGuid().ToString());
    var path = Path.Combine(testDir, "planner.db");
    var clock = new TestClock { Today = new(2026, 10, 13) };
    var e = new PlannerEngine(new SqlitePlannerStore(path), clock);
    var t = e.AddTask("persistent", true, false, clock.Today); e.ToggleTask(t.Id);
    var h = e.AddHabit("read", Palette.Elements[2]); e.ToggleHabit(h.Id, clock.Today);
    var p = e.AddPayment("bill", 123.45m, clock.Today, Repeat.Monthly, null); e.TogglePayment(p.Id, clock.Today);
    e.ToggleMood(clock.Today, Mood.Happy); e.SetGoal(Dates.WeekKey(clock.Today), "week goal");
    e.SetNote(Dates.WeekKey(clock.Today), "weekly note"); e.SetNote(Dates.MonthKey(clock.Today), "monthly note");
    e = new PlannerEngine(new SqlitePlannerStore(path), clock);
    Equal(true, e.State.Tasks.Single().Completed); Equal(true, e.HabitsOn(clock.Today).Single().Completed);
    Equal(true, e.PaymentsOn(clock.Today).Single().Completed); Equal<Mood?>(Mood.Happy, e.MoodOn(clock.Today));
    Equal("weekly note", e.Note(Dates.WeekKey(clock.Today))); Equal("monthly note", e.Note(Dates.MonthKey(clock.Today)));
    e.DeleteTask(t.Id); e = new PlannerEngine(new SqlitePlannerStore(path), clock); Equal(0, e.State.Tasks.Count);
    // Only this uniquely generated test directory is removed, never a user data directory.
    Directory.Delete(testDir, recursive: true);
});
Test("Failed persistence rolls in-memory state back", () =>
{
    var (e, _, store) = Setup(); store.FailSave = true;
    Throws<IOException>(() => e.AddTask("must not survive", false, false, null)); Equal(0, e.State.Tasks.Count);
});
Test("Legacy habit colors use the element palette without changing stored history", () =>
{
    var (e, clock, store) = Setup();
    var habit = new Habit { Name = "old habit", Color = "#0DBA30", Created = clock.Today.AddDays(-3), Ended = clock.Today };
    var state = store.Load(); state.Habits.Add(habit);
    state.HabitChecks.Add(new() { HabitId = habit.Id, Date = clock.Today.AddDays(-1) }); store.Save(state);
    e = new PlannerEngine(store, clock);
    var historical = e.HabitsOn(clock.Today.AddDays(-1)).Single();
    Equal("#66E37E", historical.Color); Equal(true, historical.Completed);
    Equal(0, e.HabitsOn(clock.Today).Count());
    e.SetNote(Dates.WeekKey(clock.Today), "unrelated save");
    var saved = store.Load(); Equal("#0DBA30", saved.Habits.Single().Color);
    Equal(habit.Created, saved.Habits.Single().Created); Equal(habit.Ended, saved.Habits.Single().Ended);
    Equal(clock.Today.AddDays(-1), saved.HabitChecks.Single().Date);
});

Test("Day layout lends spare space only from empty sections", () =>
{
    var h = DaySectionLayout.Allocate(300, [160, 25, 80], [true, false, true]);
    Equal(160d, h[0]); Equal(40d, h[1]); Equal(100d, h[2]);
});
Test("Day layout can borrow from two empty sections and preserves total height", () =>
{
    var h = DaySectionLayout.Allocate(300, [220, 25, 25], [true, false, false]);
    Equal(220d, h[0]); Equal(40d, h[1]); Equal(40d, h[2]); Equal(300d, h.Sum());
});
Test("Day layout shares limited spare space between two overflowing sections", () =>
{
    var h = DaySectionLayout.Allocate(300, [210, 25, 180], [true, false, true]);
    Equal(25d, h[1]); Equal(300d, h.Sum());
    if (h[0] <= 100 || h[2] <= 100 || h[0] >= 210 || h[2] >= 180) throw new Exception("Incorrect spare-space allocation");
});
Test("Day layout retains equal sections when all are occupied or already fit", () =>
{
    Equal(true, DaySectionLayout.Allocate(300, [200, 40, 20], [true, true, true]).All(h => h == 100));
    Equal(true, DaySectionLayout.Allocate(300, [80, 25, 80], [true, false, true]).All(h => h == 100));
});

Test("Recurring tasks use weekly and fortnightly weekdays without a future horizon", () =>
{
    var (e, clock, _) = Setup(new(2026, 10, 12));
    e.AddTask("weekly", true, false, clock.Today, DayPart.Morning, Repeat.Weekly);
    e.AddTask("fortnightly", false, true, clock.Today, DayPart.Afternoon, Repeat.Fortnightly);
    Equal(1, e.TasksOn(clock.Today.AddDays(7)).Count()); Equal(2, e.TasksOn(clock.Today.AddDays(14)).Count());
    var distant = new DateOnly(2090, 1, 1); e.EnsureTaskRange(distant, distant.AddDays(6));
    if (e.State.Tasks.Count > 8) throw new Exception("Opening a distant week materialized intervening decades");
});
Test("Monthly and yearly task anchors restore the 31st and leap day", () =>
{
    var (e, _, _) = Setup(new(2028, 1, 31));
    e.AddTask("month", false, false, new(2028, 1, 31), repeat: Repeat.Monthly);
    Equal(1, e.TasksOn(new(2028, 2, 29)).Count()); Equal(1, e.TasksOn(new(2028, 3, 31)).Count());
    Equal(0, e.TasksOn(new(2028, 3, 29)).Count());
    e.AddTask("year", true, false, new(2028, 2, 29), repeat: Repeat.Yearly);
    Equal("year", e.TasksOn(new(2029, 2, 28)).Single(t => t.Description == "year").Description);
    Equal(1, e.TasksOn(new(2032, 2, 29)).Count(t => t.Description == "year"));
});
Test("An unassigned recurring task starts only when assigned, defaulting to evening", () =>
{
    var (e, clock, _) = Setup(); var task = e.AddTask("pending", false, false, null, repeat: Repeat.Weekly);
    e.EnsureTaskRange(clock.Today, clock.Today.AddYears(5)); Equal(1, e.State.Tasks.Count); Equal(0, e.State.TaskSeries.Count);
    e.MoveTask(task.Id, clock.Today); Equal(DayPart.Evening, task.Part);
    Equal(1, e.TasksOn(clock.Today.AddDays(7)).Count()); Equal(0, e.Backlog.Count());
});
Test("Completion and text edits affect only one recurring occurrence", () =>
{
    var (e, clock, _) = Setup(); var t = e.AddTask("original", true, false, clock.Today, repeat: Repeat.Weekly);
    var next = e.TasksOn(clock.Today.AddDays(7)).Single(); e.ToggleTask(t.Id); e.EditTask(t.Id, "edited", false, true);
    Equal(false, next.Completed); Equal("original", next.Description); Equal("edited", t.Description);
    Equal("original", e.TasksOn(clock.Today.AddDays(14)).Single().Description);
});
Test("Moving one occurrence neither moves nor regenerates other occurrences after restart", () =>
{
    var (e, clock, store) = Setup(); var first = e.AddTask("move one", false, false, clock.Today, DayPart.Morning, Repeat.Weekly);
    var next = e.TasksOn(clock.Today.AddDays(7)).Single(); e.MoveTask(next.Id, clock.Today.AddDays(8));
    e = new PlannerEngine(store, clock);
    Equal(0, e.TasksOn(clock.Today.AddDays(7)).Count()); Equal(next.Id, e.TasksOn(clock.Today.AddDays(8)).Single().Id);
    Equal(DayPart.Morning, e.TasksOn(clock.Today.AddDays(8)).Single().Part);
    Equal(first.Id, e.TasksOn(clock.Today).Single().Id); Equal(1, e.TasksOn(clock.Today.AddDays(14)).Count());
});
Test("Moving future chain preserves earlier history and occurrence completion", () =>
{
    var (e, clock, _) = Setup(); var first = e.AddTask("chain", true, false, clock.Today, DayPart.Morning, Repeat.Weekly); e.ToggleTask(first.Id);
    var next = e.TasksOn(clock.Today.AddDays(7)).Single(); var third = e.TasksOn(clock.Today.AddDays(14)).Single(); e.ToggleTask(third.Id);
    e.MoveTask(next.Id, clock.Today.AddDays(9), DayPart.Afternoon, TaskMoveScope.ThisAndFuture);
    Equal<DateOnly?>(clock.Today, first.Date); Equal(true, first.Completed); Equal(0, e.TasksOn(clock.Today.AddDays(7)).Count());
    var moved = e.TasksOn(clock.Today.AddDays(16)).Single(); Equal(third.Id, moved.Id); Equal(true, moved.Completed); Equal(DayPart.Afternoon, moved.Part);
    Equal(1, e.TasksOn(clock.Today.AddDays(23)).Count()); Equal(0, e.TasksOn(clock.Today.AddDays(21)).Count());
});
Test("Reanchoring monthly future tasks uses the new day without changing previous months", () =>
{
    var (e, _, _) = Setup(new(2026, 1, 31)); var first = e.AddTask("monthly", false, false, new(2026, 1, 31), repeat: Repeat.Monthly);
    var next = e.TasksOn(new(2026, 2, 28)).Single(); e.MoveTask(next.Id, new(2026, 2, 27), scope: TaskMoveScope.ThisAndFuture);
    Equal(first.Id, e.TasksOn(new(2026, 1, 31)).Single().Id); Equal(0, e.TasksOn(new(2026, 3, 31)).Count());
    Equal(1, e.TasksOn(new(2026, 3, 27)).Count());
});
Test("Deleting a recurring task removes current and all unseen future instances, preserving earlier ones", () =>
{
    var (e, clock, store) = Setup(); var first = e.AddTask("delete", false, false, clock.Today, repeat: Repeat.Weekly); e.ToggleTask(first.Id);
    var next = e.TasksOn(clock.Today.AddDays(7)).Single(); e.TasksOn(clock.Today.AddDays(14)).ToList(); e.DeleteTask(next.Id);
    e = new PlannerEngine(store, clock); Equal(first.Id, e.TasksOn(clock.Today).Single().Id); Equal(true, e.TasksOn(clock.Today).Single().Completed);
    Equal(0, e.Matrix(clock.Today.AddDays(7), clock.Today.AddYears(2)).Scheduled.Sum());
});
Test("Changing only the day part retains monthly 31st and yearly leap-day anchors", () =>
{
    var (e, _, store) = Setup(new(2028, 1, 31));
    var first = e.AddTask("month part", false, false, new(2028, 1, 31), DayPart.Morning, Repeat.Monthly);
    var february = e.TasksOn(new(2028, 2, 29)).Single(); var march = e.TasksOn(new(2028, 3, 31)).Single();
    e.MoveTask(february.Id, february.Date, DayPart.Evening, TaskMoveScope.ThisAndFuture);
    Equal(DayPart.Morning, first.Part); Equal(march.Id, e.TasksOn(new(2028, 3, 31)).Single().Id);
    Equal(DayPart.Evening, e.TasksOn(new(2028, 4, 30)).Single().Part); Equal(0, e.TasksOn(new(2028, 3, 29)).Count());
    var annual = e.AddTask("year part", false, false, new(2028, 2, 29), DayPart.Morning, Repeat.Yearly);
    var nextYear = e.TasksOn(new(2029, 2, 28)).Single(t => t.Description == "year part");
    e.MoveTask(nextYear.Id, nextYear.Date, DayPart.Afternoon, TaskMoveScope.ThisAndFuture);
    e = new PlannerEngine(store, new TestClock { Today = new(2028, 1, 31) });
    Equal(DayPart.Morning, e.TaskById(annual.Id)!.Part);
    Equal(DayPart.Afternoon, e.TasksOn(new(2032, 2, 29)).Single(t => t.Description == "year part").Part);
    Equal(0, e.TasksOn(new(2032, 2, 28)).Count(t => t.Description == "year part"));
    e.DeleteTask(february.Id); Equal(0, e.TasksOn(new(2028, 3, 31)).Count(t => t.Description == "month part"));
});
Test("Deleting before a changed segment stops that segment too", () =>
{
    var (e, clock, _) = Setup(); var first = e.AddTask("split", false, false, clock.Today, repeat: Repeat.Weekly);
    var next = e.TasksOn(clock.Today.AddDays(7)).Single(); e.MoveTask(next.Id, clock.Today.AddDays(8), scope: TaskMoveScope.ThisAndFuture);
    e.DeleteTask(first.Id); Equal(0, e.Matrix(clock.Today, clock.Today.AddYears(1)).Scheduled.Sum());
});
Test("Rollover materializes missed recurring tasks once, keeping completed history and future dates", () =>
{
    var (e, clock, store) = Setup(new(2026, 10, 12)); var first = e.AddTask("roll", false, false, clock.Today, repeat: Repeat.Weekly); e.ToggleTask(first.Id);
    var future = e.TasksOn(new(2026, 11, 9)).Single(); clock.Today = new(2026, 11, 2); e = new PlannerEngine(store, clock);
    Equal(2, e.Backlog.Count()); Equal(1, e.TasksOn(new(2026, 10, 12)).Count()); Equal(future.Id, e.TasksOn(new(2026, 11, 9)).Single().Id);
    Equal(0, e.TasksOn(new(2026, 10, 19)).Count()); Equal(0, e.TasksOn(new(2026, 10, 26)).Count());
    e = new PlannerEngine(store, clock); Equal(2, e.Backlog.Count());
    var backlog = e.Backlog.First(); e.MoveTask(backlog.Id, new(2026, 10, 20)); e.ToggleTask(backlog.Id);
    e = new PlannerEngine(store, clock); Equal(1, e.Backlog.Count()); Equal(true, e.TasksOn(new(2026, 10, 20)).Single().Completed);
});
Test("Moving an entire chain to backlog pauses it until reassignment without an infinite queue", () =>
{
    var (e, clock, _) = Setup(); var first = e.AddTask("pause", false, false, clock.Today, repeat: Repeat.Weekly);
    var next = e.TasksOn(clock.Today.AddDays(7)).Single(); e.TasksOn(clock.Today.AddDays(14)).ToList();
    e.MoveTask(next.Id, null, scope: TaskMoveScope.ThisAndFuture); e.EnsureTaskRange(clock.Today, clock.Today.AddYears(2));
    Equal(1, e.Backlog.Count()); Equal(1, e.Matrix(clock.Today, clock.Today.AddYears(2)).Scheduled.Sum());
    e.MoveTask(next.Id, clock.Today.AddDays(10)); Equal(1, e.TasksOn(clock.Today.AddDays(17)).Count());
    Equal(first.Id, e.TasksOn(clock.Today).Single().Id);
});
Test("Changing recurrence truncates only the selected and subsequent schedule", () =>
{
    var (e, clock, _) = Setup(); var first = e.AddTask("change", false, false, clock.Today, repeat: Repeat.Weekly);
    var next = e.TasksOn(clock.Today.AddDays(7)).Single(); e.EditTask(next.Id, "monthly", true, false, Repeat.Monthly);
    Equal(first.Id, e.TasksOn(clock.Today).Single().Id); Equal(0, e.TasksOn(clock.Today.AddDays(14)).Count());
    Equal(1, e.TasksOn(clock.Today.AddDays(7).AddMonths(1)).Count());
    e.EditTask(next.Id, "single", false, false, Repeat.None); Equal(0, e.TasksOn(clock.Today.AddDays(7).AddMonths(1)).Count());
    Equal(next.Id, e.TasksOn(clock.Today.AddDays(7)).Single().Id);
});
Test("Failed series reschedule rolls back dates, segments and completion atomically", () =>
{
    var (e, clock, store) = Setup(); var first = e.AddTask("atomic", false, false, clock.Today, repeat: Repeat.Weekly);
    var next = e.TasksOn(clock.Today.AddDays(7)).Single(); e.ToggleTask(next.Id); store.FailSave = true;
    Throws<IOException>(() => e.MoveTask(first.Id, clock.Today.AddDays(1), scope: TaskMoveScope.ThisAndFuture));
    Equal(1, e.State.TaskSeries.Count); Equal<DateOnly?>(clock.Today, e.TaskById(first.Id)!.Date);
    Equal(true, e.TaskById(next.Id)!.Completed); Equal<DateOnly?>(clock.Today.AddDays(7), e.TaskById(next.Id)!.Date);
});
Test("SQLite stores series splits, moved exceptions and independent completion", () =>
{
    var dir = Path.Combine(Path.GetTempPath(), "PlanerTests", Guid.NewGuid().ToString()); var path = Path.Combine(dir, "planner.db");
    var clock = new TestClock { Today = new(2026, 10, 13) }; var e = new PlannerEngine(new SqlitePlannerStore(path), clock);
    var first = e.AddTask("sqlite repeat", true, true, clock.Today, repeat: Repeat.Weekly); e.ToggleTask(first.Id);
    var second = e.TasksOn(clock.Today.AddDays(7)).Single(); e.MoveTask(second.Id, clock.Today.AddDays(9), scope: TaskMoveScope.ThisAndFuture);
    var third = e.TasksOn(clock.Today.AddDays(16)).Single(); e.MoveTask(third.Id, null);
    e = new PlannerEngine(new SqlitePlannerStore(path), clock); Equal(2, e.State.TaskSeries.Count); Equal(1, e.Backlog.Count());
    Equal(true, e.TasksOn(clock.Today).Single().Completed); Equal(0, e.TasksOn(clock.Today.AddDays(16)).Count());
    Equal(1, e.TasksOn(clock.Today.AddDays(23)).Count()); Directory.Delete(dir, true);
});
Test("Old SQLite tasks migrate with no repeat and keep their IDs and dates", () =>
{
    var dir = Path.Combine(Path.GetTempPath(), "PlanerTests", Guid.NewGuid().ToString()); Directory.CreateDirectory(dir); var path = Path.Combine(dir, "planner.db");
    var id = Guid.NewGuid();
    using (var c = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={path};Pooling=False"))
    {
        c.Open(); using var command = c.CreateCommand(); command.CommandText = "CREATE TABLE tasks(id TEXT PRIMARY KEY,data TEXT NOT NULL);PRAGMA user_version=1;INSERT INTO tasks VALUES($id,$data)";
        command.Parameters.AddWithValue("$id", id.ToString()); command.Parameters.AddWithValue("$data", $$"""{"Id":"{{id}}","Description":"legacy","Date":"2026-10-13","Part":0,"Completed":true} """); command.ExecuteNonQuery();
    }
    var e = new PlannerEngine(new SqlitePlannerStore(path), new TestClock { Today = new(2026, 10, 13) });
    var task = e.State.Tasks.Single(); Equal(id, task.Id); Equal(Repeat.None, task.Repeat); Equal(true, task.Completed); Equal(0, e.State.TaskSeries.Count);
    Equal<DateOnly?>(new(2026, 10, 13), task.Date); Directory.Delete(dir, true);
});
Test("Pale habit palette retains element identity including legacy colors", () =>
{
    Equal("#E0F8E5", Palette.PaleHabitColor("#0DBA30")); Equal("#DBEEFD", Palette.PaleHabitColor("#59ADF6"));
    Equal("#F5F5F5", Palette.PaleHabitColor("#99908C"));
});

Test("A drag keeps its original grab offset when the window scales", () =>
{
    Equal(new SnapPoint(70, 7), DragSnapTracker.GrabOffset(new(70, 7), 200, 26, 200, 26));
    Equal(new SnapPoint(105, 10.5), DragSnapTracker.GrabOffset(new(70, 7), 200, 26, 300, 39));
});
Test("Snap capture uses half the day width and 20 vertical pixels independently", () =>
{
    var snap = new DragSnapTracker(); SnapTarget[] slots = [new(1, new(200, 100), 100)];
    Equal<int?>(null, snap.Update(new(99.99, 100), slots, 20, 60));
    Equal<int?>(null, snap.Update(new(200, 120.01), slots, 20, 60));
    Equal<int?>(1, snap.Update(new(100, 120), slots, 20, 60));
    snap.Reset(); Equal<int?>(1, snap.Update(new(300, 80), slots, 20, 60));
});
Test("Horizontal release is measured from engagement rather than the slot", () =>
{
    var snap = new DragSnapTracker(); SnapTarget[] slots = [new(1, new(100, 100), 120)];
    Equal<int?>(1, snap.Update(new(140, 100), slots, 20, 60));
    Equal<int?>(1, snap.Update(new(80, 100), slots, 20, 60));
    Equal<int?>(null, snap.Update(new(79.99, 100), slots, 20, 60));
});
Test("Snap releases beyond 60 pixels in either horizontal direction", () =>
{
    var snap = new DragSnapTracker(); SnapTarget[] slots = [new(1, new(200, 100), 120)];
    foreach (int direction in new[] { -1, 1 })
    {
        snap.Reset(); Equal<int?>(1, snap.Update(new(200, 100), slots, 20, 60));
        Equal<int?>(1, snap.Update(new(200 + direction * 60, 120), slots, 20, 60));
        Equal<int?>(null, snap.Update(new(200 + direction * 60.01, 120), slots, 20, 60));
    }
});
Test("Released slots stay free until leaving their broad capture zone", () =>
{
    var snap = new DragSnapTracker(); SnapTarget[] slots = [new(1, new(200, 100), 120)];
    Equal<int?>(1, snap.Update(new(200, 100), slots, 20, 60));
    Equal<int?>(null, snap.Update(new(261, 100), slots, 20, 60));
    Equal<int?>(null, snap.Update(new(270, 100), slots, 20, 60));
    Equal<int?>(null, snap.Update(new(321, 100), slots, 20, 60));
    Equal<int?>(1, snap.Update(new(300, 100), slots, 20, 60));
});
Test("Snap keeps the selected slot until release even near a neighboring target", () =>
{
    var snap = new DragSnapTracker(); SnapTarget[] slots = [new(1, new(100, 100), 100), new(2, new(180, 100), 100)];
    Equal<int?>(1, snap.Update(new(100, 100), slots, 20, 60));
    Equal<int?>(1, snap.Update(new(150, 100), slots, 20, 60));
    Equal<int?>(2, snap.Update(new(161, 100), slots, 20, 60));
});
Test("Removed targets and a new drag cannot retain stale snap state", () =>
{
    var snap = new DragSnapTracker(); SnapTarget[] slots = [new(1, new(100, 100), 100)];
    Equal<int?>(1, snap.Update(new(100, 100), slots, 20, 60));
    Equal<int?>(null, snap.Update(new(100, 100), [], 20, 60));
    Equal<int?>(1, snap.Update(new(100, 100), slots, 20, 60));
    Equal<int?>(null, snap.Update(new(161, 100), slots, 20, 60));
    snap.Reset(); Equal<int?>(1, snap.Update(new(161, 100), slots, 20, 60));
});
Test("Pixel thresholds scale with DPI while day widths remain per target", () =>
{
    var snap = new DragSnapTracker(); SnapTarget[] slots = [new(1, new(100, 100), 40), new(2, new(300, 100), 120)];
    Equal<int?>(null, snap.Update(new(141, 100), slots, 10, 30));
    Equal<int?>(1, snap.Update(new(100, 110), slots, 10, 30));
    Equal<int?>(1, snap.Update(new(130, 110), slots, 10, 30));
    Equal<int?>(null, snap.Update(new(130.01, 110), slots, 10, 30));
    snap.Reset(); Equal<int?>(2, snap.Update(new(400, 100), slots, 10, 30));
    Equal<int?>(null, snap.Update(new(400, 110.01), slots, 10, 30));
});

Test("Habit streak keeps yesterday's run until midnight and resets after a missed full day", () =>
{
    var (e, clock, _) = Setup(new(2026, 10, 1)); var habit = e.AddHabit("Прогулка", Palette.Elements[3]);
    for (int i = 0; i < 14; i++) { e.ToggleHabit(habit.Id, clock.Today); clock.Today = clock.Today.AddDays(1); }
    Equal(14, e.HabitStreak(habit.Id).Days); Equal<DateOnly?>(new(2026, 10, 1), e.HabitStreak(habit.Id).Start);
    clock.Today = clock.Today.AddDays(1); Equal(0, e.HabitStreak(habit.Id).Days);
    e.ToggleHabit(habit.Id, clock.Today); Equal(1, e.HabitStreak(habit.Id).Days);
});
Test("Habit streak handles undo, backfilling a gap, future checks and separate habits", () =>
{
    var (e, clock, _) = Setup(new(2026, 10, 1)); var h = e.AddHabit("Первая", Palette.Elements[0]); var second = e.AddHabit("Вторая", Palette.Elements[1]);
    for (int i = 0; i < 5; i++) if (i != 2) e.ToggleHabit(h.Id, new(2026, 10, i + 1));
    clock.Today = new(2026, 10, 5); Equal(2, e.HabitStreak(h.Id).Days); Equal(0, e.HabitStreak(second.Id).Days);
    e.ToggleHabit(h.Id, new(2026, 10, 3)); Equal(5, e.HabitStreak(h.Id).Days);
    e.ToggleHabit(h.Id, clock.Today); Equal(4, e.HabitStreak(h.Id).Days);
    e.ToggleHabit(h.Id, clock.Today.AddDays(1)); Equal(4, e.HabitStreak(h.Id).Days);
});
Test("Finishing a habit retains its final streak through SQLite restart", () =>
{
    var folder = Path.Combine(Path.GetTempPath(), "Planer-test-" + Guid.NewGuid()); Directory.CreateDirectory(folder);
    try
    {
        var clock = new TestClock { Today = new(2026, 10, 1) }; var store = new SqlitePlannerStore(Path.Combine(folder, "planner.db"));
        var e = new PlannerEngine(store, clock); var h = e.AddHabit("Читать", Palette.Elements[5]);
        e.ToggleHabit(h.Id, clock.Today); clock.Today = clock.Today.AddDays(1); e.ToggleHabit(h.Id, clock.Today);
        clock.Today = clock.Today.AddDays(1); e.FinishHabit(h.Id); clock.Today = clock.Today.AddMonths(1);
        e = new PlannerEngine(store, clock); Equal(2, e.HabitStreak(h.Id).Days); Equal(2, e.State.HabitChecks.Count);
    }
    finally { Directory.Delete(folder, true); }
});

Test("Morning notification is due from 10 inclusive until 12 exclusive, once per date", () =>
{
    var (e, clock, store) = Setup(); e.AddTask("Сегодня", false, false, clock.Today);
    e.CheckNotificationDue(clock.Today.ToDateTime(new(9, 59))); Equal(0, e.State.Notifications.Count);
    e.CheckNotificationDue(clock.Today.ToDateTime(new(10, 0))); var first = e.PendingNotifications(clock.Today.ToDateTime(new(10, 0))).Single();
    Equal(PlannerDestination.Today, first.Destination); Equal(PlannerEngine.MorningMessage, first.Body);
    e.AcknowledgeNotification(first.Key); e = new PlannerEngine(store, clock);
    e.CheckNotificationDue(clock.Today.ToDateTime(new(11, 59))); Equal(0, e.PendingNotifications(clock.Today.ToDateTime(new(11, 59))).Count);
    clock.Today = clock.Today.AddDays(1); e.AddTask("Поздно", true, false, clock.Today);
    e.CheckNotificationDue(clock.Today.ToDateTime(new(12, 0))); Equal(1, e.State.Notifications.Count);
});
Test("A late start before noon catches up; backlog and payments alone do not trigger morning", () =>
{
    var (e, clock, _) = Setup(); e.AddTask("Без даты", false, false, null); e.AddPayment("Платёж", 100, clock.Today, Repeat.None, null);
    e.CheckNotificationDue(clock.Today.ToDateTime(new(11, 0))); Equal(0, e.State.Notifications.Count);
    var task = e.AddTask("Уже выполнено", true, false, clock.Today); e.ToggleTask(task.Id);
    e.CheckNotificationDue(clock.Today.ToDateTime(new(11, 0))); Equal(1, e.PendingNotifications(clock.Today.ToDateTime(new(11, 0))).Count);
    e.DeleteTask(task.Id); Equal(0, e.PendingNotifications(clock.Today.ToDateTime(new(11, 0))).Count);
});
Test("Rollover notification is durable, requires an actual move, and never repeats in the same week", () =>
{
    var (e, clock, store) = Setup(new(2026, 10, 18)); Equal(0, e.State.Notifications.Count);
    var open = e.AddTask("Просрочено", true, true, clock.Today); var done = e.AddTask("Готово", false, false, clock.Today); e.ToggleTask(done.Id);
    clock.Today = new(2026, 10, 19); e = new PlannerEngine(store, clock);
    var notification = e.State.Notifications.Single(n => n.Kind == PlannerNotificationKind.Rollover);
    Equal(PlannerEngine.RolloverMessage, notification.Body); Equal(PlannerDestination.Week, notification.Destination);
    Equal<DateOnly?>(null, e.State.Tasks.Single(t => t.Id == open.Id).Date); Equal<DateOnly?>(new(2026, 10, 18), e.State.Tasks.Single(t => t.Id == done.Id).Date);
    e.AcknowledgeNotification(notification.Key); e = new PlannerEngine(store, clock); e.ReconcileWeek();
    Equal(1, e.State.Notifications.Count); Equal(0, e.PendingNotifications(clock.Today.ToDateTime(new(9, 0))).Count);
});
Test("Habit notifications occur at 14, 30, 60 and 90 consecutive marked dates", () =>
{
    var (e, clock, _) = Setup(new(2026, 1, 1)); var h = e.AddHabit("Чтение", Palette.Elements[0]);
    for (int day = 1; day <= 90; day++)
    {
        clock.Today = h.Created.AddDays(day - 1); e.ToggleHabit(h.Id, clock.Today);
        foreach (var n in e.PendingNotifications(clock.Today.ToDateTime(new(9, 0)))) e.AcknowledgeNotification(n.Key);
    }
    Equal("14,30,60,90", string.Join(',', e.State.Notifications.Where(n => n.HabitId == h.Id).Select(n => n.Milestone)));
    Equal(true, e.State.Notifications.All(n => n.Body.Contains("Чтение") && n.Body.Contains("Ты молодец <3")));
});
Test("Undo and redo cannot duplicate a habit toast, while a new run can reach 14 again", () =>
{
    var (e, clock, _) = Setup(new(2026, 1, 1)); var h = e.AddHabit("Вода", Palette.Elements[4]);
    for (int i = 0; i < 14; i++) { clock.Today = h.Created.AddDays(i); e.ToggleHabit(h.Id, clock.Today); }
    e.ToggleHabit(h.Id, clock.Today); Equal(0, e.PendingNotifications(clock.Today.ToDateTime(new(9, 0))).Count);
    e.ToggleHabit(h.Id, clock.Today); Equal(1, e.State.Notifications.Count);
    clock.Today = clock.Today.AddDays(2); var restart = clock.Today;
    for (int i = 0; i < 14; i++) { clock.Today = restart.AddDays(i); e.ToggleHabit(h.Id, clock.Today); }
    Equal(2, e.State.Notifications.Count(n => n.HabitId == h.Id && n.Milestone == 14));
});
Test("Future habit marks do not award milestones early; old databases receive no retroactive congratulations", () =>
{
    var (e, clock, store) = Setup(new(2026, 1, 1)); var h = e.AddHabit("Сон", Palette.Elements[5]);
    for (int i = 0; i < 30; i++) e.ToggleHabit(h.Id, h.Created.AddDays(i));
    Equal(0, e.State.Notifications.Count); clock.Today = h.Created.AddDays(13);
    e.CheckNotificationDue(clock.Today.ToDateTime(new(9, 0))); Equal(14, e.State.Notifications.Single().Milestone);
    var legacy = store.Load(); legacy.Notifications.Clear(); legacy.NotificationTrackingInitialized = false;
    clock.Today = h.Created.AddDays(29); store.Save(legacy); e = new PlannerEngine(store, clock);
    e.CheckNotificationDue(clock.Today.ToDateTime(new(9, 0))); Equal(0, e.PendingNotifications(clock.Today.ToDateTime(new(9, 0))).Count);
    Equal(2, e.State.Notifications.Count(n => n.HabitId == h.Id && n.Dispatched));
});
Test("A failed habit save cannot keep either the check or its queued milestone", () =>
{
    var (e, clock, store) = Setup(new(2026, 1, 1)); var h = e.AddHabit("Пешком", Palette.Elements[3]);
    for (int i = 0; i < 13; i++) { clock.Today = h.Created.AddDays(i); e.ToggleHabit(h.Id, clock.Today); }
    clock.Today = clock.Today.AddDays(1); store.FailSave = true; Throws<IOException>(() => e.ToggleHabit(h.Id, clock.Today));
    Equal(13, e.HabitStreak(h.Id).Days); Equal(0, e.State.Notifications.Count);
});
Test("SQLite retains notification receipts and disabled preference without changing planner history", () =>
{
    var folder = Path.Combine(Path.GetTempPath(), "Planer-test-" + Guid.NewGuid()); Directory.CreateDirectory(folder);
    try
    {
        var clock = new TestClock { Today = new(2026, 10, 17) }; var store = new SqlitePlannerStore(Path.Combine(folder, "planner.db"));
        var e = new PlannerEngine(store, clock); var t = e.AddTask("Сегодня", true, true, clock.Today);
        e.CheckNotificationDue(clock.Today.ToDateTime(new(11, 0))); e.AcknowledgeNotification(e.State.Notifications.Single().Key); e.SetNotificationsEnabled(false);
        e = new PlannerEngine(store, clock); Equal(false, e.State.NotificationsEnabled); Equal(true, e.State.Notifications.Single().Dispatched);
        Equal(t.Id, e.TasksOn(clock.Today).Single().Id); Equal(0, e.PendingNotifications(clock.Today.ToDateTime(new(11, 0))).Count);
    }
    finally { Directory.Delete(folder, true); }
});

Test("A missed final active day leaves a finished habit with no current streak", () =>
{
    var (e, clock, _) = Setup(new(2026, 10, 1)); var habit = e.AddHabit("Серия", Palette.Elements[3]);
    e.ToggleHabit(habit.Id, clock.Today); clock.Today = clock.Today.AddDays(2); e.FinishHabit(habit.Id);
    Equal(0, e.HabitStreak(habit.Id).Days); Equal(1, e.State.HabitChecks.Count);
});

Test("Deleting one repeat preserves siblings and cannot regenerate through restart or rollover", () =>
{
    var (e, clock, store) = Setup(); var first = e.AddTask("Повтор", true, false, clock.Today, repeat: Repeat.Weekly);
    e.ToggleTask(first.Id); var second = e.TasksOn(clock.Today.AddDays(7)).Single();
    var third = e.TasksOn(clock.Today.AddDays(14)).Single(); e.DeleteTask(second.Id, TaskDeleteScope.Occurrence);
    e = new PlannerEngine(store, clock);
    Equal(0, e.TasksOn(second.Date!.Value).Count()); Equal(third.Id, e.TasksOn(third.Date!.Value).Single().Id);
    Equal(true, e.TaskById(first.Id)!.Completed);
    clock.Today = clock.Today.AddDays(28); e.ReconcileWeek();
    Equal(false, e.State.Tasks.Any(t => t.OccurrenceIndex == second.OccurrenceIndex));
    Equal(true, e.State.Tasks.Any(t => t.OccurrenceIndex == 3));
});
Test("Deleted monthly exceptions remain excluded after a future reschedule and a pause", () =>
{
    var (e, clock, store) = Setup(new(2026, 1, 31));
    e.AddTask("Месяц", false, false, clock.Today, repeat: Repeat.Monthly);
    var feb = e.TasksOn(new(2026, 2, 28)).Single(); var march = e.TasksOn(new(2026, 3, 31)).Single();
    e.DeleteTask(march.Id, TaskDeleteScope.Occurrence);
    e.MoveTask(feb.Id, new(2026, 2, 27), scope: TaskMoveScope.ThisAndFuture);
    Equal(0, e.TasksOn(new(2026, 3, 27)).Count()); Equal(1, e.TasksOn(new(2026, 4, 27)).Count());
    e.MoveTask(feb.Id, null, scope: TaskMoveScope.ThisAndFuture);
    e.MoveTask(feb.Id, new(2026, 2, 26)); e = new PlannerEngine(store, clock);
    Equal(0, e.TasksOn(new(2026, 3, 26)).Count()); Equal(1, e.TasksOn(new(2026, 4, 26)).Count());
});
Test("SQLite keeps single deletions and failure rolls back exclusion and task together", () =>
{
    var (e, clock, memory) = Setup(); var first = e.AddTask("Ошибка", false, true, clock.Today, repeat: Repeat.Weekly);
    memory.FailSave = true; Throws<IOException>(() => e.DeleteTask(first.Id, TaskDeleteScope.Occurrence)); memory.FailSave = false;
    Equal(first.Id, e.TasksOn(clock.Today).Single().Id); Equal(null, e.State.TaskSeries.Single().ExcludedIndices);
    var folder = Path.Combine(Path.GetTempPath(), "Planer-test-" + Guid.NewGuid()); Directory.CreateDirectory(folder);
    try
    {
        var store = new SqlitePlannerStore(Path.Combine(folder, "planner.db")); store.Save(e.State);
        e = new PlannerEngine(store, clock); e.DeleteTask(first.Id, TaskDeleteScope.Occurrence); e = new PlannerEngine(store, clock);
        Equal(0, e.TasksOn(clock.Today).Count()); Equal(1, e.TasksOn(clock.Today.AddDays(7)).Count());
    }
    finally { Directory.Delete(folder, true); }
});

Test("Budget validates cents and never rounds malformed input into an affordable amount", () =>
{
    foreach (var text in new[] { "0", "12", "12,34", "12.34", "999999999", "0,01" }) Equal(true, BudgetMoney.TryParse(text, out _));
    foreach (var text in new[] { "", "-1", "1 000", "1,000", "1.001", "1,2.3", ".50", "1e2", "1000000000", "NaN" }) Equal(false, BudgetMoney.TryParse(text, out _));
    Equal(true, BudgetMoney.IsInput("")); Equal(true, BudgetMoney.IsInput("12,"));
    var (e, _, _) = Setup(); Throws<ArgumentException>(() => e.SetBudgetIncome(-1));
    Throws<ArgumentException>(() => e.SetBudgetIncome(1.001m)); Equal(0m, e.Budget.Income);
});
Test("Exact budget boundary succeeds, excess fails without changing the current plan", () =>
{
    var (e, clock, _) = Setup(); e.SetBudgetIncome(1000.50m); e.AddPayment("Интернет", 100.25m, clock.Today, Repeat.Monthly, null);
    var first = e.SaveBudgetCategory(null, "  Еда  ", 500m); Equal("Еда", first.Name);
    var last = e.SaveBudgetCategory(null, "Остальное", 400.25m); Equal(0m, e.Budget.Remaining);
    try { e.SaveBudgetCategory(null, "Лишнее", .01m); throw new Exception("Unaffordable category was saved"); }
    catch (ArgumentException error) { Equal(PlannerEngine.BudgetExceededMessage, error.Message); }
    Equal(2, e.State.BudgetCategories.Count); Equal(900.25m, e.Budget.CategoryLimits);
    e.SaveBudgetCategory(first.Id, "Еда и дом", 499.99m); Equal(.01m, e.Budget.Remaining);
    e.SaveBudgetCategory(last.Id, "Прочее", 400.26m); Equal(0m, e.Budget.Remaining);
    Throws<ArgumentException>(() => e.SaveBudgetCategory(first.Id, "Перерасход", 500m));
    Equal("Еда и дом", e.State.BudgetCategories.Single(c => c.Id == first.Id).Name);
});
Test("Seven unique category colors exclude payment purple and a removed color can be reused", () =>
{
    var (e, _, _) = Setup(); e.SetBudgetIncome(1000);
    for (int i = 0; i < 7; i++) e.SaveBudgetCategory(null, $"Категория {i}", 10);
    Equal(7, e.State.BudgetCategories.Select(c => c.Color).Distinct().Count());
    Equal(false, e.State.BudgetCategories.Any(c => c.Color == Palette.Elements[7]));
    Throws<ArgumentException>(() => e.SaveBudgetCategory(null, "Восьмая", 1));
    var first = e.State.BudgetCategories.First(); e.DeleteBudgetCategory(first.Id);
    Equal(940m, e.Budget.Remaining); var added = e.SaveBudgetCategory(null, "Новая", 20);
    Equal(first.Color, added.Color); Equal(7, e.State.BudgetCategories.Count);
});
Test("Budget mandatory amount uses active monthly plans, independently of paid occurrences", () =>
{
    var (e, clock, _) = Setup(); e.SetBudgetIncome(1000);
    var monthly = e.AddPayment("Месяц", 100.25m, clock.Today.AddMonths(-1), Repeat.Monthly, null);
    e.AddPayment("Неделя", 20, clock.Today, Repeat.Weekly, null);
    e.AddPayment("Две недели", 30, clock.Today, Repeat.Fortnightly, null);
    e.AddPayment("Год", 40, clock.Today, Repeat.Yearly, null); e.AddPayment("Разово", 50, clock.Today, Repeat.None, null);
    var ended = e.AddPayment("Завершён", 60, clock.Today, Repeat.Monthly, null); e.FinishPayment(ended.Id);
    var deleted = e.AddPayment("Удалён", 70, clock.Today, Repeat.Monthly, null); e.DeletePayment(deleted.Id);
    Equal(100.25m, e.Budget.MandatoryPayments); e.TogglePayment(monthly.Id, clock.Today);
    Equal(100.25m, e.Budget.MandatoryPayments);
    e.EditPayment(monthly.Id, "Новая сумма", 150, clock.Today, Repeat.Monthly, null); Equal(150m, e.Budget.MandatoryPayments);
    e.FinishPayment(monthly.Id); Equal(0m, e.Budget.MandatoryPayments);
    Equal(100.25m, e.PaymentsOn(clock.Today.AddMonths(-1)).Where(p => p.PaymentId == monthly.Id).Sum(p => p.Amount));
});
Test("Lower income or higher payment preserves category limits and exposes a negative remainder", () =>
{
    var (e, clock, _) = Setup(); e.SetBudgetIncome(1000); var category = e.SaveBudgetCategory(null, "Еда", 800);
    e.SetBudgetIncome(500); Equal(-300m, e.Budget.Remaining); Equal(800m, e.State.BudgetCategories.Single().Limit);
    Throws<ArgumentException>(() => e.SaveBudgetCategory(null, "Другая", 1));
    e.SaveBudgetCategory(category.Id, "Еда", 400); Equal(100m, e.Budget.Remaining);
    e.AddPayment("Связь", 200, clock.Today, Repeat.Monthly, null); Equal(-100m, e.Budget.Remaining);
    e.DeleteBudgetCategory(category.Id); Equal(300m, e.Budget.Remaining);
    e.SetBudgetIncome(0); Equal(0m, e.Budget.Percent(200)); Equal(-200m, e.Budget.Remaining);
});
Test("Budget mutations roll back together on failed persistence", () =>
{
    var (e, _, store) = Setup(); e.SetBudgetIncome(100); var category = e.SaveBudgetCategory(null, "Еда", 20);
    store.FailSave = true;
    Throws<IOException>(() => e.SetBudgetIncome(200)); Equal(100m, e.Budget.Income);
    Throws<IOException>(() => e.SaveBudgetCategory(category.Id, "Другое", 40)); Equal("Еда", e.State.BudgetCategories.Single().Name);
    Throws<IOException>(() => e.SaveBudgetCategory(null, "Новая", 10)); Equal(1, e.State.BudgetCategories.Count);
    Throws<IOException>(() => e.DeleteBudgetCategory(category.Id)); Equal(80m, e.Budget.Remaining);
});
Test("Schema 3 upgrade preserves every old row and stores one budget across months", () =>
{
    var folder = Path.Combine(Path.GetTempPath(), "Planer-test-" + Guid.NewGuid()); Directory.CreateDirectory(folder);
    try
    {
        string path = Path.Combine(folder, "planner.db"); var clock = new TestClock { Today = new(2026, 10, 13) };
        var store = new SqlitePlannerStore(path); var e = new PlannerEngine(store, clock);
        var habit = e.AddHabit("История", Palette.Elements[3]); e.ToggleHabit(habit.Id, clock.Today);
        var task = e.AddTask("Готово", true, true, clock.Today); e.ToggleTask(task.Id);
        var payment = e.AddPayment("Интернет", 805.50m, clock.Today, Repeat.Monthly, null); e.TogglePayment(payment.Id, clock.Today);
        e.SetGoal(Dates.WeekKey(clock.Today), "Цель"); e.SetNote(Dates.WeekKey(clock.Today), "Заметка");
        using var c = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={path};Pooling=False"); c.Open();
        using (var legacy = c.CreateCommand()) { legacy.CommandText = "DROP TABLE budget_categories; DELETE FROM metadata WHERE id='budget_income'; PRAGMA user_version=3;"; legacy.ExecuteNonQuery(); }
        string OriginalRows()
        {
            var rows = new List<string>();
            foreach (var table in new[] { "tasks", "task_series", "habits", "habit_checks", "payments", "payment_checks", "moods", "goals", "notes", "notifications", "metadata" })
            {
                using var cmd = c.CreateCommand(); cmd.CommandText = $"SELECT id,data FROM {table} WHERE id<>'budget_income' ORDER BY id";
                using var reader = cmd.ExecuteReader(); while (reader.Read()) rows.Add($"{table}:{reader.GetString(0)}:{reader.GetString(1)}");
            }
            return string.Join('\n', rows);
        }
        string before = OriginalRows(); store = new SqlitePlannerStore(path); e = new PlannerEngine(store, clock);
        Equal(0m, e.Budget.Income); Equal(0, e.State.BudgetCategories.Count); Equal(before, OriginalRows());
        e.SetBudgetIncome(10000.25m); var category = e.SaveBudgetCategory(null, "Продукты", 2500.10m);
        Equal(before, OriginalRows()); clock.Today = new(2026, 11, 2); e = new PlannerEngine(new SqlitePlannerStore(path), clock);
        Equal(10000.25m, e.Budget.Income); Equal(category.Id, e.State.BudgetCategories.Single().Id); Equal(2500.10m, e.Budget.CategoryLimits);
        Equal(true, e.TaskById(task.Id)!.Completed); Equal(1, e.State.HabitChecks.Count); Equal(1, e.State.PaymentChecks.Count);
        using var version = c.CreateCommand(); version.CommandText = "PRAGMA user_version"; Equal(4L, (long)version.ExecuteScalar()!);
    }
    finally { Directory.Delete(folder, true); }
});

Console.WriteLine($"{passed} passed, {failed} failed");
return failed == 0 ? 0 : 1;

sealed class TestClock : IPlannerClock { public DateOnly Today { get; set; } }
sealed class MemoryStore : IPlannerStore
{
    private string _json = JsonSerializer.Serialize(new PlannerState());
    public bool FailSave { get; set; }
    public PlannerState Load() => JsonSerializer.Deserialize<PlannerState>(_json)!;
    public void Save(PlannerState state)
    {
        if (FailSave) throw new IOException("simulated failure");
        _json = JsonSerializer.Serialize(state);
    }
}
