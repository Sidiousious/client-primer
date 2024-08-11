using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using GagSpeak.ChatMessages;
using GagSpeak.GagspeakConfiguration;
using GagSpeak.GagspeakConfiguration.Configurations;
using GagSpeak.GagspeakConfiguration.Models;
using GagSpeak.Services.Mediator;
using GagSpeak.UpdateMonitoring;
using GagSpeak.Utils;
using GagspeakAPI.Data;
using GagspeakAPI.Data.Character;
using GagspeakAPI.Data.Enum;
using GagspeakAPI.Dto.Connection;
using ImGuiNET;
using Microsoft.IdentityModel.Tokens;
using Penumbra.GameData.Enums;
using Penumbra.GameData.Structs;
using ProjectGagspeakAPI.Data.VibeServer;
using static GagspeakAPI.Data.Enum.GagList;

namespace GagSpeak.Services.ConfigurationServices;

/// <summary>
/// This configuration manager helps manage the various interactions with all config files related to server-end activity.
/// <para> It provides a comprehensive interface for configuring servers, managing tags and nicknames, and handling authentication keys. </para>
/// </summary>
public class ClientConfigurationManager : DisposableMediatorSubscriberBase
{
    private readonly OnFrameworkService _frameworkUtils;            // a utilities class with methods that work with the Dalamud framework
    private readonly GagspeakConfigService _configService;          // the primary gagspeak config service.
    private readonly GagStorageConfigService _gagStorageConfig;     // the config for the gag storage service (toybox gag storage)
    private readonly WardrobeConfigService _wardrobeConfig;         // the config for the wardrobe service (restraint sets)
    private readonly AliasConfigService _aliasConfig;               // the config for the alias lists (puppeteer stuff)
    private readonly PatternConfigService _patternConfig;           // the config for the pattern service (toybox pattern storage))
    private readonly AlarmConfigService _alarmConfig;               // the config for the alarm service (toybox alarm storage)
    private readonly TriggerConfigService _triggersConfig;          // the config for the triggers service (toybox triggers storage)

    public ClientConfigurationManager(ILogger<ClientConfigurationManager> logger,
        GagspeakMediator GagspeakMediator, OnFrameworkService onFrameworkService,
        GagspeakConfigService configService, GagStorageConfigService gagStorageConfig,
        WardrobeConfigService wardrobeConfig, AliasConfigService aliasConfig,
        PatternConfigService patternConfig, AlarmConfigService alarmConfig,
        TriggerConfigService triggersConfig) : base(logger, GagspeakMediator)
    {
        _frameworkUtils = onFrameworkService;
        _configService = configService;
        _gagStorageConfig = gagStorageConfig;
        _wardrobeConfig = wardrobeConfig;
        _aliasConfig = aliasConfig;
        _patternConfig = patternConfig;
        _alarmConfig = alarmConfig;
        _triggersConfig = triggersConfig;

        InitConfigs();

        // Subscribe to the connected message update so we know when to update our global permissions
        Mediator.Subscribe<ConnectedMessage>(this, (msg) =>
        {
            // update our configs to point to the new user.
            if (msg.Connection.User.UID != _configService.Current.LastUidLoggedIn)
            {
                UpdateConfigs(msg.Connection.User.UID);
            }
            // update the last logged in UID
            _configService.Current.LastUidLoggedIn = msg.Connection.User.UID;
            Save();

            // make sure bratty subs dont use disconnect to think they can get free.
            SyncDataWithConnectionDto(msg.Connection);
        });
    }

    // define public access to various storages (THESE ARE ONLY GETTERS, NO SETTERS)
    public GagspeakConfig GagspeakConfig => _configService.Current; // UNIVERSAL
    public GagStorageConfig GagStorageConfig => _gagStorageConfig.Current; // PER PLAYER
    private WardrobeConfig WardrobeConfig => _wardrobeConfig.Current; // PER PLAYER
    private AliasConfig AliasConfig => _aliasConfig.Current; // PER PLAYER
    private PatternConfig PatternConfig => _patternConfig.Current; // PER PLAYER
    private AlarmConfig AlarmConfig => _alarmConfig.Current; // PER PLAYER
    private TriggerConfig TriggerConfig => _triggersConfig.Current; // PER PLAYER

    public void UpdateConfigs(string loggedInPlayerUID)
    {
        _gagStorageConfig.UpdateUid(loggedInPlayerUID);
        _wardrobeConfig.UpdateUid(loggedInPlayerUID);
        _aliasConfig.UpdateUid(loggedInPlayerUID);
        _triggersConfig.UpdateUid(loggedInPlayerUID);
        _alarmConfig.UpdateUid(loggedInPlayerUID);

        InitConfigs();
    }

