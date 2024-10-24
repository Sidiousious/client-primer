using GagSpeak.GagspeakConfiguration.Models;
using GagSpeak.Services.ConfigurationServices;
using GagSpeak.Services.Mediator;
using GagSpeak.Toybox.Controllers;
using GagSpeak.Toybox.SimulatedVibe;
using GagSpeak.Utils;
using GagSpeak.WebAPI;
using GagspeakAPI.Data;
using GagspeakAPI.Enums;

namespace GagSpeak.Toybox.Services;
// handles the management of the connected devices or simulated vibrator.
public class ToyboxVibeService : DisposableMediatorSubscriberBase
{
    private readonly ClientConfigurationManager _clientConfigs;
    private readonly DeviceController _deviceHandler; // handles the actual connected devices.
    private readonly VibeSimAudio _vibeSimAudio; // handles the simulated vibrator
    private readonly PiShockProvider _piShockProvider;

    public ToyboxVibeService(ILogger<ToyboxVibeService> logger,
        GagspeakMediator mediator, ClientConfigurationManager clientConfigs,
        DeviceController deviceHandler, VibeSimAudio vibeSimAudio,
        PiShockProvider piShockProvider) : base(logger, mediator)
    {
        _clientConfigs = clientConfigs;
        _deviceHandler = deviceHandler;
        _vibeSimAudio = vibeSimAudio;
        _piShockProvider = piShockProvider;

        if (UsingSimulatedVibe)
        {
            // play it
            _vibeSimAudio.Play();
        }

        Mediator.Subscribe<ConnectedMessage>(this, _ =>
        {
            if (_clientConfigs.GagspeakConfig.IntifaceAutoConnect && !_deviceHandler.ConnectedToIntiface)
            {
                if (IntifaceHelper.AppPath == string.Empty)
                {
                    IntifaceHelper.GetApplicationPath();
                }
                IntifaceHelper.OpenIntiface(logger, false);
                _deviceHandler.ConnectToIntifaceAsync();
            }
        });
    }

    // public accessors here.
    public VibratorMode CurrentVibratorModeUsed => _clientConfigs.GagspeakConfig.VibratorMode;
    public bool UsingSimulatedVibe => CurrentVibratorModeUsed == VibratorMode.Simulated;
    public bool UsingRealVibe => CurrentVibratorModeUsed == VibratorMode.Actual;
    public bool ConnectedToyActive => (CurrentVibratorModeUsed == VibratorMode.Actual) ? _deviceHandler.ConnectedToIntiface && _deviceHandler.AnyDeviceConnected : VibeSimAudioPlaying;
    public bool IntifaceConnected => _deviceHandler.ConnectedToIntiface;
    public bool ScanningForDevices => _deviceHandler.ScanningForDevices;


    public bool VibeSimAudioPlaying { get; private set; } = false;
    public float VibeSimVolume { get; private set; } = 0.0f;
    public string ActiveSimPlaybackDevice => _vibeSimAudio.PlaybackDevices[_vibeSimAudio.ActivePlaybackDeviceId];
    public List<string> PlaybackDevices => _vibeSimAudio.PlaybackDevices;


    // Grab device handler via toyboxvibeService.
    public DeviceController DeviceHandler => _deviceHandler;
    public VibeSimAudio VibeSimAudio => _vibeSimAudio;

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        // stop the sound player if it is playing
        if (_vibeSimAudio.isPlaying)
        {
            _vibeSimAudio.Stop();
        }
        _vibeSimAudio.Dispose();
    }

    public void ExecuteShockAction(string shareCode, ShockTriggerAction shockAction)
    {
        _piShockProvider.ExecuteOperation(shareCode, (int)shockAction.OpCode, shockAction.Intensity, shockAction.Duration);
    }

    public void UpdateVibeSimAudioType(VibeSimType newType)
    {
        _clientConfigs.GagspeakConfig.VibeSimAudio = newType;
        _clientConfigs.Save();

        if (newType == VibeSimType.Normal)
        {
            _vibeSimAudio.ChangeAudioPath("vibrator.wav");
        }
        else if (newType == VibeSimType.Quiet)
        {
            _vibeSimAudio.ChangeAudioPath("vibratorQuiet.wav");
        }
        _vibeSimAudio.SetVolume(VibeSimVolume);
    }

    public void SwitchPlaybackDevice(int deviceId)
    {
        _vibeSimAudio.SwitchDevice(deviceId);
    }


    public void StartActiveVibes()
    {
        // start the vibe based on the type used for the vibe.
        if (UsingRealVibe)
        {
            // do something?
        }
        else if (UsingSimulatedVibe)
        {
            VibeSimAudioPlaying = true;
            _vibeSimAudio.Play();
        }
    }


    public void StopActiveVibes()
    {
        // stop the vibe based on the type used for the vibe.
        if (UsingRealVibe)
        {
            _deviceHandler.StopAllDevices();
        }
        else if (UsingSimulatedVibe)
        {
            VibeSimAudioPlaying = false;
            _vibeSimAudio.Stop();
        }
    }

    public void SendNextIntensity(byte intensity)
    {
        if (ConnectedToyActive)
        {
            if (UsingRealVibe)
            {
                DeviceHandler.SendVibeToAllDevices(intensity);
            }
            else if (UsingSimulatedVibe)
            {
                _vibeSimAudio.SetVolume(intensity / 100f);
            }
        }
    }
}





