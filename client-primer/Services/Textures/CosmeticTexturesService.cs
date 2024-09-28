using Dalamud.Interface.Textures;
using Dalamud.Interface.Textures.TextureWraps;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using GagSpeak.Services.Mediator;
using GagSpeak.UpdateMonitoring;

namespace GagSpeak.Services.Textures;

// Friendly Reminded, this is a scoped service, and IDalamudTextureWraps will only return values on the framework thread.
// Attempting to use or access this class to obtain information outside the framework draw update thread will result in a null return.
public class CosmeticTextureService : DisposableMediatorSubscriberBase
{
    private readonly OnFrameworkService _frameworkUtils;
    private readonly ITextureProvider _textures;
    private readonly IDalamudPluginInterface _pi;

    // This is shared across all states of our plugin, so should attach to the one in UISharedService
    private ISharedImmediateTexture _sharedTextures;

    public CosmeticTextureService(ILogger<CosmeticTextureService> logger, GagspeakMediator mediator,
        OnFrameworkService frameworkUtils, IDalamudPluginInterface pi, ITextureProvider tp) 
        : base(logger, mediator)
    {
        _frameworkUtils = frameworkUtils;
        _textures = tp;
        _pi = pi;

        Logger.LogInformation("GagSpeak Profile Cosmetic Cache Initializing.");

        // fire an async task to occur on the framework thread that will fetch and load in our image data.
        Task.Run(async () => await LoadAllCosmetics());
    }

    // we need to store a local static cache of our image data so
    // that they can load instantly whenever required.
    public Dictionary<string, IDalamudTextureWrap?> InternalCosmeticCache = [];



    // MUST ensure ALL images are disposed of or else we will leak a very large amount of memory.
    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        foreach (var texture in InternalCosmeticCache.Values)
        {
            texture?.Dispose();
            // if we run into issues with this not going to null, a null should have been here.
        }
        // clear the dictionary, erasing all disposed textures.
        InternalCosmeticCache.Clear();
    }

    public async Task LoadAllCosmetics()
    {
        await _frameworkUtils.RunOnFrameworkThread(() =>
        {
            // load in all the images to the dictionary by iterating through all public const strings stored in the cosmetic labels and appending them as new texture wraps that should be stored into the cache.
            foreach (var label in CosmeticLabels.Labels)
            {
                var key = label.Key;
                var path = label.Value;
                Logger.LogInformation("Cosmetic Key: " + key);

                if (string.IsNullOrEmpty(path)) continue;

                Logger.LogInformation("Renting image to store in Cache: " + key);
                var texture = RentImageFromFile(path);
                if (texture != null)
                {
                    Logger.LogInformation("Cosmetic Key: " + key + " Texture Loaded Successfully: " + path);
                    InternalCosmeticCache[key] = texture;
                }
            }
            // Corby Note: If this is too much to handle in a single thread,
            // see if there is a way to batch send requests that can be returned overtime when retrieved.
            Logger.LogInformation("GagSpeak Profile Cosmetic Cache Fetched all Image Data!");
        });
    }

    public bool isImageValid(string keyName)
    {
        var texture = _sharedTextures.GetWrapOrDefault(InternalCosmeticCache[keyName]);
        if(texture == null) return false;
        return true;
    }


    // Rent the file async. Note that this MUST be done on the framework thread.
    public IDalamudTextureWrap? RentImageFromFile(string path)
    {
        // grab the file and load it into the sharedTextures State
        _sharedTextures = _textures.GetFromFile(Path.Combine(_pi.AssemblyLocation.DirectoryName!, "Assets", path));

        // if the wrap is not successful, return null.
        if (_sharedTextures.GetWrapOrDefault() == null) return null;

        // if it is successful, grab the texture from the shared Service via a RentAsync.

        // NOTE: Calling this Creates a new instance of the Texture fetched from the _sharedTextures.
        //       This texture is then guaranteed to be available until IDispose is called.
        else return _sharedTextures.RentAsync().Result;
    }

}


public static class CosmeticLabels
{
    public static readonly Dictionary<string, string> Labels = new Dictionary<string, string>
    {
        { "DummyTest", "RequiredImages\\icon256bg.png" }, // Dummy File
    };

    public static readonly string[] CosmeticFileLocations =
    {
        "RequiredImages\\icon256bg.png", // Dummy File.
    };
}
