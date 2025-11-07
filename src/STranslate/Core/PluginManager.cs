using CommunityToolkit.Mvvm.DependencyInjection;
using Microsoft.Extensions.Logging;
using STranslate.Plugin;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Text.Json;

namespace STranslate.Core;

public class PluginManager
{
    private readonly ILogger<PluginManager> _logger;
    private readonly List<PluginMetaData> _pluginMetaDatas = [];
    private readonly string _tempExtractPath;

    public PluginManager(ILogger<PluginManager> logger)
    {
        _logger = logger;
        _tempExtractPath = Path.Combine(Path.GetTempPath(), Constant.TmpPluginFolderName);
        InitializeDirectories();
    }

    public IEnumerable<PluginMetaData> AllPluginMetaDatas => _pluginMetaDatas;

    public IEnumerable<PluginMetaData> GetPluginMetaDatas<T>() where T : IPlugin
        => _pluginMetaDatas.Where(d => d.PluginType != null && typeof(T).IsAssignableFrom(d.PluginType));

    public void LoadPlugins()
    {
        var results = LoadPluginMetaDatasFromDirectories(DataLocation.PluginDirectories);

        foreach (var result in results.Where(r => r.IsSuccess && r.PluginMetaData != null))
            _pluginMetaDatas.Add(result.PluginMetaData!);

        LogPluginLoadResults(results);
    }

    public (string Error, PluginMetaData? MetaData) InstallPlugin(string spkgFilePath)
    {
        var validationError = ValidatePluginFile(spkgFilePath);
        if (validationError != null) return (validationError, null);

        try
        {
            var pluginName = Path.GetFileNameWithoutExtension(spkgFilePath);
            var extractPath = Path.Combine(_tempExtractPath, pluginName);

            CleanDirectory(extractPath);
            ExtractPlugin(spkgFilePath, extractPath);

            var metaData = GetPluginMeta(extractPath);
            if (metaData?.PluginID == null)
                return ($"Invalid plugin structure: {JsonSerializer.Serialize(metaData)}", null);

            if (AllPluginMetaDatas.FirstOrDefault(x => x.PluginID == metaData.PluginID) is { } existing)
                return ($"插件已存在: {metaData.Name} v{existing.Version}，请先卸载旧版本再安装新版本。", null);

            var pluginPath = MoveToPluginPath(extractPath, metaData.PluginID);
            var result = LoadPluginMetaDataFromDirectory(pluginPath);

            if (!result.IsSuccess || result.PluginMetaData == null)
                return ($"Failed to load plugin: {result.ErrorMessage}", null);

            _pluginMetaDatas.Add(result.PluginMetaData);
            Ioc.Default.GetRequiredService<IInternationalization>().LoadInstalledPluginLanguages(pluginPath);

            return ("", result.PluginMetaData);
        }
        catch (Exception ex)
        {
            return ($"Unexpected error: {ex.Message}", null);
        }
    }

    public bool UninstallPlugin(PluginMetaData metaData)
    {
        var combineName = Helper.GetPluginDicrtoryName(metaData);

        MarkForDeletion(metaData.PluginDirectory);
        MarkForDeletion(Path.Combine(DataLocation.PluginSettingsDirectory, combineName));
        MarkForDeletion(Path.Combine(DataLocation.PluginCacheDirectory, combineName));

        _pluginMetaDatas.Remove(metaData);
        return true;
    }

    public void CleanupTempFiles()
    {
        try
        {
            if (Directory.Exists(_tempExtractPath))
                Directory.Delete(_tempExtractPath, true);
        }
        catch (Exception ex)
        {
            _logger.LogError($"Failed to cleanup temp files: {ex.Message}");
        }
    }

    #region Private Methods

    private void InitializeDirectories()
    {
        Directory.CreateDirectory(Constant.PreinstalledDirectory);
        Directory.CreateDirectory(DataLocation.PluginsDirectory);
        Directory.CreateDirectory(DataLocation.PluginCacheDirectory);
        Directory.CreateDirectory(_tempExtractPath);
    }

