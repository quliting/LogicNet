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
        var directories = App.GetConfig<string>("AppSettings:DeleteDirectory")
            .Split(",", StringSplitOptions.RemoveEmptyEntries);

        foreach (var directory in directories.Select(dir =>
                     Path.Combine(App.WebHostEnvironment.WebRootPath, dir).AsDirectory()))
        {
            var expiredFiles = directory.GetFiles()
                .Where(file => DateTime.Now - file.CreationTime > TimeSpan.FromMinutes(10));

            foreach (var file in expiredFiles)
            {
                file.Delete();
            }
        }

        return Task.CompletedTask;
    }
}