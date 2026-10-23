using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Planer.Core;
using Planer.Data;
using PlannerWeb.Models;
using PlannerWeb.Services;

var folder = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "PlannerWeb-tests-" + Guid.NewGuid());
Directory.CreateDirectory(folder);
int passed = 0;
void Check(bool ok, string name) { if (!ok) throw new Exception(name); Console.WriteLine("PASS " + name); passed++; }
void Throws(Action action, string name) { bool thrown = false; try { action(); } catch { thrown = true; } Check(thrown, name); }
try
{
    var db = new WebDatabase(System.IO.Path.Combine(folder, "web.db"));
    var accounts = new AccountService(db, new PasswordHasher<WebUser>());
    var a = await accounts.Register("Alice", "alice@example.test", "Only-for-tests-123");
    var b = await accounts.Register("Боб", "bob@example.test", "Only-for-tests-456");
    Check(a.PasswordHash != "Only-for-tests-123" && a.PasswordHash != b.PasswordHash, "salted password hashes");
    Check((await accounts.Login("ALICE", "Only-for-tests-123"))?.Id == a.Id, "nickname login ignores case");
    Check((await accounts.Login("ALICE@EXAMPLE.TEST", "Only-for-tests-123"))?.Id == a.Id, "email login ignores case");
    Check(await accounts.Login("Alice", "wrong") is null, "wrong password rejected");
    bool duplicate = false; try { await accounts.Register("alice", "other@example.test", "Only-for-tests-123"); } catch (ArgumentException) { duplicate = true; } Check(duplicate, "normalized nickname uniqueness");
    for (int i = 0; i < 5; i++) await accounts.Login("Боб", "wrong");
    Check(await accounts.Login("Боб", "Only-for-tests-456") is null, "account lockout after five failures");
    var databaseBytes = File.ReadAllBytes(db.Path); new WebDatabase(db.Path);
    Check(databaseBytes.SequenceEqual(File.ReadAllBytes(db.Path)), "opening existing schema does not rewrite database bytes");
    var clock = new Clock { Today = new(2026, 10, 13) };
    var sa = new UserPlannerStore(db, a.Id); var sb = new UserPlannerStore(db, b.Id);
    var ea = new PlannerEngine(sa, clock); var eb = new PlannerEngine(sb, clock);
    var t = ea.AddTask("private task", true, true, clock.Today, DayPart.Morning, Repeat.Monthly);
    ea.ToggleTask(t.Id); var habit = ea.AddHabit("Читать", Palette.Elements[3]); ea.ToggleHabit(habit.Id, clock.Today);
    var payment = ea.AddPayment("Интернет", 800.50m, clock.Today, Repeat.Monthly, null); ea.TogglePayment(payment.Id, clock.Today);
    ea.ToggleMood(clock.Today, Mood.Happy); ea.SetGoal(Dates.WeekKey(clock.Today), "private goal"); ea.SetNote(Dates.MonthKey(clock.Today), "private note"); ea.SetBudgetIncome(10000); ea.SaveBudgetCategory(null, "Продукты", 2000);
    Check(sb.Load().Tasks.Count == 0 && sb.Load().Habits.Count == 0 && sb.Load().Payments.Count == 0 && sb.Load().BudgetIncome == 0 && sb.Load().Goals.Count == 0 && sb.Load().Moods.Count == 0, "all planner entities isolated");
    eb.SetGoal(Dates.WeekKey(clock.Today), "bob goal"); eb.ToggleMood(clock.Today, Mood.Sad); eb.SetBudgetIncome(200);
    Check(sa.Load().Goals[Dates.WeekKey(clock.Today)] == "private goal" && sa.Load().Moods[clock.Today] == Mood.Happy && sa.Load().BudgetIncome == 10000, "same period keys do not collide between owners");
    Throws(() => eb.ToggleTask(t.Id), "foreign task cannot be toggled");
    Throws(() => eb.ToggleHabit(habit.Id, clock.Today), "foreign habit cannot be toggled");
    Throws(() => eb.DeletePayment(payment.Id), "foreign payment cannot be deleted");
    Throws(() => eb.SaveBudgetCategory(ea.State.BudgetCategories.Single().Id, "hack", 1), "foreign category cannot be edited");
    var own = eb.AddTask("bob task", false, false, clock.Today); eb.DeleteTask(own.Id);
    Check(sa.Load().Tasks.Single().Id == t.Id, "deletion in one account preserves other account rows");
    var restarted = new PlannerEngine(new UserPlannerStore(new WebDatabase(db.Path), a.Id), clock);
    Check(restarted.State.Tasks.Single().Completed && restarted.State.HabitChecks.Count == 1 && restarted.State.PaymentChecks.Count == 1 && restarted.Budget.Income == 10000, "owner history survives database restart");
    restarted.EnsureTaskRange(new(2026, 10, 1), new(2027, 1, 31));
    var occurrence = restarted.TasksOn(new(2026, 11, 13)).Single(); restarted.DeleteTask(occurrence.Id, TaskDeleteScope.Occurrence);
    restarted = new PlannerEngine(new UserPlannerStore(db, a.Id), clock); restarted.EnsureTaskRange(new(2026, 10, 1), new(2027, 1, 31));
    Check(!restarted.TasksOn(new(2026, 11, 13)).Any() && restarted.TasksOn(new(2026, 12, 13)).Any(), "excluded recurring occurrence survives owner store restart");
    clock.Today = new(2026, 10, 14); restarted.FinishHabit(habit.Id); restarted.DeletePayment(payment.Id);
    Check(restarted.HabitsOn(new(2026, 10, 13)).Any(h => h.Completed) && restarted.PaymentsOn(new(2026, 10, 13)).Single().Completed, "habit and payment history retained");
    clock.Today = new(2026, 10, 26); restarted.AddTask("overdue", false, false, new(2026, 10, 15)); restarted.ReconcileWeek();
    Check(restarted.Backlog.Any(x => x.Description == "overdue") && restarted.TaskById(t.Id)!.Completed, "rollover preserves completed past");
    using (var connection = db.Open())
    {
        foreach (var table in WebDatabase.Tables) { using var cmd = connection.CreateCommand(); cmd.CommandText = $"PRAGMA foreign_key_list({table})"; using var reader = cmd.ExecuteReader(); Check(reader.Read() && reader.GetString(2) == "users", "owner FK: " + table); }
        using var integrity = connection.CreateCommand(); integrity.CommandText = "PRAGMA integrity_check"; Check((string)integrity.ExecuteScalar()! == "ok", "SQLite integrity");
    }
    Throws(() => new UserPlannerStore(db, "missing-user"), "unknown owner rejected");
    var service = new PlannerService(db, new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Planner:TimeZone"] = "Asia/Yekaterinburg" }).Build());
    await Task.WhenAll(Enumerable.Range(0, 25).Select(i => service.WithEngine(b.Id, e => e.AddTask("concurrent " + i, false, false, null))));
    Check(sb.Load().Tasks.Count == 25, "concurrent requests do not lose updates");
    Throws(() => PlannerService.Execute(eb, new PlannerCommand { Action = "task.save", Text = "bad", Repeat = 99 }), "invalid repeat rejected at web boundary");
    var desktopPath = System.IO.Path.Combine(folder, "desktop.db"); new SqlitePlannerStore(desktopPath);
    Throws(() => new WebDatabase(desktopPath), "desktop database refused without modification");
    Check(Enum.GetValues<Repeat>().OrderByDescending(PlannerPage.FrequencyRank).SequenceEqual(new[] { Repeat.Weekly, Repeat.Fortnightly, Repeat.Monthly, Repeat.Yearly, Repeat.None }), "payment groups retain desktop frequency order");
    Check(PlannerPage.Money(805.5m).EndsWith("805,50 ₽") && PlannerPage.Money(805).EndsWith("805 ₽"), "money presentation retains cents and ruble suffix");
    Console.WriteLine($"{passed} web checks passed"); return 0;
}
finally { Directory.Delete(folder, true); }
sealed class Clock : IPlannerClock { public DateOnly Today { get; set; } }

