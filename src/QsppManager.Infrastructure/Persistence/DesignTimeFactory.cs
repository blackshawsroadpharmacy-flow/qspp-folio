using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace QsppManager.Infrastructure.Persistence;

/// <summary>Design-time only: lets `dotnet-ef migrations` build the model without the
/// desktop app's runtime wiring. Never used by the app itself.</summary>
public sealed class DesignTimeFactory : IDesignTimeDbContextFactory<QsppDbContext>
{
    public QsppDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<QsppDbContext>()
            .UseSqlite("Data Source=design-time-placeholder.db")
            .Options;
        return new QsppDbContext(options);
    }
}