    public bool HasCreatedConfigs()
        => (GagspeakConfig != null && GagStorageConfig != null && WardrobeConfig != null && AliasConfig != null
          && PatternConfig != null && AlarmConfig != null && TriggerConfig != null);

    /// <summary> Saves the GagspeakConfig. </summary>
    public void Save()
    {
        var caller = new StackTrace().GetFrame(1)?.GetMethod()?.ReflectedType?.Name ?? "Unknown";
        Logger.LogDebug("{caller} Calling config save", caller);
        _configService.Save();
    }

    public void InitConfigs()
    {
        if (_configService.Current.ChannelsGagSpeak.Count == 0)
        {
            Logger.LogWarning("Channel list is empty, adding Say as the default channel.");
            _configService.Current.ChannelsGagSpeak = new List<ChatChannel.ChatChannels> { ChatChannel.ChatChannels.Say };
        }
        if (_configService.Current.ChannelsPuppeteer.Count == 0)
        {
            Logger.LogWarning("Channel list is empty, adding Say as the default channel.");
            _configService.Current.ChannelsPuppeteer = new List<ChatChannel.ChatChannels> { ChatChannel.ChatChannels.Say };
        }

        // insure the nicknames and tag configs exist in the main server.
        if (_gagStorageConfig.Current.GagStorage == null) { _gagStorageConfig.Current.GagStorage = new(); }
        // create a new storage file
        if (_gagStorageConfig.Current.GagStorage.GagEquipData.IsNullOrEmpty())
        {
            Logger.LogWarning("Gag Storage Config is empty, creating a new one.");
            try
            {
                _gagStorageConfig.Current.GagStorage.GagEquipData = Enum.GetValues(typeof(GagList.GagType))
                    .Cast<GagList.GagType>().ToDictionary(gagType => gagType, gagType => new GagDrawData(ItemIdVars.NothingItem(EquipSlot.Head)));
                // print the keys in the dictionary
                Logger.LogInformation("Gag Storage Config Created with {count} keys", _gagStorageConfig.Current.GagStorage.GagEquipData.Count);
                _gagStorageConfig.Save();
            }
            catch (Exception e)
            {
                Logger.LogError(e, "Failed to create Gag Storage Config");
            }
        }
        if (_wardrobeConfig.Current.WardrobeStorage == null) { _wardrobeConfig.Current.WardrobeStorage = new(); }
        if (_aliasConfig.Current.AliasStorage == null) { _aliasConfig.Current.AliasStorage = new(); }
        if (_patternConfig.Current.PatternStorage == null) { _patternConfig.Current.PatternStorage = new(); }
        if (_alarmConfig.Current.AlarmStorage == null) { _alarmConfig.Current.AlarmStorage = new(); }
        if (_triggersConfig.Current.TriggerStorage == null) { _triggersConfig.Current.TriggerStorage = new(); }
    }

    #region ConnectionDto Update Methods
    private void SyncDataWithConnectionDto(ConnectionDto dto)
    {
        // TODO: Update this after we turn the activestate into an object from its raw values.
        string assigner = (dto.WardrobeActiveSetAssigner == string.Empty) ? "SelfApplied" : dto.WardrobeActiveSetAssigner;
        // if the active set is not string.Empty, we should update our active sets.
        if (dto.WardrobeActiveSetName != string.Empty)
        {
            SetRestraintSetState(GetRestraintSetIdxByName(dto.WardrobeActiveSetName), assigner, UpdatedNewState.Enabled, false);
        }

        // if the set was locked, we should lock it with the appropriate time.
        if (dto.WardrobeActiveSetLocked)
        {
            LockRestraintSet(GetRestraintSetIdxByName(dto.WardrobeActiveSetName), assigner, dto.WardrobeActiveSetLockTime, false);
        }
    }


    #endregion ConnectionDto Update Methods

