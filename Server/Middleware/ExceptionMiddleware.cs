using System.Net;
using FileFlows.Server.Services;

namespace FileFlows.Server.Middleware;

/// <summary>
/// A middleware used to capture all exceptions
/// </summary>
public class ExceptionMiddleware
{
    private readonly RequestDelegate _next;

    /// <summary>
    /// Constructs a instance of the exception middleware
    /// </summary>
    /// <param name="next">the next middleware to call</param>
    public ExceptionMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    /// <summary>
    /// Invokes the middleware
    /// </summary>
    /// <param name="context">the HttpContext executing this middleware</param>
    public async Task Invoke(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            context.Response.ContentType = "text/plain";
            context.Response.StatusCode = (int)HttpStatusCode.InternalServerError;

            string exceptionMessage = ex.StackTrace ?? string.Empty;
            // Check if the exception message contains "at Npgsql.NpgsqlConnection.Open"
            if (exceptionMessage.Contains("at Npgsql.NpgsqlConnection.Open") ||
                exceptionMessage.Contains("at Npgsql.Internal.NpgsqlConnector.Open") ||
                exceptionMessage.Contains("NpgsqlConnector.Connect"))
            {
                if (WebServer.FullyStarted)
                {
                    _ = ServiceLoader.Load<NotificationService>().RecordDatabaseOffline();
                    Logger.Instance.ELog("ExceptionMiddleware: Database is offline");
                }

                await context.Response.WriteAsync("Database is offline");
            }
            else
            {

                Logger.Instance.ELog("ExceptionMiddleware: " + ex.Message + Environment.NewLine +
                                     $"REQUEST [{context.Request?.Method}] [{context.Response?.StatusCode}]: {context.Request?.Path.Value}" +
                                     Environment.NewLine + ex.StackTrace);
                await context.Response.WriteAsync(ex.Message);
            }

        }
    }
}
