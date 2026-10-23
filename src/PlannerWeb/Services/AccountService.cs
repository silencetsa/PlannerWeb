using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Planer.Data;

namespace PlannerWeb.Services;

public sealed class AccountService(WebDatabase database, IPasswordHasher<WebUser> hasher)
{
    private readonly WebUser _dummy = new("", "", "", "", 0, null);
    private readonly string _dummyHash = hasher.HashPassword(new("", "", "", "", 0, null), Guid.NewGuid().ToString());
    public async Task<WebUser> Register(string nickname, string email, string password)
    {
        await database.Gate.WaitAsync();
        try
        {
            if (database.UserByLogin(nickname) is not null || database.UserByLogin(email) is not null)
                throw new ArgumentException("Никнейм или почта уже используются");
            try { return database.CreateUser(nickname, email, hasher.HashPassword(_dummy, password)); }
            catch (SqliteException e) when (e.SqliteErrorCode == 19) { throw new ArgumentException("Никнейм или почта уже используются"); }
        }
        finally { database.Gate.Release(); }
    }
    public async Task<WebUser?> Login(string login, string password)
    {
        await database.Gate.WaitAsync();
        try
        {
            var u = database.UserByLogin(login);
            var result = hasher.VerifyHashedPassword(u ?? _dummy, u?.PasswordHash ?? _dummyHash, password);
            if (u is null || u.LockedUntil > DateTimeOffset.UtcNow) return null;
            if (result == PasswordVerificationResult.Failed) { database.UpdateLogin(u, false); return null; }
            database.UpdateLogin(u, true, result == PasswordVerificationResult.SuccessRehashNeeded ? hasher.HashPassword(u, password) : null);
            return u;
        }
        finally { database.Gate.Release(); }
    }
}