    /* --------------------- Gag Storage Config Methods --------------------- */
    #region Gag Storage Methods
    internal bool IsGagEnabled(GagType gagType) => _gagStorageConfig.Current.GagStorage.GagEquipData.FirstOrDefault(x => x.Key == gagType).Value.IsEnabled;
    internal GagDrawData GetDrawData(GagType gagType) => _gagStorageConfig.Current.GagStorage.GagEquipData[gagType];
    internal EquipSlot GetGagTypeEquipSlot(GagType gagType) => _gagStorageConfig.Current.GagStorage.GagEquipData.FirstOrDefault(x => x.Key == gagType).Value.Slot;
    internal EquipItem GetGagTypeEquipItem(GagType gagType) => _gagStorageConfig.Current.GagStorage.GagEquipData.FirstOrDefault(x => x.Key == gagType).Value.GameItem;
    internal StainIds GetGagTypeStain(GagType gagType) => _gagStorageConfig.Current.GagStorage.GagEquipData.FirstOrDefault(x => x.Key == gagType).Value.GameStain;
    internal IReadOnlyList<byte> GetGagTypeStainIds(GagType gagType)
    {
        var GameStains = _gagStorageConfig.Current.GagStorage.GagEquipData.FirstOrDefault(x => x.Key == gagType).Value.GameStain;
        return [GameStains.Stain1.Id, GameStains.Stain2.Id];
    }
    internal int GetGagTypeSlotId(GagType gagType) => _gagStorageConfig.Current.GagStorage.GagEquipData.FirstOrDefault(x => x.Key == gagType).Value.ActiveSlotId;

    internal void SetGagEnabled(GagType gagType, bool isEnabled)
    {
        _gagStorageConfig.Current.GagStorage.GagEquipData.FirstOrDefault(x => x.Key == gagType).Value.IsEnabled = isEnabled;
        _gagStorageConfig.Save();
    }

    internal void SetGagTypeEquipSlot(GagType gagType, EquipSlot slot)
    {
        _gagStorageConfig.Current.GagStorage.GagEquipData.FirstOrDefault(x => x.Key == gagType).Value.Slot = slot;
        _gagStorageConfig.Save();
    }

    internal void SetGagTypeEquipItem(GagType gagType, EquipItem item)
    {
        _gagStorageConfig.Current.GagStorage.GagEquipData.FirstOrDefault(x => x.Key == gagType).Value.GameItem = item;
        _gagStorageConfig.Save();
    }

    internal void SetGagTypeStain(GagType gagType, StainId stain)
    {
        _gagStorageConfig.Current.GagStorage.GagEquipData.FirstOrDefault(x => x.Key == gagType).Value.GameStain = stain;
        _gagStorageConfig.Save();
    }

    internal void SetGagTypeSlotId(GagType gagType, int slotId)
    {
        _gagStorageConfig.Current.GagStorage.GagEquipData.FirstOrDefault(x => x.Key == gagType).Value.ActiveSlotId = slotId;
        _gagStorageConfig.Save();
    }

    internal void UpdateGagItem(GagType gagType, GagDrawData newData)
    {
        _gagStorageConfig.Current.GagStorage.GagEquipData[gagType] = newData;
        _gagStorageConfig.Save();
        Logger.LogInformation("GagStorage Config Saved");
    }
    #endregion Gag Storage Methods
    /* --------------------- Wardrobe Config Methods --------------------- */
    #region Wardrobe Config Methods
    /// <summary> 
    /// I swear to god, so not set anything inside this object through this fetch. Treat it as readonly.
    /// </summary>
    internal List<RestraintSet> StoredRestraintSets => WardrobeConfig.WardrobeStorage.RestraintSets;
    public List<string> GetRestraintSetNames() => WardrobeConfig.WardrobeStorage.RestraintSets.Select(set => set.Name).ToList();
    internal int GetActiveSetIdx() => WardrobeConfig.WardrobeStorage.RestraintSets.FindIndex(x => x.Enabled);
    internal RestraintSet GetActiveSet() => WardrobeConfig.WardrobeStorage.RestraintSets.FirstOrDefault(x => x.Enabled)!; // this can be null.
    internal RestraintSet GetRestraintSet(int setIndex) => WardrobeConfig.WardrobeStorage.RestraintSets[setIndex];
    internal int GetRestraintSetIdxByName(string name) => WardrobeConfig.WardrobeStorage.RestraintSets.FindIndex(x => x.Name == name);

    internal void AddNewRestraintSet(RestraintSet newSet)
    {
        // add 1 to the name until it is unique.
        while (WardrobeConfig.WardrobeStorage.RestraintSets.Any(x => x.Name == newSet.Name))
        {
            newSet.Name += "(copy)";
        }
        _wardrobeConfig.Current.WardrobeStorage.RestraintSets.Add(newSet);
        _wardrobeConfig.Save();
        Logger.LogInformation("Restraint Set added to wardrobe");
        // publish to mediator
        Mediator.Publish(new PlayerCharWardrobeChanged(DataUpdateKind.WardrobeRestraintOutfitsUpdated));
    }

    // remove a restraint set
    internal void RemoveRestraintSet(int setIndex)
    {
        _wardrobeConfig.Current.WardrobeStorage.RestraintSets.RemoveAt(setIndex);
        _wardrobeConfig.Save();
        Mediator.Publish(new PlayerCharWardrobeChanged(DataUpdateKind.WardrobeRestraintOutfitsUpdated));
    }

