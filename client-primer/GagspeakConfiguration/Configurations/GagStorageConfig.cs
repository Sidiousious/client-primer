using GagSpeak.GagspeakConfiguration.Models;

namespace GagSpeak.GagspeakConfiguration.Configurations;

public class GagStorageConfig : IGagspeakConfiguration
{
    public GagStorage GagStorage { get; set; }
    public static int CurrentVersion => 1;
    public int Version { get; set; } = CurrentVersion;
}
