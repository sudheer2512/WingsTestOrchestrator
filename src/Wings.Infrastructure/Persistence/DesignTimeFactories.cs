using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Wings.Infrastructure.Persistence;

/// <summary>Used only by `dotnet ef` to generate migrations; no database connection is opened.</summary>
public sealed class SqliteDesignTimeFactory : IDesignTimeDbContextFactory<SqliteWingsDbContext>
{
    public SqliteWingsDbContext CreateDbContext(string[] args)
        => new(new DbContextOptionsBuilder<SqliteWingsDbContext>().UseSqlite("Data Source=design.db").Options);
}

public sealed class SqlServerDesignTimeFactory : IDesignTimeDbContextFactory<SqlServerWingsDbContext>
{
    public SqlServerWingsDbContext CreateDbContext(string[] args)
        => new(new DbContextOptionsBuilder<SqlServerWingsDbContext>().UseSqlServer("Server=.;Database=WingsTestOrchestrator;Trusted_Connection=True;TrustServerCertificate=True").Options);
}
