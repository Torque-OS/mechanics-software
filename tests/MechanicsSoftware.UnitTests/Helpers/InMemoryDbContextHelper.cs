using Microsoft.EntityFrameworkCore;
using MechanicsSoftware.Infrastructure.Persistence;
using System.Runtime.CompilerServices;

namespace MechanicsSoftware.UnitTests.Helpers;

public static class InMemoryDbContextHelper
{
    private static readonly ConditionalWeakTable<AppDbContext, InMemoryStatusHistoryStore> HistoryStores = [];

    public static AppDbContext Create(string? dbName = null)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(dbName ?? Guid.NewGuid().ToString())
            .Options;

        var historyStore = new InMemoryStatusHistoryStore();
        var db = new AppDbContext(options, historyStore);
        HistoryStores.Add(db, historyStore);
        return db;
    }

    public static InMemoryStatusHistoryStore GetHistoryStore(AppDbContext db) =>
        HistoryStores.GetValue(db, _ => new InMemoryStatusHistoryStore());
}