    private string? ValidatePluginFile(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            return "Plugin path cannot be null or empty.";

        if (!File.Exists(filePath))
            return $"Plugin file does not exist: {filePath}";

        if (Path.GetExtension(filePath).ToLower() != Constant.PluginFileExtension)
            return $"Unsupported plugin file type. Expected {Constant.PluginFileExtension}";

        return null;
    }

    private void CleanDirectory(string path)
    {
        if (Directory.Exists(path))
            Directory.Delete(path, true);
    }

    private void ExtractPlugin(string spkgFilePath, string extractPath)
    {
        try
        {
            ZipFile.ExtractToDirectory(spkgFilePath, extractPath);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to extract SPKG file: {ex.Message}", ex);
        }
    }

    private void MarkForDeletion(string directoryPath)
    {
        if (Directory.Exists(directoryPath))
            File.Create(Path.Combine(directoryPath, "NeedDelete.txt")).Dispose();
    }

    private PluginLoadResult LoadPluginMetaDataFromDirectory(string pluginDirectory)
    {
        var metaData = GetPluginMeta(pluginDirectory);
        if (metaData == null)
            return PluginLoadResult.Fail("Failed to load plugin metadata", Path.GetFileName(pluginDirectory));

        return LoadPluginPairFromMetaData(metaData);
    }

    private List<PluginLoadResult> LoadPluginMetaDatasFromDirectories(params string[] pluginDirectories)
    {
        var allPluginMetaDatas = GetAllPluginMetaData(pluginDirectories);
        var (uniqueList, duplicateList) = GetUniqueLatestPluginMeta(allPluginMetaDatas);

        if (duplicateList.Count > 0)
            LogDuplicatePlugins(duplicateList);

        return uniqueList.Select(LoadPluginPairFromMetaData).ToList();
    }

    private PluginLoadResult LoadPluginPairFromMetaData(PluginMetaData metaData)
    {
        try
        {
            var assemblyLoader = new PluginAssemblyLoader(metaData.ExecuteFilePath);
            var assembly = assemblyLoader.LoadAssemblyAndDependencies();

            if (assembly == null)
                return PluginLoadResult.Fail("Assembly loading failed", metaData.Name);

            var type = assemblyLoader.FromAssemblyGetTypeOfInterface(assembly, typeof(IPlugin));
            if (type == null)
                return PluginLoadResult.Fail("IPlugin interface not found", metaData.Name);

            var assemblyName = assembly.GetName().Name;
            if (assemblyName == null)
                return PluginLoadResult.Fail("Assembly name is null", metaData.Name);

            metaData.AssemblyName = assemblyName;
            metaData.PluginType = type;
            UpdateDirectories(metaData);

            _logger.LogInformation($"插件加载成功: {metaData.Name}");
            return PluginLoadResult.Success(metaData);
        }
        catch (FileNotFoundException ex)
        {
            return PluginLoadResult.Fail($"Plugin file not found: {ex.FileName}", metaData.Name, ex);
        }
        catch (ReflectionTypeLoadException ex)
        {
            var errors = string.Join("; ", ex.LoaderExceptions.Select(e => e?.Message));
            return PluginLoadResult.Fail($"Type loading failed: {errors}", metaData.Name, ex);
        }
        catch (Exception ex)
        {
            return PluginLoadResult.Fail($"Plugin loading error: {ex.Message}", metaData.Name, ex);
        }
    }

    private List<PluginMetaData> GetAllPluginMetaData(string[] pluginDirectories)
    {
        return pluginDirectories
            .SelectMany(Directory.EnumerateDirectories)
            .Where(dir => !Helper.ShouldDeleteDirectory(dir) || !Helper.TryDeleteDirectory(dir))
            .Select(GetPluginMeta)
            .Where(metadata => metadata != null)
            .Cast<PluginMetaData>()
            .ToList();
    }

    private PluginMetaData? GetPluginMeta(string pluginDirectory)
    {
        var configPath = Path.Combine(pluginDirectory, Constant.PluginMetaFileName);

        if (!Directory.Exists(pluginDirectory) || !File.Exists(configPath))
            return null;

        try
        {
            var content = File.ReadAllText(configPath);
            var metaData = JsonSerializer.Deserialize<PluginMetaData>(content);

            if (metaData == null || !File.Exists(metaData.ExecuteFilePath))
                return null;

            metaData.PluginDirectory = pluginDirectory;
            metaData.IsPrePlugin = pluginDirectory.Contains(Constant.PreinstalledDirectory);

            return metaData;
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error reading plugin config {configPath}: {ex.Message}");
            return null;
        }
    }

