using GagSpeak.GagspeakConfiguration.Configurations;

namespace GagSpeak.GagspeakConfiguration;

// will probably change this in the future considering we use a different config storage approach in gagspeak
public class AlarmConfigService : ConfigurationServiceBase<AlarmConfig>
{
    public const string ConfigName = "alarms.json";
    public const bool PerCharacterConfig = true;

    public AlarmConfigService(string configDir) : base(configDir) { }

    protected override string ConfigurationName => ConfigName;
    protected override bool PerCharacterConfigPath => PerCharacterConfig;
}
