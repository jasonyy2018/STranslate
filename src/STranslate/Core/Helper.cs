using CommunityToolkit.Mvvm.DependencyInjection;
using Microsoft.Extensions.Logging;
using STranslate.Plugin;
using System.IO;

namespace STranslate.Core;

public static class Helper
{
    private static readonly ILogger _logger = Ioc.Default.GetRequiredService<ILogger>();

    public static bool ShouldDeleteDirectory(string directory)
        => File.Exists(Path.Combine(directory, "NeedDelete.txt"));

    public static bool TryDeleteDirectory(string directory)
    {
        try
        {
            Directory.Delete(directory, true);
            return true;
        }
        catch (Exception e)
        {
            _logger.LogError($"无法删除目录 <{directory}>: {e.Message}");
            return false;
        }
    }

    public static void MoveDirectory(string source, string target)
    {
        // 检查根目录是否相同
        if (Path.GetPathRoot(source) is string path && path.Equals(Path.GetPathRoot(target), StringComparison.OrdinalIgnoreCase))
        {
            Directory.Move(source, target);
        }
        else
        {
            // 跨卷移动，需要复制然后删除
            CopyDirectory(source, target);
            Directory.Delete(source, true);
        }
    }

    private static void CopyDirectory(string sourceDir, string destinationDir)
    {
        // 获取源目录的信息
        var dir = new DirectoryInfo(sourceDir);

        if (!dir.Exists)
        {
            throw new DirectoryNotFoundException($"Source directory not found: {dir.FullName}");
        }

        // 创建目标目录
        Directory.CreateDirectory(destinationDir);

        // 复制文件
        foreach (FileInfo file in dir.GetFiles())
        {
            string targetFilePath = Path.Combine(destinationDir, file.Name);
            file.CopyTo(targetFilePath);
        }

        // 递归复制子目录
        foreach (DirectoryInfo subDir in dir.GetDirectories())
        {
            string newDestinationDir = Path.Combine(destinationDir, subDir.Name);
            CopyDirectory(subDir.FullName, newDestinationDir);
        }
    }

    public static string GetPluginDicrtoryName(PluginMetaData metaData)
        => metaData.IsPrePlugin ? metaData.AssemblyName : $"{metaData.AssemblyName}_{metaData.PluginID}";
}