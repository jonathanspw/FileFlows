using FileFlows.Server.Services;
using FileFlows.ServerShared.Workers;
using FileFlows.Shared.Models;

namespace FileFlows.Server.Workers;

/// <summary>
/// Worker that run in the FileFLows Server
/// </summary>
public abstract class ServerWorker : Worker
{
    /// <summary>
    /// Creates a new instance of the server worker
    /// </summary>
    /// <param name="schedule">the type of schedule this worker runs at</param>
    /// <param name="interval">the interval of this worker</param>
    protected ServerWorker(ScheduleType schedule, int interval) : base(schedule, interval)
    {
    }

    /// <inheritdoc />
    protected override void Execute()
    {
        var settings = ServiceLoader.Load<SettingsService>().Get().Result;
        if (settings.EulaAccepted == false)
        {
            Logger.Instance.ILog("EULA Not accepted cannot execute worker: " + GetType().Name);
            return; // cannot proceed unless they have accepted the EULA
        }

        ExecuteActual(settings);
    }

    /// <summary>
    /// Executes the actual worker
    /// </summary>
    protected abstract void ExecuteActual(Settings settings);
}