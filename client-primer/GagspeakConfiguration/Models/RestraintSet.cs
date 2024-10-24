using Dalamud.Utility;
using GagSpeak.Interop.IpcHelpers.Moodles;
using GagSpeak.Utils;
using GagSpeak.WebAPI.Utils;
using Penumbra.GameData.Enums;
using Penumbra.GameData.Structs;

namespace GagSpeak.GagspeakConfiguration.Models;

/// <summary>
/// A basic authentication class to validate that the information from the client when they attempt to connect is correct.
/// </summary>
[Serializable]
public record RestraintSet : IMoodlesAssociable
{
    [JsonIgnore]
    private readonly ItemIdVars _itemIdVars;

    public RestraintSet(ItemIdVars itemHelpers)
    {
        _itemIdVars = itemHelpers;

        // Initialize DrawData in the constructor
        DrawData = EquipSlotExtensions.EqdpSlots.ToDictionary(
            slot => slot,
            slot => new EquipDrawData(_itemIdVars, ItemIdVars.NothingItem(slot))
            {
                Slot = slot,
                IsEnabled = false,
            }
        );

        // Initialize BonusDrawData in the constructor
        BonusDrawData = BonusExtensions.AllFlags.ToDictionary(
            slot => slot,
            slot => new BonusDrawData(BonusItem.Empty(slot))
            {
                Slot = slot,
                IsEnabled = false,
            }
        );
    }

    /// <summary> The name of the pattern </summary>
    public string Name { get; set; } = "New Restraint Set";

    /// <summary> The description of the pattern </summary>
    public string Description { get; set; } = "Enter Description Here...";
    public bool Enabled { get; set; } = false;
    public string EnabledBy { get; set; } = string.Empty;

    [JsonIgnore]
    public bool Locked => LockType != "None";

    public string LockType { get; set; } = "None";
    public string LockPassword { get; set; } = string.Empty;
    public DateTimeOffset LockedUntil { get; set; } = DateTimeOffset.MinValue;
    public string LockedBy { get; set; } = string.Empty;
    public bool ForceHeadgearOnEnable { get; set; } = false;
    public bool ForceVisorOnEnable { get; set; } = false;
    public Dictionary<EquipSlot, EquipDrawData> DrawData { get; set; } = [];
    public Dictionary<BonusItemFlag, BonusDrawData> BonusDrawData { get; set; } = [];

    // any Mods to apply with this set, and their respective settings.
    public List<AssociatedMod> AssociatedMods { get; private set; } = new List<AssociatedMod>();

    // the list of Moodles to apply when the set is active, and remove when inactive.
    public List<Guid> AssociatedMoodles { get; private set; } = new List<Guid>();
    public List<Guid> AssociatedMoodlePresets { get; private set; } = new List<Guid>();

    // Customize+ preset to activate here soon when it stops being a bitch.

    // Spatial Audio Sound Type to use while this restraint set is active. [WIP]


    /// <summary> Controls which UID's are able to see this restraint set. </summary>
    public List<string> ViewAccess { get; private set; } = new List<string>();

    /// <summary> 
    /// The Hardcore Set Properties to apply when restraint set is toggled.
    /// The string indicates the UID associated with the set properties.
    /// </summary>
    public Dictionary<string, HardcoreSetProperties> SetProperties { get; set; } = new();


    public RestraintSet DeepCloneSet()
    {
        // Clone basic properties
        var clonedSet = new RestraintSet(_itemIdVars)
        {
            Name = this.Name,
            Description = this.Description,
            Enabled = this.Enabled,
            EnabledBy = this.EnabledBy,
            LockType = this.LockType,
            LockPassword = this.LockPassword,
            LockedUntil = this.LockedUntil,
            LockedBy = this.LockedBy,
            ForceHeadgearOnEnable = this.ForceHeadgearOnEnable,
            ForceVisorOnEnable = this.ForceVisorOnEnable,
            AssociatedMods = new List<AssociatedMod>(this.AssociatedMods.Select(mod => mod.DeepClone())),
            AssociatedMoodles = new List<Guid>(this.AssociatedMoodles),
            AssociatedMoodlePresets = new List<Guid>(this.AssociatedMoodlePresets),
            ViewAccess = new List<string>(this.ViewAccess),
            SetProperties = new Dictionary<string, HardcoreSetProperties>(this.SetProperties.ToDictionary(
                kvp => kvp.Key,
                kvp => kvp.Value.DeepClone()))
        };

        // Deep clone DrawData
        StaticLogger.Logger.LogWarning("Attempting DeepClone");
        clonedSet.DrawData = new Dictionary<EquipSlot, EquipDrawData>(
            this.DrawData.ToDictionary(
                kvp => kvp.Key,
                kvp => kvp.Value.DeepCloneDrawData()));
        // log the drawdata after.
        StaticLogger.Logger.LogWarning("DrawData after DeepClone: {0}", clonedSet.DrawData);


        // Deep clone BonusDrawData
        clonedSet.BonusDrawData = new Dictionary<BonusItemFlag, BonusDrawData>(
            this.BonusDrawData.ToDictionary(
                kvp => kvp.Key,
                kvp => kvp.Value.DeepClone()));

        return clonedSet;
    }