    // Called whenever set is saved.
    internal void UpdateRestraintSet(int setIndex, RestraintSet updatedSet)
    {
        _wardrobeConfig.Current.WardrobeStorage.RestraintSets[setIndex] = updatedSet;
        _wardrobeConfig.Save();
        Mediator.Publish(new PlayerCharWardrobeChanged(DataUpdateKind.WardrobeRestraintOutfitsUpdated));
    }

    internal bool PropertiesEnabledForSet(int setIndexToCheck, string UIDtoCheckPropertiesFor)
    {
        // do not allow hardcore properties for self.
        if (UIDtoCheckPropertiesFor == "SelfApplied") return false;

        HardcoreSetProperties setProperties = WardrobeConfig.WardrobeStorage.RestraintSets[setIndexToCheck].SetProperties[UIDtoCheckPropertiesFor];
        // if no object for this exists, return false
        if (setProperties == null) return false;
        // check if any properties are enabled
        return setProperties.LegsRestrained || setProperties.ArmsRestrained || setProperties.Gagged || setProperties.Blindfolded || setProperties.Immobile
            || setProperties.Weighty || setProperties.LightStimulation || setProperties.MildStimulation || setProperties.HeavyStimulation;
    }

    internal void SetRestraintSetState(int setIndex, string UIDofPair, UpdatedNewState newState, bool pushToServer = true)
    {
        if (newState == UpdatedNewState.Disabled)
        {
            WardrobeConfig.WardrobeStorage.RestraintSets[setIndex].Enabled = false;
            WardrobeConfig.WardrobeStorage.RestraintSets[setIndex].EnabledBy = string.Empty;
            _wardrobeConfig.Save();

            var pairHasHardcoreSetForUID = WardrobeConfig.WardrobeStorage.RestraintSets[setIndex].SetProperties.ContainsKey(UIDofPair);
            // see if the properties are enabled for this set for this user
            if (pairHasHardcoreSetForUID && PropertiesEnabledForSet(setIndex, UIDofPair))
            {
                Mediator.Publish(new RestraintSetToggledMessage(setIndex, UIDofPair, newState, true, pushToServer));
            }
            else
            {
                Mediator.Publish(new RestraintSetToggledMessage(setIndex, UIDofPair, newState, false, pushToServer));
            }
        }
        else
        {
            // disable all other restraint sets first
            WardrobeConfig.WardrobeStorage.RestraintSets
                .Where(set => set.Enabled)
                .ToList()
                .ForEach(set =>
                {
                    set.Enabled = false;
                    set.EnabledBy = string.Empty;
                });

            // then enable our set.
            WardrobeConfig.WardrobeStorage.RestraintSets[setIndex].Enabled = true;
            WardrobeConfig.WardrobeStorage.RestraintSets[setIndex].EnabledBy = UIDofPair;
            _wardrobeConfig.Save();

            var pairHasHardcoreSetForUID = WardrobeConfig.WardrobeStorage.RestraintSets[setIndex].SetProperties.ContainsKey(UIDofPair);

            // see if the properties are enabled for this set for this user
            if (pairHasHardcoreSetForUID && PropertiesEnabledForSet(setIndex, UIDofPair))
            {
                Mediator.Publish(new RestraintSetToggledMessage(setIndex, UIDofPair, newState, true, pushToServer));
            }
            else
            {
                Mediator.Publish(new RestraintSetToggledMessage(setIndex, UIDofPair, newState, false, pushToServer));
            }
        }
    }

    internal void LockRestraintSet(int setIndex, string UIDofPair, DateTimeOffset endLockTimeUTC, bool pushToServer = true)
    {
        // set the locked and locked-by status.
        WardrobeConfig.WardrobeStorage.RestraintSets[setIndex].Locked = true;
        WardrobeConfig.WardrobeStorage.RestraintSets[setIndex].LockedBy = UIDofPair;
        WardrobeConfig.WardrobeStorage.RestraintSets[setIndex].LockedUntil = endLockTimeUTC;
        _wardrobeConfig.Save();

        Mediator.Publish(new RestraintSetToggledMessage(setIndex, UIDofPair, UpdatedNewState.Locked, false, pushToServer));
    }

