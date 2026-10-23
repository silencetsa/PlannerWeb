using Microsoft.Data.Sqlite;

namespace Planer.Data;

public sealed record WebUser(string Id, string Nickname, string Email, string PasswordHash, int FailedAttempts, DateTimeOffset? LockedUntil);

/// <summary>Web-only schema. Every planner row has an owner FK and a composite primary key.</summary>
public sealed class WebDatabase
{
    public static readonly string[] Tables = ["tasks", "task_series", "habits", "habit_checks", "payments", "payment_checks", "moods", "goals", "notes", "metadata", "notifications", "budget_categories"];
    public string Path { get; }
    // One process/replica: hold this gate over the complete load -> domain command -> save cycle.
    public SemaphoreSlim Gate { get; } = new(1, 1);
    public WebDatabase(string path)
    {
        Path = System.IO.Path.GetFullPath(path);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
        using var c = Open(); using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='tasks' AND sql NOT LIKE '%owner_id%'";
        if (Convert.ToInt32(cmd.ExecuteScalar()) > 0) throw new InvalidOperationException("A desktop database cannot be opened as a web database. Use a separate data directory.");
        // DELETE journal is suitable for Amvera persistent network volumes (no WAL shared-memory dependency).
        cmd.CommandText = "PRAGMA journal_mode=DELETE; CREATE TABLE IF NOT EXISTS users(id TEXT PRIMARY KEY, nickname TEXT NOT NULL, normalized_nickname TEXT NOT NULL UNIQUE, email TEXT NOT NULL, normalized_email TEXT NOT NULL UNIQUE, password_hash TEXT NOT NULL, failed_attempts INTEGER NOT NULL DEFAULT 0, locked_until TEXT);"
            + string.Join("", Tables.Select(t => $"CREATE TABLE IF NOT EXISTS {t}(owner_id TEXT NOT NULL REFERENCES users(id) ON DELETE CASCADE, id TEXT NOT NULL, data TEXT NOT NULL, PRIMARY KEY(owner_id,id));")) + "PRAGMA user_version=1;";
        cmd.ExecuteNonQuery();
    }
    public SqliteConnection Open()
    {
        var c = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = Path, Pooling = false, ForeignKeys = true, DefaultTimeout = 30 }.ToString());
        c.Open(); return c;
    }
    public bool Healthy() { using var c = Open(); using var cmd = c.CreateCommand(); cmd.CommandText = "SELECT 1"; return Convert.ToInt32(cmd.ExecuteScalar()) == 1; }
    public static string Normalize(string value) => value.Trim().Normalize(System.Text.NormalizationForm.FormKC).ToUpperInvariant();
    public WebUser? UserById(string id) => Find("id", id);
    public WebUser? UserByLogin(string login) => Find(login.Contains('@') ? "normalized_email" : "normalized_nickname", Normalize(login));
    private WebUser? Find(string column, string value)
    {
        using var c = Open(); using var cmd = c.CreateCommand(); cmd.CommandText = $"SELECT id,nickname,email,password_hash,failed_attempts,locked_until FROM users WHERE {column}=$value"; cmd.Parameters.AddWithValue("$value", value);
        using var r = cmd.ExecuteReader();
        return r.Read() ? new(r.GetString(0), r.GetString(1), r.GetString(2), r.GetString(3), r.GetInt32(4), r.IsDBNull(5) ? null : DateTimeOffset.Parse(r.GetString(5), System.Globalization.CultureInfo.InvariantCulture)) : null;
    }
    public WebUser CreateUser(string nickname, string email, string hash)
    {
        var u = new WebUser(Guid.NewGuid().ToString(), nickname.Trim(), email.Trim(), hash, 0, null);
        using var c = Open(); using var cmd = c.CreateCommand(); cmd.CommandText = "INSERT INTO users(id,nickname,normalized_nickname,email,normalized_email,password_hash) VALUES($id,$name,$normalName,$email,$normalEmail,$hash)";
        cmd.Parameters.AddWithValue("$id", u.Id); cmd.Parameters.AddWithValue("$name", u.Nickname); cmd.Parameters.AddWithValue("$normalName", Normalize(u.Nickname)); cmd.Parameters.AddWithValue("$email", u.Email); cmd.Parameters.AddWithValue("$normalEmail", Normalize(u.Email)); cmd.Parameters.AddWithValue("$hash", hash); cmd.ExecuteNonQuery(); return u;
    }
    public void UpdateLogin(WebUser u, bool success, string? newHash = null)
    {
        using var c = Open(); using var cmd = c.CreateCommand();
        cmd.CommandText = "UPDATE users SET failed_attempts=$attempts,locked_until=$until,password_hash=$hash WHERE id=$id";
        var attempts = success ? 0 : u.FailedAttempts + 1;
        cmd.Parameters.AddWithValue("$attempts", attempts >= 5 ? 0 : attempts);
        cmd.Parameters.AddWithValue("$until", !success && attempts >= 5 ? DateTimeOffset.UtcNow.AddMinutes(15).ToString("O") : DBNull.Value);
        cmd.Parameters.AddWithValue("$hash", newHash ?? u.PasswordHash); cmd.Parameters.AddWithValue("$id", u.Id); cmd.ExecuteNonQuery();
    }
}
