using GagSpeak.PlayerData.Handlers;
using GagSpeak.Services.Mediator;
using GagspeakAPI.Data;
using GagspeakAPI.Data.Character;
using GagSpeak.UpdateMonitoring;
using GagSpeak.WebAPI;
using GagSpeak.PlayerData.Data;
using GagSpeak.PlayerData.Pairs;
using GagspeakAPI.Enums;
using GagspeakAPI.Data.Permissions;

namespace GagSpeak.PlayerData.Pairs;

/// <summary>
/// Manages various Data Component Sending to Online Pairs.
/// </summary>
public class OnlinePairManager : DisposableMediatorSubscriberBase
{
    private readonly ApiController _apiController;
    private readonly OnFrameworkService _frameworkUtil;
    private readonly PlayerCharacterData _playerManager;
    private readonly PairManager _pairManager;

    // NewOnlinePairs
    private readonly HashSet<UserData> _newOnlinePairs = [];

    // Store the most recently sent component of our API formats from our player character
    private CharacterAppearanceData? _lastAppearanceData;
    private CharacterWardrobeData? _lastWardrobeData;
    private CharacterAliasData? _lastAliasData;
    private CharacterToyboxData? _lastToyboxData;
    private string _lastShockPermShareCode = string.Empty;

    public OnlinePairManager(ILogger<OnlinePairManager> logger,
        ApiController apiController, OnFrameworkService dalamudUtil,
        PlayerCharacterData playerCharacterManager,
        PairManager pairManager, GagspeakMediator mediator)
        : base(logger, mediator)
    {
        _apiController = apiController;
        _frameworkUtil = dalamudUtil;
        _playerManager = playerCharacterManager;
        _pairManager = pairManager;


        Mediator.Subscribe<DelayedFrameworkUpdateMessage>(this, (_) => FrameworkOnUpdate());

        // Subscriber to update our composite data after a safeword.
        Mediator.Subscribe<UpdateAllOnlineWithCompositeMessage>(this, (_) => PushCharacterCompositeData(_pairManager.GetOnlineUserDatas()));

        // Push Composite data to all online players when connected.
        Mediator.Subscribe<ConnectedMessage>(this, (_) => PushCharacterCompositeData(_pairManager.GetOnlineUserDatas()));
        // Push Composite data to any new pairs that go online.
        Mediator.Subscribe<PairWentOnlineMessage>(this, (msg) => _newOnlinePairs.Add(msg.UserData));

        // Fired whenever our Appearance data updates. We then send this data to all online pairs.
        Mediator.Subscribe<CharacterAppearanceDataCreatedMessage>(this, (msg) =>
        {
            var newAppearanceData = msg.CharacterAppearanceData;
            if (_lastAppearanceData == null || !Equals(newAppearanceData, _lastAppearanceData))
            {
                _lastAppearanceData = newAppearanceData;
                PushCharacterAppearanceData(_pairManager.GetOnlineUserDatas(), msg.UpdateKind);
            }
            else
            {
                Logger.LogDebug("Data was no different. Not sending data", LoggerType.OnlinePairs);
            }
        });

        // Fired whenever our Wardrobe data updates. We then send this data to all online pairs.
        Mediator.Subscribe<CharacterWardrobeDataCreatedMessage>(this, (msg) =>
        {
            var newWardrobeData = msg.CharacterWardrobeData;
            if (_lastWardrobeData == null || !Equals(newWardrobeData, _lastWardrobeData))
            {
                _lastWardrobeData = newWardrobeData;
                PushCharacterWardrobeData(_pairManager.GetOnlineUserDatas(), msg.UpdateKind);
            }
            else
            {
                Logger.LogDebug("Data was no different. Not sending data", LoggerType.OnlinePairs);
            }
        });

        // Fired whenever our Alias data updates. We then send this data to all online pairs.
        Mediator.Subscribe<CharacterAliasDataCreatedMessage>(this, (msg) =>
        {
            var newAliasData = msg.CharacterAliasData;
            if (_lastAliasData == null || !Equals(newAliasData, _lastAliasData))
            {
                _lastAliasData = newAliasData;
                PushCharacterAliasListData(msg.userData, msg.UpdateKind);
            }
            else
            {
                Logger.LogDebug("Data was no different. Not sending data", LoggerType.OnlinePairs);
            }
        });

        // Fired whenever our Toybox data updates. We then send this data to all online pairs.
        Mediator.Subscribe<CharacterToyboxDataCreatedMessage>(this, (msg) =>
        {
            var newToyboxData = msg.CharacterToyboxData;
            if (_lastToyboxData == null || !Equals(newToyboxData, _lastToyboxData))
            {
                _lastToyboxData = newToyboxData;
                PushCharacterToyboxData(_pairManager.GetOnlineUserDatas(), msg.UpdateKind);
            }
            else
            {
                Logger.LogDebug("Data was no different. Not sending data", LoggerType.OnlinePairs);
            }
        });

        // Fired whenever our Alias data updates. We then send this data to all online pairs.
        Mediator.Subscribe<CharacterPiShockPermDataCreatedMessage>(this, (msg) =>
        {
            var newShockPermShareCode = msg.ShareCode;
            if (_lastShockPermShareCode == null || !Equals(newShockPermShareCode, _lastShockPermShareCode))
            {
                _lastShockPermShareCode = newShockPermShareCode;
                PushCharacterPiShockPerms(new List<UserData>(){ msg.UserData }, msg.ShockPermsForPair, msg.UpdateKind);
            }
            else
            {
                Logger.LogDebug("PiShock Data was no different. Not sending data", LoggerType.OnlinePairs);
            }
        });

        Mediator.Subscribe<CharacterPiShockGlobalPermDataUpdatedMessage>(this, (msg) => 
            PushCharacterPiShockPerms(_pairManager.GetOnlineUserDatas(), msg.GlobalShockPermissions, msg.UpdateKind));
    }