    internal void UnlockRestraintSet(int setIndex, string UIDofPair, bool pushToServer = true)
    {
        // Clear all locked states. (making the assumption this is only called when the UIDofPair matches the LockedBy)
        WardrobeConfig.WardrobeStorage.RestraintSets[setIndex].Locked = false;
        WardrobeConfig.WardrobeStorage.RestraintSets[setIndex].LockedBy = string.Empty;
        WardrobeConfig.WardrobeStorage.RestraintSets[setIndex].LockedUntil = DateTimeOffset.MinValue;
        _wardrobeConfig.Save();

        Mediator.Publish(new RestraintSetToggledMessage(setIndex, UIDofPair, UpdatedNewState.Unlocked, false, pushToServer));
    }



    internal int GetRestraintSetCount() => WardrobeConfig.WardrobeStorage.RestraintSets.Count;
    internal List<AssociatedMod> GetAssociatedMods(int setIndex) => WardrobeConfig.WardrobeStorage.RestraintSets[setIndex].AssociatedMods;
    internal bool IsBlindfoldActive() => WardrobeConfig.WardrobeStorage.BlindfoldInfo.IsActive;

    internal void SetBlindfoldState(bool newState, string applierUID)
    {
        WardrobeConfig.WardrobeStorage.BlindfoldInfo.IsActive = newState;
        WardrobeConfig.WardrobeStorage.BlindfoldInfo.BlindfoldedBy = applierUID;
        _wardrobeConfig.Save();
    }

    // TODO this logic is flawed, and so is above, as this should not be manipulated by the client.
    // rework later to fix and make it scan against pair list.
    internal string GetBlindfoldedBy() => WardrobeConfig.WardrobeStorage.BlindfoldInfo.BlindfoldedBy;
    internal EquipDrawData GetBlindfoldItem() => WardrobeConfig.WardrobeStorage.BlindfoldInfo.BlindfoldItem;
    internal void SetBlindfoldItem(EquipDrawData drawData)
    {
        WardrobeConfig.WardrobeStorage.BlindfoldInfo.BlindfoldItem = drawData;
        _wardrobeConfig.Save();
    }
    #endregion Wardrobe Config Methods

    /* --------------------- Puppeteer Alias Configs --------------------- */
    #region Alias Config Methods
    public List<AliasTrigger> FetchListForPair(string userId)
    {
        if (!_aliasConfig.Current.AliasStorage.ContainsKey(userId))
        {
            Logger.LogDebug("User {userId} does not have an alias list, creating one.", userId);
            // If not, initialize it with a new AliasList object
            _aliasConfig.Current.AliasStorage[userId] = new AliasStorage();
            _aliasConfig.Save();
        }
        return _aliasConfig.Current.AliasStorage[userId].AliasList;
    }
    public void AddAlias(string userId, AliasTrigger alias)
    {
        // Check if the userId key exists in the AliasStorage dictionary
        if (!_aliasConfig.Current.AliasStorage.ContainsKey(userId))
        {
            Logger.LogDebug("User {userId} does not have an alias list, creating one.", userId);
            // If not, initialize it with a new AliasList object
            _aliasConfig.Current.AliasStorage[userId] = new AliasStorage();
        }
        // Add alias logic
        _aliasConfig.Current.AliasStorage[userId].AliasList.Add(alias);
        _aliasConfig.Save();
        Mediator.Publish(new PlayerCharAliasChanged(userId));
    }

    public void RemoveAlias(string userId, AliasTrigger alias)
    {
        // Remove alias logic
        _aliasConfig.Current.AliasStorage[userId].AliasList.Remove(alias);
        _aliasConfig.Save();
        Mediator.Publish(new PlayerCharAliasChanged(userId));
    }

    public void UpdateAliasInput(string userId, int aliasIndex, string input)
    {
        // Update alias input logic
        _aliasConfig.Current.AliasStorage[userId].AliasList[aliasIndex].InputCommand = input;
        _aliasConfig.Save();
        Mediator.Publish(new PlayerCharAliasChanged(userId));
    }

    public void UpdateAliasOutput(string userId, int aliasIndex, string output)
    {
        // Update alias output logic
        _aliasConfig.Current.AliasStorage[userId].AliasList[aliasIndex].OutputCommand = output;
        _aliasConfig.Save();
        Mediator.Publish(new PlayerCharAliasChanged(userId));
    }

    #endregion Alias Config Methods
    /* --------------------- Toybox Pattern Configs --------------------- */
    #region Pattern Config Methods

    /// <summary> Fetches the currently Active Alarm to have as a reference accessor. Can be null. </summary>
    public PatternData? GetActiveRunningPattern() => _patternConfig.Current.PatternStorage.Patterns.FirstOrDefault(p => p.IsActive);

