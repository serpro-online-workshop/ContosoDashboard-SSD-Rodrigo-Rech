using ContosoDashboard.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace ContosoDashboard.Tests.Fixtures;

public sealed class SqliteTestFactory : IAsyncDisposable
{
    private readonly string _databasePath = Path.Combine(Path.GetTempPath(), $"contoso-documents-{Guid.NewGuid():N}.db");
    private readonly SqliteConnection _connection;

    public SqliteTestFactory()
    {
        _connection = new SqliteConnection($"Data Source={_databasePath}");
        _connection.Open();
        Options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(_connection)
            .EnableSensitiveDataLogging()
            .Options;
    }

    public DbContextOptions<ApplicationDbContext> Options { get; }

    public ApplicationDbContext CreateContext() => new(Options);

    public async Task EnsureCreatedAsync()
    {
        await using var context = CreateContext();
        await context.Database.EnsureCreatedAsync();
    }

    public async ValueTask DisposeAsync()
    {
        await _connection.DisposeAsync();
        TryDelete(_databasePath);
        TryDelete($"{_databasePath}-shm");
        TryDelete($"{_databasePath}-wal");
    }

    private static void TryDelete(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }
}
