using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Furion;
using Furion.Schedule;

namespace LogicNet.Web.Core.Job;

public class DeleteDirJob : IJob
{
    public Task ExecuteAsync(JobExecutingContext context, CancellationToken stoppingToken)
    {
        var directory = Path.Combine(App.WebHostEnvironment.WebRootPath, "Temp");
        directory.AsDirectory().GetFiles()
            .Where(file => DateTime.Now - file.CreationTime > TimeSpan.FromMinutes(10))
            .ToList()
            .ForEach(file => file.Delete());

        return Task.CompletedTask;
    }
}