    public PatternData FetchPattern(int idx) => _patternConfig.Current.PatternStorage.Patterns[idx];
    public int GetPatternIdxByName(string name) => _patternConfig.Current.PatternStorage.Patterns.FindIndex(p => p.Name == name);
    public List<string> GetPatternNames() => _patternConfig.Current.PatternStorage.Patterns.Select(set => set.Name).ToList();
    public bool IsIndexInBounds(int index) => index >= 0 && index < _patternConfig.Current.PatternStorage.Patterns.Count;
    public bool IsAnyPatternPlaying() => _patternConfig.Current.PatternStorage.Patterns.Any(p => p.IsActive);
    public int ActivePatternIdx() => _patternConfig.Current.PatternStorage.Patterns.FindIndex(p => p.IsActive);
    public int GetPatternCount() => _patternConfig.Current.PatternStorage.Patterns.Count;

    public TimeSpan GetPatternLength(int idx)
    {
        var pattern = _patternConfig.Current.PatternStorage.Patterns[idx].Duration;

        if (string.IsNullOrWhiteSpace(pattern) || !TimeSpan.TryParseExact(pattern, "hh\\:mm\\:ss", null, out var timespanDuration))
        {
            timespanDuration = TimeSpan.Zero; // Default to 0 minutes and 0 seconds
        }
        return timespanDuration;
    }

    public void AddNewPattern(PatternData newPattern)
    {
        _patternConfig.Current.PatternStorage.Patterns.Add(newPattern);
        _patternConfig.Save();
        // publish to mediator one was added
        Logger.LogInformation("Pattern Added: {0}", newPattern.Name);
        Mediator.Publish(new PlayerCharToyboxChanged(DataUpdateKind.ToyboxPatternListUpdated));
    }

    public void RemovePattern(int indexToRemove)
    {
        // grab the patternData of the pattern we are removing.
        var patternToRemove = _patternConfig.Current.PatternStorage.Patterns[indexToRemove];
        _patternConfig.Current.PatternStorage.Patterns.RemoveAt(indexToRemove);
        _patternConfig.Save();
        // publish to mediator one was removed
        Mediator.Publish(new PatternRemovedMessage(patternToRemove));
        Mediator.Publish(new PlayerCharToyboxChanged(DataUpdateKind.ToyboxPatternListUpdated));
    }

    public void SetPatternState(int idx, bool newState, string startPoint = "", string playbackDuration = "", bool shouldPublishToMediator = true)
    {
        _patternConfig.Current.PatternStorage.Patterns[idx].IsActive = newState;
        _patternConfig.Save();
        if (newState)
        {
            // if we are activating, make sure we pass in the startpoint and playback duration. if the passed in is string.Empty, use the vars from the pattern[idx].
            Mediator.Publish(new PatternActivedMessage(idx,
                string.IsNullOrWhiteSpace(startPoint) ? _patternConfig.Current.PatternStorage.Patterns[idx].StartPoint : startPoint,
                string.IsNullOrWhiteSpace(playbackDuration) ? _patternConfig.Current.PatternStorage.Patterns[idx].Duration : playbackDuration));
        }
        else
        {
            Mediator.Publish(new PatternDeactivedMessage(idx));
        }
        // Push update if we should publish
        if (shouldPublishToMediator)
        {
            Mediator.Publish(new PlayerCharToyboxChanged(DataUpdateKind.ToyboxPatternListUpdated));
        }
    }

    public void UpdatePatternStatesFromCallback(List<PatternInfo> callbackPatternList)
    {
        // iterate over each alarmInfo in the alarmInfo list. If any of the AlarmStorages alarms have a different enabled state than the alarm info's, change it.
        /*        foreach (AlarmInfo alarmInfo in callbackAlarmList)
                {
                    // if the alarm is found in the list,
                    if (_alarmConfig.Current.AlarmStorage.Alarms.Any(x => x.Name == alarmInfo.Name))
                    {
                        // grab the alarm reference
                        var alarmRef = _alarmConfig.Current.AlarmStorage.Alarms.FirstOrDefault(x => x.Name == alarmInfo.Name);
                        // update the enabled state if the values are different.
                        if (alarmRef != null && alarmRef.Enabled != alarmInfo.Enabled)
                        {
                            alarmRef.Enabled = alarmInfo.Enabled;
                        }
                    }
                    else
                    {
                        Logger.LogWarning("Failed to match an Alarm in your list with an alarm in the callbacks list. This shouldnt be possible?");
                    }
                } DO NOTHING FOR NOW */
    }

    public void UpdatePattern(PatternData pattern, int idx)
    {
        _patternConfig.Current.PatternStorage.Patterns[idx] = pattern;
        _patternConfig.Save();
        Mediator.Publish(new PlayerCharToyboxChanged(DataUpdateKind.ToyboxPatternListUpdated));
    }

