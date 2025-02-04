using System.Text;
using System.Text.Json;
using FileFlows.DataLayer.DatabaseConnectors;
using FileFlows.DataLayer.Converters;
using FileFlows.DataLayer.Helpers;
using FileFlows.DataLayer.Models;
using FileFlows.Plugin;
using FileFlows.ServerShared.Models;
using FileFlows.Shared;
using FileFlows.Shared.Models;

namespace FileFlows.DataLayer;

/// <summary>
/// Manages data access operations for the DbLogMessage table
/// </summary>
internal class DbLogMessageManager : BaseManager
{
    /// <summary>
    /// Initializes a new instance of the DbLogMessage manager
    /// </summary>
    /// <param name="logger">the logger</param>
    /// <param name="dbType">the type of database</param>
    /// <param name="dbConnector">the database connector</param>
    public DbLogMessageManager(ILogger logger, DatabaseType dbType, IDatabaseConnector dbConnector) : base(logger,
        dbType, dbConnector)
    {
    }

    /// <summary>
    /// Fetches all items
    /// </summary>
    /// <returns>the items</returns>
    internal async Task<List<DbLogMessage>> GetAll()
    {
        using var db = await DbConnector.GetDb();
        return (await db.Db.FetchAsync<DbLogMessage>()).ToList();
    }

    /// <summary>
    /// Bulk inserts multiple messages
    /// </summary>
    /// <param name="messages">the messages to insert</param>
    public async Task BulkInsert(params DbLogMessage[] messages)
    {
        using var db = await DbConnector.GetDb();
        db.Db.BeginTransaction();

        try
        {
            foreach (var msg in messages)
            {
                string sql =
                    $"insert into {Wrap(nameof(DbLogMessage))} " +
                    $" ({Wrap(nameof(DbLogMessage.ClientUid))}, " +
                    $" {Wrap(nameof(DbLogMessage.LogDate))}, " +
                    $" {Wrap(nameof(DbLogMessage.Type))}, " +
                    $" {Wrap(nameof(DbLogMessage.Message))}) " +
                    " values " +
                    $"('{(msg.ClientUid == Guid.Empty ? "" : msg.ClientUid)}', {DbConnector.FormatDateQuoted(msg.LogDate)}, {(int)msg.Type}, @0)";

                await db.Db.ExecuteAsync(sql, msg.Message);
            }

            db.Db.CompleteTransaction();
        }
        catch (Exception)
        {
            db.Db.AbortTransaction();
        }
    }

    /// <summary>
    /// Deletes old log messages
    /// </summary>
    /// <param name="max">the max log message to retrain</param>
    /// <returns>a task to await</returns>
    public async Task PruneOldLogs(int max)
    {
        string sql = $@"
DELETE FROM {Wrap(nameof(DbLogMessage))}
WHERE {Wrap(nameof(DbLogMessage.LogDate))} < (
    SELECT MIN({Wrap(nameof(DbLogMessage.LogDate))})
    FROM (
        SELECT {Wrap(nameof(DbLogMessage.LogDate))}, 
               ROW_NUMBER() OVER (ORDER BY {Wrap(nameof(DbLogMessage.LogDate))} DESC) AS RowNumber
        FROM {Wrap(nameof(DbLogMessage))}
    ) AS SubQuery
    WHERE RowNumber <= {max}
);
";
        try
        {
            using var db = await DbConnector.GetDb();
            await db.Db.ExecuteAsync(sql);
        }
        catch (Exception ex)
        {
            Logger.WLog("Error pruning old logs: " + ex.Message + Environment.NewLine + sql);
        }
    }

    /// <summary>
    /// Searches the database for the log messages
    /// </summary>
    /// <param name="filter">the search filter</param>
    /// <returns>the matching log messages</returns>
    public async Task<List<DbLogMessage>> Search(LogSearchModel filter)
    {
        var filterType = (int)(filter.Type ?? LogType.Info);
        var sql = $@"
select * from {Wrap(nameof(DbLogMessage))}
where {Wrap(nameof(DbLogMessage.Type))} {(filter.TypeIncludeHigherSeverity ? "<=" : "=")} {filterType}
and {Wrap(nameof(DbLogMessage.ClientUid))} = {SqlHelper.Escape(filter.Source)}
and ( {Wrap(nameof(DbLogMessage.LogDate))} between {DbConnector.FormatDateQuoted(filter.FromDate)}
and {DbConnector.FormatDateQuoted(filter.ToDate)} )
";
        if (string.IsNullOrWhiteSpace(filter.Message) == false)
            sql += $" and lower({Wrap(nameof(DbLogMessage.Message))}) " +
                   $" like lower('%{filter.Message.Replace("'", "''").Replace(" ", "%")}%') ";

        sql += $" order by {Wrap(nameof(DbLogMessage.LogDate))} desc ";

        sql += DbType switch
        {
            DatabaseType.SqlServer => $" OFFSET 0 ROWS FETCH NEXT 5000 ROWS ONLY",
            _ => $" LIMIT 5000",
        };

        try
        {
            using var db = await DbConnector.GetDb();
            var results = await db.Db.FetchAsync<DbLogMessage>(sql);
            results.Reverse();
            return results;
        }
        catch (Exception ex)
        {
            string message = "LogMessage Error: " + ex.Message + Environment.NewLine + sql;
            Logger.ELog(message);
            return new List<DbLogMessage>()
            {
                new ()
                {
                    Message = message,
                    Type = LogType.Error,
                    ClientUid = Guid.Empty,
                    LogDate = DateTime.UtcNow
                }
            };
        }
    }
}