using GagSpeak.PlayerData.Handlers;
using GagSpeak.Services.Mediator;
using GagspeakAPI.Data;
using GagspeakAPI.Data.Character;
using GagSpeak.UpdateMonitoring;
using GagSpeak.WebAPI;
using GagSpeak.PlayerData.Data;
using GagSpeak.PlayerData.Pairs;

namespace GagSpeak.PlayerData.Pairs;

/// <summary>
/// Manages various Data Component Sending to Online Pairs.
/// </summary>
public class OnlinePairManager : DisposableMediatorSubscriberBase
{
    private readonly ApiController _apiController;
    private readonly OnFrameworkService _frameworkUtil;
    private readonly PlayerCharacterManager _playerManager;
    private readonly PairManager _pairManager;

    // Store the most recently sent component of our API formats from our player character
    private CharacterAppearanceData? _lastAppearanceData;
    private CharacterWardrobeData? _lastWardrobeData;
    private CharacterAliasData? _lastAliasData;
    private CharacterToyboxData? _lastToyboxData;

    public OnlinePairManager(ILogger<OnlinePairManager> logger,
        ApiController apiController, OnFrameworkService dalamudUtil,
        PlayerCharacterManager playerCharacterManager,
        PairManager pairManager, GagspeakMediator mediator)
        : base(logger, mediator)
    {
        logger.LogWarning("Online Pair Manager Initializing");
        _apiController = apiController;
        _frameworkUtil = dalamudUtil;
        _playerManager = playerCharacterManager;
        _pairManager = pairManager;

        // Push Composite data to all online players when connected.
        Mediator.Subscribe<ConnectedMessage>(this, (_) => PushCharacterCompositeData(_pairManager.GetOnlineUserDatas()));

        // Fired whenever our Appearance data updates. We then send this data to all online pairs.
        Mediator.Subscribe<CharacterAppearanceDataCreatedMessage>(this, (msg) =>
        {
            var newAppearanceData = msg.CharacterAppearanceData;
            if (_lastAppearanceData == null || !Equals(newAppearanceData, _lastAppearanceData))
            {
                _lastAppearanceData = newAppearanceData;
                PushCharacterAppearanceData(_pairManager.GetOnlineUserDatas());
            }
            else
            {
                Logger.LogDebug("Data was no different. Not sending data");
            }
        });

        // Fired whenever our Wardrobe data updates. We then send this data to all online pairs.
        Mediator.Subscribe<CharacterWardrobeDataCreatedMessage>(this, (msg) =>
        {
            var newWardrobeData = msg.CharacterWardrobeData;
            if (_lastWardrobeData == null || !Equals(newWardrobeData, _lastWardrobeData))
            {
                _lastWardrobeData = newWardrobeData;
                PushCharacterWardrobeData(_pairManager.GetOnlineUserDatas());
            }
            else
            {
                Logger.LogDebug("Data was no different. Not sending data");
            }
        });

        // Fired whenever our Alias data updates. We then send this data to all online pairs.
        Mediator.Subscribe<CharacterAliasDataCreatedMessage>(this, (msg) =>
        {
            var newAliasData = msg.CharacterAliasData;
            if (_lastAliasData == null || !Equals(newAliasData, _lastAliasData))
            {
                _lastAliasData = newAliasData;
                PushCharacterAliasListData(msg.userData);
            }
            else
            {
                Logger.LogDebug("Data was no different. Not sending data");
            }
        });

        // Fired whenever our Toybox data updates. We then send this data to all online pairs.
        Mediator.Subscribe<CharacterToyboxDataCreatedMessage>(this, (msg) =>
        {
            var newToyboxData = msg.CharacterToyboxData;
            if (_lastToyboxData == null || !Equals(newToyboxData, _lastToyboxData))
            {
                _lastToyboxData = newToyboxData;
                PushCharacterToyboxData(_pairManager.GetOnlineUserDatas());
            }
            else
            {
                Logger.LogDebug("Data was no different. Not sending data");
            }
        });

        logger.LogWarning("Online Pair Manager Initialized");
    }

    /// <summary> Pushes all our Player Data to all online pairs once connected. </summary>
    private void PushCharacterCompositeData(List<UserData> onlinePlayers)
    {
        if (onlinePlayers.Any())
        {
            // TODO: Compile the composite data from the PlayerManager here!
            CharacterCompositeData compiledDataToSend = new CharacterCompositeData();

            // Send the data to all online players.
            _ = Task.Run(async () =>
            {
                Logger.LogInformation("Online pairs loaded, pushing Composite data to all online users");
                await _apiController.PushCharacterCompositeData(compiledDataToSend, onlinePlayers).ConfigureAwait(false);
            });
        }
    }


    /// <summary> Pushes the character wardrobe data to the server for the visible players </summary>
    private void PushCharacterAppearanceData(List<UserData> onlinePlayers)
    {
        if (onlinePlayers.Any() && _lastAppearanceData != null)
        {
            _ = Task.Run(async () =>
            {
                Logger.LogTrace("Pushing new Appearance data to all visible players"); // RECOMMENDATION: Toggle off when not debugging
                await _apiController.PushCharacterAppearanceData(_lastAppearanceData, onlinePlayers).ConfigureAwait(false);
            });
        }
    }

    /// <summary> Pushes the character wardrobe data to the server for the visible players </summary>
    private void PushCharacterWardrobeData(List<UserData> onlinePlayers)
    {
        if (onlinePlayers.Any() && _lastWardrobeData != null)
        {
            _ = Task.Run(async () =>
            {
                Logger.LogTrace("Pushing new Wardrobe data to all visible players"); // RECOMMENDATION: Toggle off when not debugging
                await _apiController.PushCharacterWardrobeData(_lastWardrobeData, onlinePlayers).ConfigureAwait(false);
            });
        }
    }

    /// <summary> Pushes the character alias list to the respective pair we updated it for. </summary>
    private void PushCharacterAliasListData(UserData onlinePairToPushTo)
    {
        if (_lastAliasData != null)
        {
            _ = Task.Run(async () =>
            {
                Logger.LogTrace("Pushing new Alias data to visible player"); // RECOMMENDATION: Toggle off when not debugging
                await _apiController.PushCharacterAliasListData(_lastAliasData, onlinePairToPushTo).ConfigureAwait(false);
            });
        }
    }

    /// <summary> Pushes the character toybox data to the server for the visible players </summary>
    private void PushCharacterToyboxData(List<UserData> onlinePlayers)
    {
        if (onlinePlayers.Any() && _lastToyboxData != null)
        {
            _ = Task.Run(async () =>
            {
                Logger.LogTrace("Pushing new Toybox data to all visible players"); // RECOMMENDATION: Toggle off when not debugging
                await _apiController.PushCharacterToyboxInfoData(_lastToyboxData, onlinePlayers).ConfigureAwait(false);
            });
        }
    }
}
