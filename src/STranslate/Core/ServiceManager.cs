using Microsoft.Extensions.Logging;
using STranslate.Plugin;
using System.IO;

namespace STranslate.Core;

public class ServiceManager
{
    private readonly PluginManager _pluginManager;
    private readonly ServiceSettings _serviceSettings;
    private readonly ILogger<ServiceManager> _logger;
    private readonly List<Service> _services;

    public ServiceManager(PluginManager pluginManager, ServiceSettings serviceSettings, ILogger<ServiceManager> logger)
    {
        _pluginManager = pluginManager;
        _serviceSettings = serviceSettings;
        _logger = logger;
        _services = [];

        Directory.CreateDirectory(DataLocation.PluginSettingsDirectory);
    }
    public IEnumerable<Service> AllServices => _services;

    public void LoadServices()
    {
        // 遍历所有插件目录，删除服务配置目录和缓存目录
        var directories = new string[] { DataLocation.PluginSettingsDirectory, DataLocation.PluginCacheDirectory }
            .SelectMany(Directory.EnumerateDirectories);

        foreach (var directory in directories)
        {
            if (Helper.ShouldDeleteDirectory(directory))
            {
                Helper.TryDeleteDirectory(directory);
                continue;
            }
        }

        var svcJsonNames = new List<string>();

        var serviceDataCollections = new List<ServiceData>[]
        {
            _serviceSettings.TranSvcDatas,
            _serviceSettings.OcrSvcDatas,
            _serviceSettings.TtsSvcDatas,
            _serviceSettings.VocabularySvcDatas,
        };

        foreach (var metaData in _pluginManager.AllPluginMetaDatas)
        {
            var combineName = Helper.GetPluginDicrtoryName(metaData);
            var serviceSettingPath = Path.Combine(DataLocation.PluginSettingsDirectory, combineName);
            if (!Directory.Exists(serviceSettingPath))
                continue;

            /*
             * 当前方案: xxPlugin/35d9d684683245e680a5308c801ca2ad.json
             * 待定方案: xxPlugin/35d9d684683245e680a5308c801ca2ad/Settings.json
             * xxPlugin/35d9d684683245e680a5308c801ca2ad/Other.json
             */
            // 获取目录下所有json文件名
            var jsonFileNames = Directory
                .EnumerateFiles(serviceSettingPath, "*.json", SearchOption.TopDirectoryOnly)
                .Select(Path.GetFileNameWithoutExtension)
                .OfType<string>()
                .ToList();
            svcJsonNames.AddRange(jsonFileNames);

            foreach (var serviceDataCollection in serviceDataCollections)
            {
                /* 
                 * 只有同时存在配置文件(.json)和服务配置数据(ServiceData)的服务才会被加载
                 * 代码等同于 LINQ 查询:
                 * var result = from svc in _serviceSettings.TranSvcDatas
                 *              join fileName in jsonFileNames on svc.SvcID equals fileName
                 *              select svc;
                 */
                jsonFileNames
                    .Join(serviceDataCollection,
                            fileName => fileName,
                            serviceData => serviceData.SvcID,
                            (fileName, serviceData) => serviceData)
                    .ToList()
                    .ForEach(item =>
                    {
                        var service = CreateService(metaData, item);
                        service.Initialize();
                        _services.Add(service);
                    });

            }
        }
        //TODO: 如果ServiceSettings如果维护出错多出结果的话需要处理
        var svcSettingJsons = serviceDataCollections.SelectMany(item => item.Select(x => x.SvcID)).ToList();
        var lossSvcs = svcSettingJsons.Except(svcJsonNames);
        if (lossSvcs.Any())
        {
            _serviceSettings.TranSvcDatas.RemoveAll(s => lossSvcs.Contains(s.SvcID));
            _serviceSettings.OcrSvcDatas.RemoveAll(s => lossSvcs.Contains(s.SvcID));
            _serviceSettings.TtsSvcDatas.RemoveAll(s => lossSvcs.Contains(s.SvcID));
            _serviceSettings.VocabularySvcDatas.RemoveAll(s => lossSvcs.Contains(s.SvcID));
            _serviceSettings.Save();

            _logger.LogWarning($"发现丢失的服务配置，已自动移除: {string.Join(", ", lossSvcs)}");
        }

        if (!_serviceSettings.TranSvcDatas.Select(x => x.SvcID).Contains(_serviceSettings.ReplaceSvcID))
        {
            _serviceSettings.ReplaceSvcID = string.Empty;
            _serviceSettings.Save();

            _logger.LogInformation("替换服务已重置为空，因为之前设置的服务不存在。");
        }

        if (!_serviceSettings.TranSvcDatas.Select(x => x.SvcID).Contains(_serviceSettings.ImageTranslateSvcID))
        {
            _serviceSettings.ImageTranslateSvcID = string.Empty;
            _serviceSettings.Save();

            _logger.LogInformation("图片翻译服务已重置为空，因为之前设置的服务不存在。");
        }
    }

    /// <summary>
    /// 添加新的翻译服务实例
    /// </summary>
    /// <param name="metaData">插件元数据</param>
    /// <param name="type">服务类型</param>
    /// <returns>新创建的服务实例</returns>
    public Service AddService(PluginMetaData metaData, ServiceType type)
    {
        var service = CreateService(metaData);
        service.Initialize();
        _services.Add(service);

        var serviceDataCollection = GetServiceDataCollection(type);
        serviceDataCollection.Add(new ServiceData(service.ServiceID, service.DisplayName, service.IsEnabled));
        _serviceSettings.Save();
        return service;
    }

    /// <summary>
    /// 移除服务实例
    /// </summary>
    /// <param name="service">要移除的服务实例</param>
    /// <param name="type">服务类型</param>
    public void RemoveService(Service service, ServiceType type)
    {
        service.Dispose();
        _services.Remove(service);

        var serviceDataCollection = GetServiceDataCollection(type);
        var serviceData = serviceDataCollection.FirstOrDefault(s => s.SvcID == service.ServiceID);

        if (serviceData != null)
        {
            serviceDataCollection.Remove(serviceData);
        }
    }

    private List<ServiceData> GetServiceDataCollection(ServiceType type)
    {
        return type switch
        {
            ServiceType.Translation => _serviceSettings.TranSvcDatas,
            ServiceType.OCR => _serviceSettings.OcrSvcDatas,
            ServiceType.TTS => _serviceSettings.TtsSvcDatas,
            ServiceType.Vocabulary => _serviceSettings.VocabularySvcDatas,
            _ => throw new ArgumentOutOfRangeException(nameof(type), type, "不支持的服务类型")
        };
    }

    private Service CreateService(PluginMetaData metaData, ServiceData? settings = null)
    {
        var metaDataClone = metaData.Clone();
        var serviceID = settings?.SvcID ?? Guid.NewGuid().ToString("N");

        var service = new Service
        {
            ServiceID = serviceID,
            MetaData = metaDataClone,
            IsEnabled = settings?.IsEnabled ?? false,
            DisplayName = settings?.Name ?? metaDataClone.Name
        };

        var plugin = metaDataClone.CreatePluginInstance();
        var context = new PluginContext(metaDataClone, serviceID);
        service.Plugin = plugin;
        service.Context = context;

        return service;
    }
}