    public string EnsureUniqueName(string baseName)
    {
        int copyNumber = 1;
        string newName = baseName;

        while (_patternConfig.Current.PatternStorage.Patterns.Any(set => set.Name == newName))
            newName = baseName + $"(copy{copyNumber++})";

        return newName;
    }

    #endregion Pattern Config Methods
    /* --------------------- Toybox Alarm Configs --------------------- */
    #region Alarm Config Methods
    public List<Alarm> AlarmsRef => _alarmConfig.Current.AlarmStorage.Alarms; // readonly accessor
    public Alarm FetchAlarm(int idx) => _alarmConfig.Current.AlarmStorage.Alarms[idx];
    public int FetchAlarmCount() => _alarmConfig.Current.AlarmStorage.Alarms.Count;
    public void RemovePatternNameFromAlarms(string patternName)
    {
        for (int i = 0; i < _alarmConfig.Current.AlarmStorage.Alarms.Count; i++)
        {
            var alarm = _alarmConfig.Current.AlarmStorage.Alarms[i];
            if (alarm.PatternToPlay == patternName)
            {
                alarm.PatternToPlay = "";
                alarm.PatternDuration = "00:00";
                _alarmConfig.Save();
                Mediator.Publish(new PlayerCharToyboxChanged(DataUpdateKind.ToyboxAlarmListUpdated));
            }
        }
    }

    public void AddNewAlarm(Alarm alarm)
    {
        // ensure the alarm has a unique name.
        int copyNumber = 1;
        string newName = alarm.Name;

        while (_alarmConfig.Current.AlarmStorage.Alarms.Any(set => set.Name == newName))
            newName = alarm.Name + $"(copy{copyNumber++})";

        alarm.Name = newName;
        _alarmConfig.Current.AlarmStorage.Alarms.Add(alarm);
        _alarmConfig.Save();

        Logger.LogInformation("Alarm Added: {0}", alarm.Name);
        Mediator.Publish(new PlayerCharToyboxChanged(DataUpdateKind.ToyboxAlarmListUpdated));
    }

    public void RemoveAlarm(int indexToRemove)
    {
        Logger.LogInformation("Alarm Removed: {0}", _alarmConfig.Current.AlarmStorage.Alarms[indexToRemove].Name);
        _alarmConfig.Current.AlarmStorage.Alarms.RemoveAt(indexToRemove);
        _alarmConfig.Save();

        Mediator.Publish(new PlayerCharToyboxChanged(DataUpdateKind.ToyboxAlarmListUpdated));
    }

    public void SetAlarmState(int idx, bool newState, bool shouldPublishToMediator = true)
    {
        _alarmConfig.Current.AlarmStorage.Alarms[idx].Enabled = newState;
        _alarmConfig.Save();

        // publish the alarm added/removed based on state
        if (shouldPublishToMediator)
        {
            Mediator.Publish(new PlayerCharToyboxChanged(DataUpdateKind.ToyboxAlarmToggled));
        }
    }

    public void UpdateAlarmStatesFromCallback(List<AlarmInfo> callbackAlarmList)
    {
        // iterate over each alarmInfo in the alarmInfo list. If any of the AlarmStorages alarms have a different enabled state than the alarm info's, change it.
        foreach (AlarmInfo alarmInfo in callbackAlarmList)
        {
            // if the alarm is found in the list,
            if (_alarmConfig.Current.AlarmStorage.Alarms.Any(x => x.Name == alarmInfo.Name))
            {
                // grab the alarm reference
                var alarmRef = _alarmConfig.Current.AlarmStorage.Alarms.FirstOrDefault(x => x.Name == alarmInfo.Name);
                // update the enabled state if the values are different.
                if (alarmRef != null && alarmRef.Enabled != alarmInfo.Enabled)
                {
                    alarmRef.Enabled = alarmInfo.Enabled;
                }
            }
            else
            {
                Logger.LogWarning("Failed to match an Alarm in your list with an alarm in the callbacks list. This shouldnt be possible?");
            }
        }
    }


    public void UpdateAlarm(Alarm alarm, int idx)
    {
        _alarmConfig.Current.AlarmStorage.Alarms[idx] = alarm;
        _alarmConfig.Save();
        Mediator.Publish(new PlayerCharToyboxChanged(DataUpdateKind.ToyboxAlarmListUpdated));
    }

    #endregion Alarm Config Methods

    /* --------------------- Toybox Trigger Configs --------------------- */
    #region Trigger Config Methods

    // stuff

    #endregion Trigger Config Methods