    private void FrameworkOnUpdate()
    {
        // quit out if not connected or the new online pairs list is empty.
        if (!_apiController.IsConnected || !_newOnlinePairs.Any()) return;

        // Otherwise, copy the list, then clear it, and push our composite data to the users in that list.
        var newOnlinePairs = _newOnlinePairs.ToList();
        _newOnlinePairs.Clear();
        PushCharacterCompositeData(newOnlinePairs);
    }


    /// <summary> Pushes all our Player Data to all online pairs once connected. </summary>
    private void PushCharacterCompositeData(List<UserData> newOnlinePairs)
    {
        if (newOnlinePairs.Any())
        {
            // Send the data to all online players.
            _ = Task.Run(async () =>
            {
                CharacterCompositeData compiledDataToSend = await _playerManager.CompileCompositeDataToSend();
                Logger.LogDebug("new Online Pairs Identified, pushing latest Composite data", LoggerType.OnlinePairs);
                await _apiController.PushCharacterCompositeData(compiledDataToSend, newOnlinePairs).ConfigureAwait(false);
            });
        }
    }


    /// <summary> Pushes the character wardrobe data to the server for the visible players </summary>
    private void PushCharacterAppearanceData(List<UserData> onlinePlayers, DataUpdateKind updateKind)
    {
        if (_lastAppearanceData != null)
        {
            _ = Task.Run(async () =>
            {
                await _apiController.PushCharacterAppearanceData(_lastAppearanceData, onlinePlayers, updateKind).ConfigureAwait(false);
            });
        }
        else
        {
            Logger.LogWarning("No Appearance data to push to online players");
        }
    }

    /// <summary> Pushes the character wardrobe data to the server for the visible players </summary>
    private void PushCharacterWardrobeData(List<UserData> onlinePlayers, DataUpdateKind updateKind)
    {
        if (_lastWardrobeData != null)
        {
            _ = Task.Run(async () =>
            {
                await _apiController.PushCharacterWardrobeData(_lastWardrobeData, onlinePlayers, updateKind).ConfigureAwait(false);
            });
        }
        else
        {
            Logger.LogWarning("No Wardrobe data to push to online players");
        }
    }

    /// <summary> Pushes the character alias list to the respective pair we updated it for. </summary>
    private void PushCharacterAliasListData(UserData onlinePairToPushTo, DataUpdateKind updateKind)
    {
        if (_lastAliasData != null)
        {
            _ = Task.Run(async () =>
            {
                await _apiController.PushCharacterAliasListData(_lastAliasData, onlinePairToPushTo, DataUpdateKind.PuppeteerAliasListUpdated).ConfigureAwait(false);
            });
        }
        else
        {
            Logger.LogWarning("No Alias data to push to online players");
        }
    }

    /// <summary> Pushes the character toybox data to the server for the visible players </summary>
    private void PushCharacterToyboxData(List<UserData> onlinePlayers, DataUpdateKind updateKind)
    {
        if (_lastToyboxData != null)
        {
            _ = Task.Run(async () =>
            {
                await _apiController.PushCharacterToyboxData(_lastToyboxData, onlinePlayers, updateKind).ConfigureAwait(false);
            });
        }
        else
        {
            Logger.LogWarning("No Toybox data to push to online players");
        }
    }

    private void PushCharacterPiShockPerms(List<UserData> onlinePairToPushTo, PiShockPermissions perms, DataUpdateKind updateKind)
    {
        // Can be used for both global and individual updates.
        if (_lastShockPermShareCode != string.Empty)
        {
            _ = Task.Run(async () =>
            {
                await _apiController.PushCharacterPiShockData(perms, onlinePairToPushTo, updateKind).ConfigureAwait(false);
            });
        }
        else
        {
            Logger.LogWarning("No PiShock data to push to online players");
        }
    }
}
