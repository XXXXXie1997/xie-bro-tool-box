using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace XieBox.Api;
public sealed class DesignTimeDbFactory : IDesignTimeDbContextFactory<PlatformDb>
{
    public PlatformDb CreateDbContext(string[] args)
    {
        // Model/SQL generation requires no server. Database updates require a real connection from the environment.
        var connection = Environment.GetEnvironmentVariable("ConnectionStrings__PlatformDatabase")
            ?? "Server=127.0.0.1;Port=3306;Database=xiebox;User ID=xiebox;Password=;SslMode=Preferred";
        return new PlatformDb(new DbContextOptionsBuilder<PlatformDb>().UseMySQL(connection).Options);
    }
}