    #region API Compilation
    public CharacterToyboxData CompileToyboxToAPI()
    {
        // Map PatternConfig to PatternInfo
        var patternList = new List<PatternInfo>();
        foreach (var pattern in PatternConfig.PatternStorage.Patterns)
        {
            patternList.Add(new PatternInfo
            {
                Name = pattern.Name,
                Description = pattern.Description,
                Duration = pattern.Duration,
                IsActive = pattern.IsActive,
                ShouldLoop = pattern.ShouldLoop
            });
        }

        // Map TriggerConfig to TriggerInfo
        var triggerList = new List<TriggerInfo>();
        foreach (var trigger in TriggerConfig.TriggerStorage.Triggers)
        {
            triggerList.Add(new TriggerInfo
            {
                Enabled = trigger.Enabled,
                Name = trigger.Name,
                Description = trigger.Description,
                Type = trigger.Type,
                CanViewAndToggleTrigger = trigger.CanTrigger,
            });
        }

        // Map AlarmConfig to AlarmInfo
        var alarmList = new List<AlarmInfo>();
        foreach (var alarm in AlarmConfig.AlarmStorage.Alarms)
        {
            alarmList.Add(new AlarmInfo
            {
                Enabled = alarm.Enabled,
                Name = alarm.Name,
                SetTimeUTC = alarm.SetTimeUTC,
                PatternToPlay = alarm.PatternToPlay,
                PatternDuration = alarm.PatternDuration,
                RepeatFrequency = alarm.RepeatFrequency
            });
        }

        // Create and return CharacterToyboxData
        return new CharacterToyboxData
        {
            PatternList = patternList,
            AlarmList = alarmList,
            TriggerList = triggerList
        };
    }
    #endregion API Compilation

    #region UI Prints
    public void DrawWardrobeInfo()
    {
        ImGui.Text("Wardrobe Outfits:");
        ImGui.Indent();
        foreach (var item in WardrobeConfig.WardrobeStorage.RestraintSets)
        {
            ImGui.Text(item.Name);
        }
        ImGui.Unindent();
        var ActiveSet = WardrobeConfig.WardrobeStorage.RestraintSets.FirstOrDefault(x => x.Enabled);
        if (ActiveSet != null)
        {
            ImGui.Text("Active Set Info: ");
            ImGui.Indent();
            ImGui.Text($"Name: {ActiveSet.Name}");
            ImGui.Text($"Description: {ActiveSet.Description}");
            ImGui.Text($"Enabled By: {ActiveSet.EnabledBy}");
            ImGui.Text($"Is Locked: {ActiveSet.Locked}");
            ImGui.Text($"Locked By: {ActiveSet.LockedBy}");
            ImGui.Text($"Locked Until: {ActiveSet.LockedUntil}");
            ImGui.Unindent();
        }
    }


    public void DrawAliasLists()
    {
        foreach (var alias in AliasConfig.AliasStorage)
        {
            if (ImGui.CollapsingHeader($"Alias Data for {alias.Key}"))
            {
                ImGui.Text("List of Alias's For this User:");
                // begin a table.
                using var table = ImRaii.Table($"##table-for-{alias.Key}", 2);
                if (!table) { return; }

                using var spacing = ImRaii.PushStyle(ImGuiStyleVar.ItemSpacing, ImGui.GetStyle().ItemInnerSpacing);
                ImGui.TableSetupColumn("If You Say:", ImGuiTableColumnFlags.WidthFixed, ImGuiHelpers.GlobalScale * 100);
                ImGui.TableSetupColumn("They will Execute:", ImGuiTableColumnFlags.WidthStretch);

                foreach (var aliasTrigger in alias.Value.AliasList)
                {
                    ImGui.Separator();
                    ImGui.Text("[INPUT TRIGGER]: ");
                    ImGui.SameLine();
                    ImGui.Text(aliasTrigger.InputCommand);
                    ImGui.NewLine();
                    ImGui.Text("[OUTPUT RESPONSE]: ");
                    ImGui.SameLine();
                    ImGui.Text(aliasTrigger.OutputCommand);
                }
            }
        }
    }

    public void DrawPatternsInfo()
    {
        foreach (var item in PatternConfig.PatternStorage.Patterns)
        {
            ImGui.Text($"Info for Pattern: {item.Name}");
            ImGui.Indent();
            ImGui.Text($"Description: {item.Description}");
            ImGui.Text($"Duration: {item.Duration}");
            ImGui.Text($"Is Active: {item.IsActive}");
            ImGui.Text($"Should Loop: {item.ShouldLoop}");
            ImGui.Unindent();
        }
    }



    #endregion UI Prints
}
