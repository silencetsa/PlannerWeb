using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Planer.Core;

namespace Planer.Data;

/// <summary>Transactional, file-based store. Each entity is independently addressable in SQLite.</summary>
public sealed class UserPlannerStore : IPlannerStore
{
    public string DatabasePath { get; }
    private readonly string _connectionString;
    private static readonly string[] Tables = ["tasks", "task_series", "habits", "habit_checks", "payments", "payment_checks", "moods", "goals", "notes", "metadata", "notifications", "budget_categories"];

    private readonly string _owner;
    public UserPlannerStore(WebDatabase database, string owner)
    {
        if (database.UserById(owner) is null) throw new ArgumentException("Unknown owner");
        _owner = owner;
        DatabasePath = database.Path;
        _connectionString = new SqliteConnectionStringBuilder { DataSource = DatabasePath, Pooling = false, ForeignKeys = true, DefaultTimeout = 30 }.ToString();
    }
    private SqliteConnection Open()
    {
        var c = new SqliteConnection(_connectionString);
        c.Open();
        return c;
    }
    public PlannerState Load()
    {
        using var c = Open();
        var s = new PlannerState
        {
            Tasks = Read<PlannerTask>(c, "tasks"), Habits = Read<Habit>(c, "habits"),
            TaskSeries = Read<TaskSeries>(c, "task_series"),
            HabitChecks = Read<HabitCheck>(c, "habit_checks"), Payments = Read<Payment>(c, "payments"),
            PaymentChecks = Read<PaymentCheck>(c, "payment_checks"), Notifications = Read<PlannerNotification>(c, "notifications"),
            BudgetCategories = Read<BudgetCategory>(c, "budget_categories")
        };
        foreach (var (key, data) in ReadRows(c, "moods")) s.Moods[DateOnly.ParseExact(key, "yyyy-MM-dd", CultureInfo.InvariantCulture)] = JsonSerializer.Deserialize<Mood>(data);
        foreach (var (key, data) in ReadRows(c, "goals")) s.Goals[key] = JsonSerializer.Deserialize<string>(data)!;
        foreach (var (key, data) in ReadRows(c, "notes")) s.Notes[key] = JsonSerializer.Deserialize<string>(data)!;
        var rollover = ReadRows(c, "metadata").FirstOrDefault(r => r.Key == "last_rollover_week");
        if (rollover.Data is not null) s.LastRolloverWeek = JsonSerializer.Deserialize<DateOnly?>(rollover.Data);
        var metadata = ReadRows(c, "metadata").ToDictionary(r => r.Key, r => r.Data);
        s.NotificationTrackingInitialized = metadata.TryGetValue("notification_tracking", out var tracking) && JsonSerializer.Deserialize<bool>(tracking);
        s.NotificationsEnabled = !metadata.TryGetValue("notifications_enabled", out var enabled) || JsonSerializer.Deserialize<bool>(enabled);
        s.BudgetIncome = metadata.TryGetValue("budget_income", out var income) ? JsonSerializer.Deserialize<decimal>(income) : 0;
        return s;
    }
    private List<T> Read<T>(SqliteConnection c, string table) => ReadRows(c, table).Select(r => JsonSerializer.Deserialize<T>(r.Data)!).ToList();
    private List<(string Key, string Data)> ReadRows(SqliteConnection c, string table, SqliteTransaction? transaction = null)
    {
        using var command = c.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"SELECT id,data FROM {table} WHERE owner_id=$owner"; command.Parameters.AddWithValue("$owner", _owner);
        using var reader = command.ExecuteReader();
        var rows = new List<(string, string)>();
        while (reader.Read()) rows.Add((reader.GetString(0), reader.GetString(1)));
        return rows;
    }
    public void Save(PlannerState s)
    {
        using var c = Open();
        using var transaction = c.BeginTransaction();
        Write(c, transaction, "tasks", s.Tasks.Select(x => (x.Id.ToString(), JsonSerializer.Serialize(x))));
        Write(c, transaction, "task_series", s.TaskSeries.Select(x => (x.Id.ToString(), JsonSerializer.Serialize(x))));
        Write(c, transaction, "habits", s.Habits.Select(x => (x.Id.ToString(), JsonSerializer.Serialize(x))));
        Write(c, transaction, "habit_checks", s.HabitChecks.Select(x => ($"{x.HabitId}:{x.Date:yyyy-MM-dd}", JsonSerializer.Serialize(x))));
        Write(c, transaction, "payments", s.Payments.Select(x => (x.Id.ToString(), JsonSerializer.Serialize(x))));
        Write(c, transaction, "payment_checks", s.PaymentChecks.Select(x => ($"{x.PaymentId}:{x.Date:yyyy-MM-dd}", JsonSerializer.Serialize(x))));
        Write(c, transaction, "moods", s.Moods.Select(x => ($"{x.Key:yyyy-MM-dd}", JsonSerializer.Serialize(x.Value))));
        Write(c, transaction, "goals", s.Goals.Select(x => (x.Key, JsonSerializer.Serialize(x.Value))));
        Write(c, transaction, "notes", s.Notes.Select(x => (x.Key, JsonSerializer.Serialize(x.Value))));
        Write(c, transaction, "notifications", s.Notifications.Select(n => (n.Key, JsonSerializer.Serialize(n))));
        Write(c, transaction, "budget_categories", s.BudgetCategories.Select(x => (x.Id.ToString(), JsonSerializer.Serialize(x))));
        Write(c, transaction, "metadata", [("last_rollover_week", JsonSerializer.Serialize(s.LastRolloverWeek)),
            ("notification_tracking", JsonSerializer.Serialize(s.NotificationTrackingInitialized)), ("notifications_enabled", JsonSerializer.Serialize(s.NotificationsEnabled)),
            ("budget_income", JsonSerializer.Serialize(s.BudgetIncome))]);
        transaction.Commit();
    }
    private void Write(SqliteConnection c, SqliteTransaction transaction, string table, IEnumerable<(string Key, string Data)> rows)
    {
        // Upsert only modified rows; delete only rows no longer present in this entity set.
        var materialized = rows.ToList();
        using var command = c.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"INSERT INTO {table}(owner_id,id,data) VALUES($owner,$id,$data) ON CONFLICT(owner_id,id) DO UPDATE SET data=excluded.data WHERE data<>excluded.data";
        command.Parameters.AddWithValue("$owner", _owner);
        var id = command.Parameters.Add("$id", SqliteType.Text);
        var data = command.Parameters.Add("$data", SqliteType.Text);
        foreach (var row in materialized) { id.Value = row.Key; data.Value = row.Data; command.ExecuteNonQuery(); }
        var keys = materialized.Select(x => x.Key).ToHashSet();
        foreach (var row in ReadRows(c, table, transaction).Where(x => !keys.Contains(x.Key)))
        {
            using var delete = c.CreateCommand();
            delete.Transaction = transaction;
            delete.CommandText = $"DELETE FROM {table} WHERE owner_id=$owner AND id=$id";
            delete.Parameters.AddWithValue("$owner", _owner); delete.Parameters.AddWithValue("$id", row.Key);
            delete.ExecuteNonQuery();
        }
    }
}

