using FFXIVClientStructs.FFXIV.Client.Game.UI;
using Newtonsoft.Json.Linq;

namespace GagSpeak.GagspeakConfiguration.Models;

/// <summary>
/// A basic authentication class to validate that the information from the client when they attempt to connect is correct.
/// </summary>
[Serializable]
public record PatternData
{
    /// <summary> The name of the pattern </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary> The description of the pattern </summary>
    public string Description { get; set; } = string.Empty;

    /// <summary> The author of the pattern (Anonymous by default) </summary>
    public string Author { get; set; } = "Anon. Kinkster";

    /// <summary> Tags for the pattern. 5 tags at most. </summary>
    public List<string> Tags { get; set; } = new();

    /// <summary> The duration of the pattern </summary>
    public string Duration { get; set; } = "00:00";

    /// <summary> The start point of the pattern to play </summary>
    public string StartPoint { get; set; } = "00:00";

    /// <summary> The duration of the pattern to play (if 00:00, play full) </summary>
    public string PlaybackDuration { get; set; } = "00:00";

    /// <summary> If the pattern is active </summary>
    public bool IsActive { get; set; } = false;

    /// <summary> If the pattern should loop </summary>
    public bool ShouldLoop { get; set; } = false;

    /// <summary> The list of allowed users who can view this pattern </summary>
    public List<string> AllowedUsers { get; set; } = new();

    /// <summary> The pattern byte data </summary>
    public List<byte> PatternByteData { get; set; } = new();

    public JObject Serialize()
    {
        // Convert _patternData to a comma-separated string
        string patternDataString = string.Join(",", PatternByteData);

        return new JObject()
        {
            ["Name"] = Name,
            ["Description"] = Description,
            ["Author"] = Author,
            ["Tags"] = new JArray(Tags),
            ["Duration"] = Duration,
            ["StartPoint"] = StartPoint,
            ["PlaybackDuration"] = PlaybackDuration,
            ["IsActive"] = IsActive,
            ["ShouldLoop"] = ShouldLoop,
            ["AllowedUsers"] = new JArray(AllowedUsers),
            ["PatternByteData"] = patternDataString
        };
    }

    public void Deserialize(JObject jsonObject)
    {
        try
        {
            Name = jsonObject["Name"]?.Value<string>() ?? string.Empty;
            Description = jsonObject["Description"]?.Value<string>() ?? string.Empty;
            Author = jsonObject["Author"]?.Value<string>() ?? "Anon. Kinkster";

            // Deserialize the ViewAccess
            if (jsonObject["Tags"] is JArray viewAccessArray)
            {
                Tags = viewAccessArray.Select(x => x.Value<string>()).ToList()!;
            }

            Duration = jsonObject["Duration"]?.Value<string>() ?? "00:00";
            StartPoint = jsonObject["StartPoint"]?.Value<string>() ?? "00:00";
            PlaybackDuration = jsonObject["PlaybackDuration"]?.Value<string>() ?? "00:00";
            IsActive = jsonObject["IsActive"]?.Value<bool>() ?? false;
            ShouldLoop = jsonObject["ShouldLoop"]?.Value<bool>() ?? false;

            // Deserialize the AllowedUsers
            if (jsonObject["AllowedUsers"] is JArray allowedUsersArray)
            {
                AllowedUsers = allowedUsersArray.Select(x => x.Value<string>()).ToList()!;
            }

            PatternByteData.Clear();
            var patternDataString = jsonObject["PatternByteData"]?.Value<string>();
            if (string.IsNullOrEmpty(patternDataString))
            {
                // If the string is null or empty, generate a list with a single byte of 0
                PatternByteData = new List<byte> { (byte)0 };
            }
            else
            {
                // Otherwise, split the string into an array and convert each element to a byte
                PatternByteData = patternDataString.Split(',')
                    .Select(byte.Parse)
                    .ToList();
            }
        }
        catch (System.Exception e) { throw new Exception($"{e} Error deserializing pattern data"); }
    }
}
