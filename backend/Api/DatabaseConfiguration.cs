using MySql.Data.MySqlClient;

namespace XieBox.Api;

public static class DatabaseConfiguration
{
    public static string GetConnectionString(IConfiguration configuration)
    {
        var connection = configuration.GetConnectionString("PlatformDatabase");
        if (string.IsNullOrWhiteSpace(connection))
            throw new InvalidOperationException("Configure ConnectionStrings__PlatformDatabase for MySQL. See backend/Database/README.md.");
        var parsed = new MySqlConnectionStringBuilder(connection);
        if (string.IsNullOrWhiteSpace(parsed.Database) || string.IsNullOrWhiteSpace(parsed.UserID))
            throw new InvalidOperationException("The MySQL connection must specify Database and User ID.");
        return connection;
    }

    public static async Task CheckEmptyTestDatabaseAsync(string connectionString)
    {
        var parsed = new MySqlConnectionStringBuilder(connectionString);
        if (!parsed.Database.StartsWith("xiebox_test_", StringComparison.Ordinal)
            || !System.Text.RegularExpressions.Regex.IsMatch(parsed.Database, "^[a-z0-9_]+$"))
            throw new InvalidOperationException("Integration tests require a separate database named xiebox_test_*.");
        await using var connection = new MySqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM information_schema.tables WHERE table_schema = DATABASE()";
        if (Convert.ToInt64(await command.ExecuteScalarAsync()) != 0)
            throw new InvalidOperationException("Integration tests require an empty database; existing data will not be overwritten. Create a fresh xiebox_test_* database.");
    }

    public static async Task ValidateDatabaseAsync(string connectionString)
    {
        await using var connection = new MySqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT DEFAULT_CHARACTER_SET_NAME, DEFAULT_COLLATION_NAME FROM information_schema.schemata WHERE SCHEMA_NAME = DATABASE()";
        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync() || reader.GetString(0) != "utf8mb4" || reader.GetString(1) != "utf8mb4_bin")
            throw new InvalidOperationException("MySQL database must use utf8mb4 and utf8mb4_bin. See backend/Database/create-database.sql.");
    }
}
