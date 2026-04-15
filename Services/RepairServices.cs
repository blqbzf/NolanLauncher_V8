using System;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace NolanWoWLauncher.Services;

public sealed class RepairServices
{
    public OperationResult RunRepairScript(string clientPath)
    {
        var log = new StringBuilder();
        log.AppendLine($"clientPath={clientPath}");

        if (string.IsNullOrWhiteSpace(clientPath) || !Directory.Exists(clientPath))
            return new OperationResult { Success = false, Message = $"客户端目录无效\n{log}" };

        var clientScript = Path.Combine(clientPath, "repair_client.cmd");
        var existedInClient = File.Exists(clientScript);
        log.AppendLine($"clientScript={clientScript}");
        log.AppendLine($"clientScript.exists.before={existedInClient}");

        if (!existedInClient)
        {
            try
            {
                var scriptText = EmbeddedResources.ReadTextOrNull("repair_client.cmd");
                log.AppendLine($"embeddedScript.read.success={!string.IsNullOrEmpty(scriptText)}");

                if (string.IsNullOrEmpty(scriptText))
                    return new OperationResult { Success = false, Message = $"repair_client.cmd 内嵌读取失败\n{log}" };

                File.WriteAllText(clientScript, scriptText);
                log.AppendLine($"clientScript.exists.afterWrite={File.Exists(clientScript)}");
            }
            catch (Exception ex)
            {
                log.AppendLine($"write.exception={ex}");
                return new OperationResult { Success = false, Message = $"写入 repair_client.cmd 到客户端目录失败\n{log}" };
            }
        }

        try
        {
            var process = Process.Start(new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = "/c repair_client.cmd",
                WorkingDirectory = clientPath,
                UseShellExecute = true
            });

            log.AppendLine($"process.started={(process is not null)}");
            if (process is null)
                return new OperationResult { Success = false, Message = $"命令未启动\n{log}" };

            return new OperationResult
            {
                Success = true,
                Message = (existedInClient
                    ? "已直接执行当前游戏目录中的 repair_client.cmd"
                    : "当前游戏目录没有 repair_client.cmd，已先写入再执行") + Environment.NewLine + log
            };
        }
        catch (Exception ex)
        {
            log.AppendLine($"start.exception={ex}");
            return new OperationResult { Success = false, Message = $"执行 repair_client.cmd 失败\n{log}" };
        }
    }
}
