using System;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Logging;

namespace Kavita.Server.Helpers;

/// <summary>Only removes an empty, orphaned EF rebuild table from an incomplete fresh installation.</summary>
public static class SqliteMigrationRecovery
{
    public static async Task<bool> HasManualMigrationHistoryAsync(DatabaseFacade database, CancellationToken cancellationToken = default)
    {
        await database.OpenConnectionAsync(cancellationToken);
        try
        {
            return await TableExistsAsync(database.GetDbConnection(), "ManualMigrationHistory", cancellationToken);
        }
        finally
        {
            await database.CloseConnectionAsync();
        }
    }

    public static async Task<bool> RemoveEmptyOrphanedVolumeTableAsync(
        DatabaseFacade database, ILogger logger, CancellationToken cancellationToken = default)
    {
        await database.OpenConnectionAsync(cancellationToken);
        try
        {
            var connection = database.GetDbConnection();
            if (!await TableExistsAsync(connection, "ef_temp_Volume", cancellationToken)) return false;

            // Do not touch rebuild tables in existing installations or any table holding records.
            if (!await TableExistsAsync(connection, "Volume", cancellationToken) ||
                await TableExistsAsync(connection, "ManualMigrationHistory", cancellationToken) ||
                await HasRowsAsync(connection, "Volume", cancellationToken) ||
                await HasRowsAsync(connection, "ef_temp_Volume", cancellationToken))
            {
                throw new InvalidOperationException(
                    "ef_temp_Volume exists but cannot safely be removed: the database may contain user data.");
            }

            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = "DROP TABLE \"ef_temp_Volume\";";
            await command.ExecuteNonQueryAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            logger.LogWarning("Removed empty ef_temp_Volume left by an interrupted fresh-install migration");
            return true;
        }
        finally
        {
            await database.CloseConnectionAsync();
        }
    }

    private static async Task<bool> TableExistsAsync(DbConnection connection, string name, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT EXISTS(SELECT 1 FROM sqlite_master WHERE type = 'table' AND name = @name);";
        var parameter = command.CreateParameter();
        parameter.ParameterName = "@name";
        parameter.Value = name;
        command.Parameters.Add(parameter);
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken)) != 0;
    }

    private static async Task<bool> HasRowsAsync(DbConnection connection, string name, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = name switch
        {
            "Volume" => "SELECT EXISTS(SELECT 1 FROM \"Volume\");",
            "ef_temp_Volume" => "SELECT EXISTS(SELECT 1 FROM \"ef_temp_Volume\");",
            _ => throw new ArgumentOutOfRangeException(nameof(name)),
        };
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken)) != 0;
    }
}