    private void UpdateDirectories(PluginMetaData metaData)
    {
        var combineName = Helper.GetPluginDicrtoryName(metaData);
        metaData.PluginSettingsDirectoryPath = Path.Combine(DataLocation.PluginSettingsDirectory, combineName);
        metaData.PluginCacheDirectoryPath = Path.Combine(DataLocation.PluginCacheDirectory, combineName);
    }

    private (List<PluginMetaData> UniqueList, List<PluginMetaData> DuplicateList) GetUniqueLatestPluginMeta(
        List<PluginMetaData> allPluginMetaDatas)
    {
        var uniqueList = new List<PluginMetaData>();
        var duplicateList = new List<PluginMetaData>();

        foreach (var group in allPluginMetaDatas.GroupBy(x => x.PluginID))
        {
            var sorted = group.OrderByDescending(x => x.Version).ToList();
            uniqueList.Add(sorted.First());
            if (sorted.Count > 1)
                duplicateList.AddRange(sorted.Skip(1));
        }

        return (uniqueList, duplicateList);
    }

    private string MoveToPluginPath(string extractPath, string pluginID)
    {
        var pluginName = Path.GetFileName(extractPath)
            ?? throw new InvalidOperationException("Cannot determine plugin name");

        var targetPath = Constant.PrePluginIDs.Contains(pluginID)
            ? Path.Combine(Constant.PreinstalledDirectory, pluginName)
            : Path.Combine(DataLocation.PluginsDirectory, $"{pluginName}_{pluginID}");

        Helper.MoveDirectory(extractPath, targetPath);
        return targetPath;
    }

    private void LogDuplicatePlugins(List<PluginMetaData> duplicateList)
    {
        _logger.LogWarning($"发现 {duplicateList.Count} 个重复插件，将跳过加载:");

        foreach (var dup in duplicateList)
        {
            var info = $"{dup.Name} v{dup.Version} (ID: {dup.PluginID}) | 类型: {(dup.IsPrePlugin ? "预装插件" : "用户插件")}";
            if (!string.IsNullOrEmpty(dup.Author)) info += $" | 作者: {dup.Author}";
            _logger.LogWarning($"  ↳ 跳过重复插件: {info}");
        }
    }

    private void LogPluginLoadResults(List<PluginLoadResult> results)
    {
        var successful = results.Count(r => r.IsSuccess);
        var failed = results.Count - successful;

        _logger.LogInformation($"插件加载完成: 总计 {results.Count} 个，成功 {successful} 个，失败 {failed} 个");

        foreach (var result in results.Where(r => r.IsSuccess && r.PluginMetaData != null))
        {
            var m = result.PluginMetaData!;
            var info = $"{m.Name} v{m.Version} (ID: {m.PluginID}) | {(m.IsPrePlugin ? "预装" : "用户")}插件";
            if (!string.IsNullOrEmpty(m.Author)) info += $" | {m.Author}";
            _logger.LogInformation($"  ✓ {info}");
        }

        foreach (var result in results.Where(r => !r.IsSuccess))
        {
            _logger.LogError($"  ✗ {result.PluginName ?? "未知"}: {result.ErrorMessage}");
            if (result.Exception?.InnerException != null)
                _logger.LogError($"    ↳ {result.Exception.InnerException.Message}");
        }
    }

    #endregion
}

public class PluginLoadResult
{
    public bool IsSuccess { get; init; }
    public string? ErrorMessage { get; init; }
    public Exception? Exception { get; init; }
    public PluginMetaData? PluginMetaData { get; init; }
    public string? PluginName { get; init; }

    public static PluginLoadResult Success(PluginMetaData metaData) => new()
    {
        IsSuccess = true,
        PluginMetaData = metaData,
        PluginName = metaData.Name
    };

    public static PluginLoadResult Fail(string message, string? pluginName = null, Exception? ex = null) => new()
    {
        IsSuccess = false,
        ErrorMessage = message,
        PluginName = pluginName,
        Exception = ex
    };
}