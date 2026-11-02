using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Furion;
using Furion.Logging;
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
            if (!directory.Exists) continue;

            var expiredFiles = directory.GetFiles()
                .Where(file => DateTime.Now - file.CreationTime > TimeSpan.FromMinutes(10));

            foreach (var file in expiredFiles)
            {
                try
                {
                    file.Delete();
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // 文件被占用（如正在写入）或无权限：跳过本轮，下一周期重试，不影响其他文件清理
                    Log.Warning($"清理临时文件失败，已跳过：{file.FullName}，原因：{ex.Message}");
                }
            }
        }

        return Task.CompletedTask;
    }
}