    // parameterless constructor for serialization
    public JObject Serialize()
    {
        var serializer = new JsonSerializer();
        // for the DrawData dictionary.
        JObject drawDataEquipmentObject = new JObject();
        // serialize each item in it
        foreach (var pair in DrawData)
        {
            drawDataEquipmentObject[pair.Key.ToString()] = new JObject()
            {
                ["Slot"] = pair.Value.Slot.ToString(),
                ["IsEnabled"] = pair.Value.IsEnabled,
                ["CustomItemId"] = pair.Value.GameItem.Id.ToString(),
                ["GameStain"] = pair.Value.GameStain.ToString(),
            };
        }

        // for the BonusDrawData dictionary.
        var bonusDrawDataArray = new JArray();
        // serialize each item in it
        foreach (var pair in BonusDrawData)
        {
            bonusDrawDataArray.Add(new JObject()
            {
                ["BonusItemFlag"] = pair.Key.ToString(),
                ["BonusDrawData"] = pair.Value.Serialize()
            });
        }

        // for the AssociatedMods list.
        var associatedModsArray = new JArray();
        // serialize each item in it
        foreach (var mod in AssociatedMods)
        {
            associatedModsArray.Add(mod.Serialize());
        }

        // for the set properties
        var setPropertiesArray = new JArray();
        // serialize each item in it
        foreach (var pair in SetProperties)
        {
            setPropertiesArray.Add(new JObject()
            {
                ["UID"] = pair.Key,
                ["HardcoreSetProperties"] = pair.Value.Serialize()
            });
        }

        return new JObject()
        {
            ["Name"] = Name,
            ["Description"] = Description,
            ["Enabled"] = Enabled,
            ["EnabledBy"] = EnabledBy,
            ["Locked"] = Locked,
            ["LockType"] = LockType,
            ["LockPassword"] = LockPassword,
            ["LockedUntil"] = LockedUntil.UtcDateTime.ToString("o"),
            ["LockedBy"] = LockedBy,
            ["ForceHeadgearOnEnable"] = ForceHeadgearOnEnable,
            ["ForceVisorOnEnable"] = ForceVisorOnEnable,
            ["DrawData"] = drawDataEquipmentObject,
            ["BonusDrawData"] = bonusDrawDataArray,
            ["AssociatedMods"] = associatedModsArray,
            ["AssociatedMoodles"] = new JArray(AssociatedMoodles),
            ["AssociatedMoodlePresets"] = new JArray(AssociatedMoodlePresets),
            ["ViewAccess"] = new JArray(ViewAccess),
            ["SetProperties"] = setPropertiesArray
        };
    }


    public void Deserialize(JObject jsonObject)
    {
        Name = jsonObject["Name"]?.Value<string>() ?? string.Empty;
        Description = jsonObject["Description"]?.Value<string>() ?? string.Empty;
        Enabled = jsonObject["Enabled"]?.Value<bool>() ?? false;
        EnabledBy = jsonObject["EnabledBy"]?.Value<string>() ?? string.Empty;
        LockType = jsonObject["LockType"]?.Value<string>() ?? "None";
        if (LockType.IsNullOrEmpty()) LockType = "None"; // correct lockType if it is null or empty
        LockPassword = jsonObject["LockPassword"]?.Value<string>() ?? string.Empty;
        if (jsonObject["LockedUntil"]?.Type == JTokenType.String)
        {
            string lockedUntilStr = jsonObject["LockedUntil"].Value<string>();
            if (DateTimeOffset.TryParse(lockedUntilStr, out DateTimeOffset result))
            {
                LockedUntil = result;
            }
            else
            {
                LockedUntil = DateTimeOffset.MinValue; // Or handle the parse failure as appropriate
            }
        }
        else
        {
            LockedUntil = DateTimeOffset.MinValue; // Default or error value if the token is not a string
        }
        LockedBy = jsonObject["LockedBy"]?.Value<string>() ?? string.Empty;
        ForceHeadgearOnEnable = jsonObject["ForceHeadgearOnEnable"]?.Value<bool>() ?? false;
        ForceVisorOnEnable = jsonObject["ForceVisorOnEnable"]?.Value<bool>() ?? false;
        try
        {
            var drawDataObject = jsonObject["DrawData"]?.Value<JObject>();
            if (drawDataObject != null)
            {
                foreach (var property in drawDataObject.Properties())
                {
                    var equipmentSlot = (EquipSlot)Enum.Parse(typeof(EquipSlot), property.Name);
                    var itemObject = property.Value.Value<JObject>();
                    if (itemObject != null)
                    {
                        ulong customItemId = itemObject["CustomItemId"]?.Value<ulong>() ?? 4294967164;
                        var gameStainString = itemObject["GameStain"]?.Value<string>() ?? "0,0";
                        var stainParts = gameStainString.Split(',');

                        StainIds gameStain;
                        if (stainParts.Length == 2 && int.TryParse(stainParts[0], out int stain1) && int.TryParse(stainParts[1], out int stain2))
                        {
                            gameStain = new StainIds((StainId)stain1, (StainId)stain2);
                        }
                        else
                        {
                            gameStain = StainIds.None;
                        }

                        var drawData = new EquipDrawData(_itemIdVars, ItemIdVars.NothingItem(equipmentSlot))
                        {
                            Slot = (EquipSlot)Enum.Parse(typeof(EquipSlot), itemObject["Slot"]?.Value<string>() ?? string.Empty),
                            IsEnabled = itemObject["IsEnabled"]?.Value<bool>() ?? false,
                            GameItem = _itemIdVars.Resolve(equipmentSlot, new CustomItemId(customItemId)),
                            GameStain = gameStain
                        };

                        DrawData[equipmentSlot] = drawData;
                    }
                }
            }

            // Deserialize the BonusDrawData
            if (jsonObject["BonusDrawData"] is JObject bonusDrawDataObj)
            {
                foreach (var (slot, bonusDrawData) in BonusDrawData)
                {
                    if (bonusDrawDataObj[slot.ToString()] is JObject slotObj)
                    {
                        bonusDrawData.Deserialize(slotObj);
                        // add it to the bonus draw data
                        BonusDrawData[slot] = bonusDrawData;
                    }
                }
            }

            // Deserialize the AssociatedMods
            if (jsonObject["AssociatedMods"] is JArray associatedModsArray)
            {
                AssociatedMods = associatedModsArray.Select(mod => mod.ToObject<AssociatedMod>()).ToList();
            }

            // Deserialize the AssociatedMoodles
            if (jsonObject["AssociatedMoodles"] is JArray associatedMoodlesArray)
            {
                AssociatedMoodles = associatedMoodlesArray.Select(moodle => Guid.Parse(moodle.Value<string>())).ToList();
            }

            // Deserialize the AssociatedMoodlePresets
            if (jsonObject["AssociatedMoodlePresets"] is JArray associatedMoodlePresetsArray)
            {
                AssociatedMoodlePresets = associatedMoodlePresetsArray.Select(preset => preset.Value<Guid>()).ToList();
            }

            // Deserialize the ViewAccess
            if (jsonObject["ViewAccess"] is JArray viewAccessArray)
            {
                ViewAccess = viewAccessArray.Select(viewer => viewer.Value<string>()).ToList();
            }

            // Deserialize the SetProperties
            if (jsonObject["SetProperties"] is JObject setPropertiesObj)
            {
                foreach (var (uid, setProperties) in SetProperties)
                {
                    if (setPropertiesObj[uid] is JObject uidObj)
                    {
                        setProperties.Deserialize(uidObj);
                        // add it to the set properties
                        SetProperties[uid] = setProperties;
                    }
                }
            }
        }
        catch (Exception e)
        {
            StaticLogger.Logger.LogError($"Failed to deserialize RestraintSet {Name} {e}");
            throw new Exception($"Failed to deserialize RestraintSet {Name} {e}");
        }
    }
}
