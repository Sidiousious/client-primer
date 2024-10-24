using Dalamud.Interface;
using Dalamud.Interface.Colors;
using Dalamud.Interface.GameFonts;
using Dalamud.Interface.ManagedFontAtlas;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Textures.TextureWraps;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Dalamud.Utility;
using GagSpeak.Interop;
using GagSpeak.Interop.Ipc;
using GagSpeak.Localization;
using GagSpeak.PlayerData.Pairs;
using GagSpeak.Services.ConfigurationServices;
using GagSpeak.Services.Mediator;
using GagSpeak.UpdateMonitoring;
using GagSpeak.Utils;
using GagSpeak.WebAPI;
using GagspeakAPI.Data;
using GagspeakAPI.Enums;
using ImGuiNET;
using OtterGui.Text;
using PInvoke;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;


namespace GagSpeak.UI;

/// <summary> 
/// The shared service for UI elements within our plugin. 
/// 
/// This function should be expected to take advantage 
/// of classes with common functionality, preventing copy pasting.
/// 
/// Think of it as a collection of helpers for all functions.
/// </summary>
public partial class UiSharedService : DisposableMediatorSubscriberBase
{
    public const string TooltipSeparator = "--SEP--";                           // the tooltip seperator                                
    public static readonly ImGuiWindowFlags PopupWindowFlags = ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse;
    private const string _nicknameEnd = "##GAGSPEAK_USER_NICKNAME_END##";       // the end of the nickname
    private const string _nicknameStart = "##GAGSPEAK_USER_NICKNAME_START##";   // the start of the nickname
    private readonly Dalamud.Localization _localization;                        // language localization for our plugin

    private readonly ApiController _apiController;                              // our api controller for the server connectivity
    private readonly ClientConfigurationManager _clientConfigs;    // the client-end related config service 
    private readonly ServerConfigurationManager _serverConfigs;    // the server-end related config manager
    private readonly OnFrameworkService _frameworkUtil;                         // helpers for functions that should occur dalamud's framework  thread
    private readonly IpcManager _ipcManager;                                    // manager for the IPC's our plugin links 

    private readonly IDalamudPluginInterface _pi;       // the primary interface for our plugin
    private readonly ITextureProvider _textureProvider; // the texture provider for our plugin
    private ISharedImmediateTexture _sharedTextures;    // represents a shared texture cache for plugin images. (REMAKE THIS INTO A DICTIONARY)

    public Dictionary<string, object> _selectedComboItems;    // the selected combo items
    private bool _penumbraExists = false;                               // if penumbra currently exists on the client
    private bool _glamourerExists = false;                              // if glamourer currently exists on the client
    private bool _customizePlusExists = false;                          // if customize plus currently exists on the client
    private bool _moodlesExists = false;                                // if moodles currently exists on the client
    private bool _useTheme = true;                                      // if we should use the GagSpeak Theme

    // default image paths
    private const string Logo256Path = "RequiredImages\\icon256.png";
    private const string Logo256bgPath = "RequiredImages\\icon256bg.png";
    private const string SupporterBooster = "RequiredImages\\BoosterIcon.png";
    private const string SupporterT1 = "RequiredImages\\Tier1Icon.png";
    private const string SupporterT2 = "RequiredImages\\Tier2Icon.png";
    private const string SupporterT3 = "RequiredImages\\Tier3Icon.png";
    private const string OwnerT4 = "RequiredImages\\Tier4Icon.png";
    // helpers to get the static images
    public IDalamudTextureWrap GetLogo() => GetImageFromDirectoryFile(Logo256Path);
    public IDalamudTextureWrap? RentLogo() => RentImageFromFile(Logo256Path);
    public IDalamudTextureWrap GetLogoNoRadial() => GetImageFromDirectoryFile(Logo256bgPath);
    public IDalamudTextureWrap? RentLogoNoRadial() => RentImageFromFile(Logo256bgPath);
    public IDalamudTextureWrap GetSupporterBooster() => GetImageFromDirectoryFile(SupporterBooster);
    public IDalamudTextureWrap? RentSupporterBooster() => RentImageFromFile(SupporterBooster);
    public IDalamudTextureWrap GetSupporterTierOne() => GetImageFromDirectoryFile(SupporterT1);
    public IDalamudTextureWrap? RentSupporterTierOne() => RentImageFromFile(SupporterT1);
    public IDalamudTextureWrap GetSupporterTierTwo() => GetImageFromDirectoryFile(SupporterT2);
    public IDalamudTextureWrap? RentSupporterTierTwo() => RentImageFromFile(SupporterT2);
    public IDalamudTextureWrap GetSupporterTierThree() => GetImageFromDirectoryFile(SupporterT3);
    public IDalamudTextureWrap? RentSupporterTierThree() => RentImageFromFile(SupporterT3);
    public IDalamudTextureWrap GetSupporterTierFour() => GetImageFromDirectoryFile(OwnerT4);
    public IDalamudTextureWrap? RentSupporterTierFour() => RentImageFromFile(OwnerT4);

    // a rented version of the gagspeak logo cached throughout the instance.
    // ONLY TO BE USED IN PLUGIN WHERE IT MUST SERVE AS A REPLACEMENT FOR LOADED PROFILE IMAGES.
    private IDalamudTextureWrap? GagSpeakLogoNoRadial = null;

    public UiSharedService(ILogger<UiSharedService> logger, GagspeakMediator mediator,
        Dalamud.Localization localization, ApiController apiController,
        ClientConfigurationManager clientConfigurationManager,
        ServerConfigurationManager serverConfigurationManager,
        OnFrameworkService frameworkUtil, IpcManager ipcManager,
        IDalamudPluginInterface pluginInterface, ITextureProvider textureProvider)
        : base(logger, mediator)
    {
        _localization = localization;
        _apiController = apiController;
        _clientConfigs = clientConfigurationManager;
        _serverConfigs = serverConfigurationManager;
        _frameworkUtil = frameworkUtil;
        _ipcManager = ipcManager;
        _pi = pluginInterface;
        _textureProvider = textureProvider;

        _localization.SetupWithLangCode("en");
        _selectedComboItems = new(StringComparer.Ordinal);

        // A subscription from our mediator to see on each delayed framework if the IPC's are available from the IPC manager
        Mediator.Subscribe<DelayedFrameworkUpdateMessage>(this, (_) =>
        {
            _penumbraExists = IpcCallerPenumbra.APIAvailable;
            _glamourerExists = IpcCallerGlamourer.APIAvailable;
            _customizePlusExists = IpcCallerCustomize.APIAvailable;
            _moodlesExists = IpcCallerMoodles.APIAvailable;
        });

        // the font atlas for our UID display (make it the font from gagspeak probably unless this fits more)
        UidFont = _pi.UiBuilder.FontAtlas.NewDelegateFontHandle(e =>
        {
            e.OnPreBuild(tk => tk.AddDalamudAssetFont(Dalamud.DalamudAsset.NotoSansJpMedium, new()
            {
                SizePx = 35
            }));
        });

        // the font atlas for our game font
        GameFont = _pi.UiBuilder.FontAtlas.NewGameFontHandle(new(GameFontFamilyAndSize.Axis12));
        // the font atlas for our icon font
        IconFont = _pi.UiBuilder.IconFontFixedWidthHandle;

        // load the image on startup, so it always appears while we load other profiles. Ensure we dispose of it upon plugin close.
        Logger.LogDebug("Fetching Logo");
        Task.Run(async () => await frameworkUtil.RunOnFrameworkThread(() => { GagSpeakLogoNoRadial = RentImageFromFile(Logo256bgPath); }));
    }

    public ApiController ApiController => _apiController;   // a public accessible api controller for the plugin, pulled from the private field
    public IFontHandle GameFont { get; init; } // the current game font
    // public bool HasValidPenumbraModPath => !(_ipcManager.Penumbra.ModDirectory ?? string.Empty).IsNullOrEmpty() && Directory.Exists(_ipcManager.Penumbra.ModDirectory);
    public IFontHandle IconFont { get; init; } // the current icon font
    public UserData PlayerUserData => _apiController.GetConnectionDto().Result.User;
    public IFontHandle UidFont { get; init; } // the current UID font
    public IFontHandle GagspeakFont { get; init; } // the current Gagspeak font
    public Dictionary<ushort, string> WorldData => _frameworkUtil.WorldData.Value;
    public ulong PlayerLocalContentID => _frameworkUtil.GetPlayerLocalContentId();
    public string PlayerName => _frameworkUtil.GetPlayerName();
    public uint WorldId => _frameworkUtil.GetHomeWorldId(); // the homeworld ID of the current player
    public bool UseTheme => _useTheme;
    public Vector2 LastMainUIWindowPosition { get; set; } = Vector2.Zero;
    public Vector2 LastMainUIWindowSize { get; set; } = Vector2.Zero;

    protected override void Dispose(bool disposing)
    {
        if (!disposing) return;

        base.Dispose(disposing);
        GagSpeakLogoNoRadial?.Dispose();
        GagSpeakLogoNoRadial = null;
        UidFont.Dispose();
        GameFont.Dispose();
    }

    public IDalamudTextureWrap GetImageFromDirectoryFile(string path)
        => _textureProvider.GetFromFile(Path.Combine(_pi.AssemblyLocation.DirectoryName!, "Assets", path)).GetWrapOrEmpty();

    public IDalamudTextureWrap? RentImageFromFile(string path)
    {
        _sharedTextures = _textureProvider.GetFromFile(Path.Combine(_pi.AssemblyLocation.DirectoryName!, "Assets", path));
        if (_sharedTextures.GetWrapOrDefault() == null) return null;
        else return _sharedTextures.RentAsync().Result;
    }

    public IDalamudTextureWrap GetGameStatusIcon(uint IconId)
    {
        return _textureProvider.GetFromGameIcon(new GameIconLookup(IconId)).GetWrapOrEmpty();
    }

    public IDalamudTextureWrap LoadImage(byte[] imageData)
    {
        return _textureProvider.CreateFromImageAsync(imageData).Result;
    }

    /// <summary> 
    /// A helper function to attach a tooltip to a section in the UI currently hovered. 
    /// </summary>
    /// <param name="text"> The text to display in the tooltip. </param>
    public static void AttachToolTip(string text)
    {
        // if the item is currently hovered, with the ImGuiHoveredFlags set to allow when disabled
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
        {
            // begin the tooltip interface
            ImGui.BeginTooltip();
            // push the text wrap position to the font size times 35
            ImGui.PushTextWrapPos(ImGui.GetFontSize() * 35f);
            // we will then check to see if the text contains a tooltip seperator
            if (text.Contains(TooltipSeparator, StringComparison.Ordinal))
            {
                // if it does, we will split the text by the tooltip seperator
                var splitText = text.Split(TooltipSeparator, StringSplitOptions.RemoveEmptyEntries);
                // for each of the split text, we will display the text unformatted
                for (int i = 0; i < splitText.Length; i++)
                {
                    ImGui.TextUnformatted(splitText[i]);
                    if (i != splitText.Length - 1) ImGui.Separator();
                }
            }
            // otherwise, if it contains no tooltip seperator, then we will display the text unformatted
            else
            {
                ImGui.TextUnformatted(text);
            }
            // finally, pop the text wrap position and end the tooltip
            ImGui.PopTextWrapPos();
            ImGui.EndTooltip();
        }
    }

    /// <summary>
    /// A helper function for centering the next displayed window.
    /// </summary>
    /// <param name="width"> The width of the window. </param>
    /// <param name="height"> The height of the window. </param>
    /// <param name="cond"> The condition for the ImGuiWindow to be displayed . </param>
    public static void CenterNextWindow(float width, float height, ImGuiCond cond = ImGuiCond.None)
    {
        // get the center of the main viewport
        var center = ImGui.GetMainViewport().GetCenter();
        // then set the next window position to the center minus half the width and height
        ImGui.SetNextWindowPos(new Vector2(center.X - width / 2, center.Y - height / 2), cond);
    }

    /// <summary>
    /// A helper function for retrieving the proper color value given RGBA.
    /// </summary>
    /// <returns> The color formatted as a uint </returns>
    public static uint Color(byte r, byte g, byte b, byte a)
    { uint ret = a; ret <<= 8; ret += b; ret <<= 8; ret += g; ret <<= 8; ret += r; return ret; }

    /// <summary>
    /// A helper function for retrieving the proper color value given a vector4.
    /// </summary>
    /// <returns> The color formatted as a uint </returns>
    public static uint Color(Vector4 color)
    {
        uint ret = (byte)(color.W * 255);
        ret <<= 8;
        ret += (byte)(color.Z * 255);
        ret <<= 8;
        ret += (byte)(color.Y * 255);
        ret <<= 8;
        ret += (byte)(color.X * 255);
        return ret;
    }

    /// <summary>
    /// A helper function for displaying colortext. Keep in mind that this already exists in ottergui and we likely dont need it.
    /// </summary>
    public static void ColorText(string text, Vector4 color)
    {
        using var raiicolor = ImRaii.PushColor(ImGuiCol.Text, color);
        ImGui.TextUnformatted(text);
    }

    public static void ColorTextCentered(string text, Vector4 color)
    {
        float offset = (ImGui.GetContentRegionAvail().X - ImGui.CalcTextSize(text).X) / 2;
        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + offset);
        ColorText(text, color);
    }

    /// <summary>
    /// A helper function for wrapped text that is colored.  Keep in mind that this already exists in ottergui and we likely dont need it.
    /// </summary>
    public static void ColorTextWrapped(string text, Vector4 color)
    {
        using var raiicolor = ImRaii.PushColor(ImGuiCol.Text, color);
        TextWrapped(text);
    }

    /// <summary> 
    /// Helper function to see if the CTRL key is currently held down.
    ///  Keep in mind that this already exists in ottergui and we likely dont need it.
    /// </summary>
    /// <returns> True if CTRL is being pressed, false if not. </returns>
    public static bool CtrlPressed() => (GetKeyState(0xA2) & 0x8000) != 0 || (GetKeyState(0xA3) & 0x8000) != 0;

    /// <summary>
    /// Helper function to draw the outlined font in ImGui.
    /// Im not actually sure if this is in ottergui or not.
    /// </summary>
    public static void DrawOutlinedFont(string text, Vector4 fontColor, Vector4 outlineColor, int thickness)
    {
        var original = ImGui.GetCursorPos();

        using (ImRaii.PushColor(ImGuiCol.Text, outlineColor))
        {
            ImGui.SetCursorPos(original with { Y = original.Y - thickness });
            ImGui.TextUnformatted(text);
            ImGui.SetCursorPos(original with { X = original.X - thickness });
            ImGui.TextUnformatted(text);
            ImGui.SetCursorPos(original with { Y = original.Y + thickness });
            ImGui.TextUnformatted(text);
            ImGui.SetCursorPos(original with { X = original.X + thickness });
            ImGui.TextUnformatted(text);
            ImGui.SetCursorPos(original with { X = original.X - thickness, Y = original.Y - thickness });
            ImGui.TextUnformatted(text);
            ImGui.SetCursorPos(original with { X = original.X + thickness, Y = original.Y + thickness });
            ImGui.TextUnformatted(text);
            ImGui.SetCursorPos(original with { X = original.X - thickness, Y = original.Y + thickness });
            ImGui.TextUnformatted(text);
            ImGui.SetCursorPos(original with { X = original.X + thickness, Y = original.Y - thickness });
            ImGui.TextUnformatted(text);
        }

        using (ImRaii.PushColor(ImGuiCol.Text, fontColor))
        {
            ImGui.SetCursorPos(original);
            ImGui.TextUnformatted(text);
            ImGui.SetCursorPos(original);
            ImGui.TextUnformatted(text);
        }
    }

    public static void DrawOutlinedFont(ImDrawListPtr drawList, string text, Vector2 textPos, uint fontColor, uint outlineColor, int thickness)
    {
        drawList.AddText(textPos with { Y = textPos.Y - thickness },
            outlineColor, text);
        drawList.AddText(textPos with { X = textPos.X - thickness },
            outlineColor, text);
        drawList.AddText(textPos with { Y = textPos.Y + thickness },
            outlineColor, text);
        drawList.AddText(textPos with { X = textPos.X + thickness },
            outlineColor, text);
        drawList.AddText(new Vector2(textPos.X - thickness, textPos.Y - thickness),
            outlineColor, text);
        drawList.AddText(new Vector2(textPos.X + thickness, textPos.Y + thickness),
            outlineColor, text);
        drawList.AddText(new Vector2(textPos.X - thickness, textPos.Y + thickness),
            outlineColor, text);
        drawList.AddText(new Vector2(textPos.X + thickness, textPos.Y - thickness),
            outlineColor, text);

        drawList.AddText(textPos, fontColor, text);
        drawList.AddText(textPos, fontColor, text);
    }

    public static Vector4 GetBoolColor(bool input) => input ? ImGuiColors.ParsedGreen : ImGuiColors.DalamudRed;

    public Vector2 GetGlobalHelperScaleSize(Vector2 size) => size * ImGuiHelpers.GlobalScale;

    public float GetFontScalerFloat() => ImGuiHelpers.GlobalScale * (_pi.UiBuilder.DefaultFontSpec.SizePt / 12f);

    public float GetButtonSize(string text)
    {
        Vector2 vector2 = ImGui.CalcTextSize(text);
        return vector2.X + ImGui.GetStyle().FramePadding.X * 2f;
    }

    public float GetIconTextButtonSize(FontAwesomeIcon icon, string text)
    {
        Vector2 vector;
        using (IconFont.Push())
            vector = ImGui.CalcTextSize(icon.ToIconString());

        Vector2 vector2 = ImGui.CalcTextSize(text);
        float num = 3f * ImGuiHelpers.GlobalScale;
        return vector.X + vector2.X + ImGui.GetStyle().FramePadding.X * 2f + num;
    }

    /// <summary>
    /// Helper function for retrieving the nickname of a clients paired users.
    /// </summary>
    /// <param name="pairs"> The list of pairs the client has. </param>
    /// <returns> The string of nicknames for the pairs. </returns>
    public static string GetNicknames(List<Pair> pairs)
    {
        StringBuilder sb = new();
        sb.AppendLine(_nicknameStart);
        foreach (var entry in pairs)
        {
            var note = entry.GetNickname();
            if (note.IsNullOrEmpty()) continue;

            sb.Append(entry.UserData.UID).Append(":\"").Append(entry.GetNickname()).AppendLine("\"");
        }
        sb.AppendLine(_nicknameEnd);

        return sb.ToString();
    }

    public static float GetWindowContentRegionWidth() => ImGui.GetWindowContentRegionMax().X - ImGui.GetWindowContentRegionMin().X;

    public bool DrawScaledCenterButtonImage(string ID, Vector2 buttonSize, Vector4 buttonColor,
        Vector2 imageSize, IDalamudTextureWrap image)
    {
        // push ID for the function
        ImGui.PushID(ID);
        // grab the current cursor position
        var InitialPos = ImGui.GetCursorPos();
        // calculate the difference in height between the button and the image
        var heightDiff = buttonSize.Y - imageSize.Y;
        // draw out the button centered
        if (UtilsExtensions.CenteredLineWidths.TryGetValue(ID, out var dims))
        {
            ImGui.SetCursorPosX(ImGui.GetContentRegionAvail().X / 2 - dims / 2);
        }
        var oldCur = ImGui.GetCursorPosX();
        bool result = ImGui.Button(string.Empty, buttonSize);
        //Logger.LogTrace("Result of button: {result}", result);
        ImGui.SameLine(0, 0);
        UtilsExtensions.CenteredLineWidths[ID] = ImGui.GetCursorPosX() - oldCur;
        ImGui.Dummy(Vector2.Zero);
        // now go back up to the inital position, then step down by the height difference/2
        ImGui.SetCursorPosY(InitialPos.Y + heightDiff / 2);
        UtilsExtensions.ImGuiLineCentered($"###CenterImage{ID}", () =>
        {
            ImGui.Image(image.ImGuiHandle, imageSize, Vector2.Zero, Vector2.One, buttonColor);
        });
        ImGui.PopID();
        // return the result
        return result;
    }

    /// <summary> The additional param for an ID is optional. if not provided, the id will be the text. </summary>
    public bool IconButton(FontAwesomeIcon icon, float? height = null, string? id = null, bool disabled = false, bool inPopup = false)
    {
        using var dis = ImRaii.PushStyle(ImGuiStyleVar.Alpha, disabled ? 0.5f : 1f);
        int num = 0;
        if (inPopup)
        {
            ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(1.0f, 1.0f, 1.0f, 0.0f));
            num++;
        }

        string text = icon.ToIconString();

        ImGui.PushID((id == null) ? icon.ToIconString() : id + icon.ToIconString());
        Vector2 vector;
        using (IconFont.Push())
            vector = ImGui.CalcTextSize(text);
        ImDrawListPtr windowDrawList = ImGui.GetWindowDrawList();
        Vector2 cursorScreenPos = ImGui.GetCursorScreenPos();
        float x = vector.X + ImGui.GetStyle().FramePadding.X * 2f;
        float frameHeight = height ?? ImGui.GetFrameHeight();
        bool result = ImGui.Button(string.Empty, new Vector2(x, frameHeight));
        Vector2 pos = new Vector2(cursorScreenPos.X + ImGui.GetStyle().FramePadding.X,
            cursorScreenPos.Y + (height ?? ImGui.GetFrameHeight()) / 2f - (vector.Y / 2f));
        using (IconFont.Push())
            windowDrawList.AddText(pos, ImGui.GetColorU32(ImGuiCol.Text), text);
        ImGui.PopID();

        if (num > 0)
        {
            ImGui.PopStyleColor(num);
        }
        return result && !disabled;
    }

    private bool IconTextButtonInternal(FontAwesomeIcon icon, string text, Vector4? defaultColor = null, float? width = null, bool disabled = false, string id = "")
    {
        using var dis = ImRaii.PushStyle(ImGuiStyleVar.Alpha, disabled ? 0.5f : 1f);
        int num = 0;
        if (defaultColor.HasValue)
        {
            ImGui.PushStyleColor(ImGuiCol.Button, defaultColor.Value);
            num++;
        }

        ImGui.PushID(text + "##" + id);
        Vector2 vector;
        using (IconFont.Push())
            vector = ImGui.CalcTextSize(icon.ToIconString());
        Vector2 vector2 = ImGui.CalcTextSize(text);
        ImDrawListPtr windowDrawList = ImGui.GetWindowDrawList();
        Vector2 cursorScreenPos = ImGui.GetCursorScreenPos();
        float num2 = 3f * ImGuiHelpers.GlobalScale;
        float x = width ?? vector.X + vector2.X + ImGui.GetStyle().FramePadding.X * 2f + num2;
        float frameHeight = ImGui.GetFrameHeight();
        bool result = ImGui.Button(string.Empty, new Vector2(x, frameHeight));
        Vector2 pos = new Vector2(cursorScreenPos.X + ImGui.GetStyle().FramePadding.X, cursorScreenPos.Y + ImGui.GetStyle().FramePadding.Y);
        using (IconFont.Push())
            windowDrawList.AddText(pos, ImGui.GetColorU32(ImGuiCol.Text), icon.ToIconString());
        Vector2 pos2 = new Vector2(pos.X + vector.X + num2, cursorScreenPos.Y + ImGui.GetStyle().FramePadding.Y);
        windowDrawList.AddText(pos2, ImGui.GetColorU32(ImGuiCol.Text), text);
        ImGui.PopID();
        if (num > 0)
        {
            ImGui.PopStyleColor(num);
        }
        dis.Pop();

        return result && !disabled;
    }

    public bool IconTextButton(FontAwesomeIcon icon, string text, float? width = null, bool isInPopup = false, bool disabled = false, string id = "Identifier")
    {
        return IconTextButtonInternal(icon, text,
            isInPopup ? new Vector4(1.0f, 1.0f, 1.0f, 0.0f) : null,
            width <= 0 ? null : width,
            disabled, id);
    }

    private bool IconSliderFloatInternal(string id, FontAwesomeIcon icon, string label, ref float valueRef, float min,
        float max, Vector4? defaultColor = null, float? width = null, bool disabled = false, string format = "%.1f")
    {
        using var dis = ImRaii.PushStyle(ImGuiStyleVar.Alpha, disabled ? 0.5f : 1f);
        int num = 0;
        // Disable if issues, tends to be culpret
        if (defaultColor.HasValue)
        {
            ImGui.PushStyleColor(ImGuiCol.FrameBg, defaultColor.Value);
            num++;
        }

        ImGui.PushID(id);
        Vector2 vector;
        using (IconFont.Push())
            vector = ImGui.CalcTextSize(icon.ToIconString());
        Vector2 vector2 = ImGui.CalcTextSize(label);
        ImDrawListPtr windowDrawList = ImGui.GetWindowDrawList();
        Vector2 cursorScreenPos = ImGui.GetCursorScreenPos();
        float num2 = 3f * ImGuiHelpers.GlobalScale;
        float x = width ?? vector.X + vector2.X + ImGui.GetStyle().FramePadding.X * 2f + num2;
        float frameHeight = ImGui.GetFrameHeight();
        ImGui.SetCursorPosX(vector.X + ImGui.GetStyle().FramePadding.X * 2f);
        ImGui.SetNextItemWidth(x - vector.X - num2 * 4); // idk why this works, it probably doesnt on different scaling. Idfk. Look into later.
        bool result = ImGui.SliderFloat(label + "##" + id, ref valueRef, min, max, format);

        Vector2 pos = new Vector2(cursorScreenPos.X + ImGui.GetStyle().FramePadding.X, cursorScreenPos.Y + ImGui.GetStyle().FramePadding.Y);
        using (IconFont.Push())
            windowDrawList.AddText(pos, ImGui.GetColorU32(ImGuiCol.Text), icon.ToIconString());
        ImGui.PopID();
        if (num > 0)
        {
            ImGui.PopStyleColor(num);
        }
        dis.Pop();

        return result && !disabled;
    }

    public bool IconSliderFloat(string id, FontAwesomeIcon icon, string label, ref float valueRef,
        float min, float max, float? width = null, bool isInPopup = false, bool disabled = false)
    {
        return IconSliderFloatInternal(id, icon, label, ref valueRef, min, max,
            isInPopup ? new Vector4(1.0f, 1.0f, 1.0f, 0.1f) : null,
            width <= 0 ? null : width,
            disabled);
    }

    private bool IconInputTextInternal(string id, FontAwesomeIcon icon, string label, string hint, ref string inputStr,
        uint maxLength, Vector4? defaultColor = null, float? width = null, bool disabled = false)
    {
        using var dis = ImRaii.PushStyle(ImGuiStyleVar.Alpha, disabled ? 0.5f : 1f);
        int num = 0;
        // Disable if issues, tends to be culpret
        if (defaultColor.HasValue)
        {
            ImGui.PushStyleColor(ImGuiCol.FrameBg, defaultColor.Value);
            num++;
        }

        ImGui.PushID(id);
        Vector2 vector;
        using (IconFont.Push())
            vector = ImGui.CalcTextSize(icon.ToIconString());
        Vector2 vector2 = ImGui.CalcTextSize(label);
        ImDrawListPtr windowDrawList = ImGui.GetWindowDrawList();
        Vector2 cursorScreenPos = ImGui.GetCursorScreenPos();
        float num2 = 3f * ImGuiHelpers.GlobalScale;
        float x = width ?? vector.X + vector2.X + ImGui.GetStyle().FramePadding.X * 2f + num2;
        float frameHeight = ImGui.GetFrameHeight();
        ImGui.SetCursorPosX(vector.X + ImGui.GetStyle().FramePadding.X * 2f);
        ImGui.SetNextItemWidth(x - vector.X - num2 * 4); // idk why this works, it probably doesnt on different scaling. Idfk. Look into later.
        bool result = ImGui.InputTextWithHint(label, hint, ref inputStr, maxLength, ImGuiInputTextFlags.EnterReturnsTrue);

        Vector2 pos = new Vector2(cursorScreenPos.X + ImGui.GetStyle().FramePadding.X, cursorScreenPos.Y + ImGui.GetStyle().FramePadding.Y);
        using (IconFont.Push())
            windowDrawList.AddText(pos, ImGui.GetColorU32(ImGuiCol.Text), icon.ToIconString());
        ImGui.PopID();
        if (num > 0)
        {
            ImGui.PopStyleColor(num);
        }
        dis.Pop();

        return result && !disabled;
    }

    public bool IconInputText(string id, FontAwesomeIcon icon, string label, string hint, ref string inputStr,
        uint maxLength, float? width = null, bool isInPopup = false, bool disabled = false)
    {
        return IconInputTextInternal(id, icon, label, hint, ref inputStr, maxLength,
            isInPopup ? new Vector4(1.0f, 1.0f, 1.0f, 0.1f) : null,
            width <= 0 ? null : width,
            disabled);
    }


    /// <summary> Cleans sender string from the chatlog before processing, so it stays a valid player sender string. </summary>
    /// <param name="senderName"> The original uncleaned sender name string </param>
    /// <returns> The cleaned sender name string </returns>
    public static string CleanSenderName(string senderName)
    {
        string[] senderStrings = SplitCamelCase(RemoveSpecialSymbols(senderName)).Split(" ");
        string playerSender = senderStrings.Length == 1 ? senderStrings[0] : senderStrings.Length == 2 ?
            (senderStrings[0] + " " + senderStrings[1]) :
            (senderStrings[0] + " " + senderStrings[2]);
        return playerSender;
    }

    /// <summary> function for splitting camel case </summary>
    /// <param name="input"> the input string </param>
    /// <returns> The string with camel case split </returns>
    public static string SplitCamelCase(string input)
    {
        return Regex.Replace(input, "([A-Z])", " $1", RegexOptions.Compiled).Trim();
    }

    /// <summary> function for removing special symbols </summary>
    /// <returns> The string with special symbols removed </returns>
    public static string RemoveSpecialSymbols(string value)
    {
        Regex rgx = new Regex(@"[^\u4e00-\u9fff a-zA-Z:/._\ '-]");
        //      [^...] matches any character not in the brackets.
        //      a-z matches any lowercase letter.
        //      A-Z matches any uppercase letter.
        //      :/._\ '- matches a colon, slash, period, underscore, space, or hyphen, or apostrophe.
        //      \u4e00-\u9fff matches any Chinese character.
        return rgx.Replace(value, "");
    }


    /// <summary> Validates a password </summary>
    public bool ValidatePassword(string password)
    {
        Logger.LogDebug($"Validating Password {password}");
        return !string.IsNullOrWhiteSpace(password) && password.Length <= 20 && !password.Contains(" ");
    }

    /// <summary> Validates a 4 digit combination </summary>
    public bool ValidateCombination(string combination)
    {
        Logger.LogDebug($"Validating Combination {combination}");
        return int.TryParse(combination, out _) && combination.Length == 4;
    }

    /// <summary> Helper function to convert a timespan object into a string with format XdXhXmXs. </summary>
    public string TimeSpanToString(TimeSpan timeSpan)
    {
        return $"{timeSpan.Days}d{timeSpan.Hours}h{timeSpan.Minutes}m{timeSpan.Seconds}s";
    }

    public static DateTimeOffset GetEndTimeUTC(string input)
    {
        // Match days, hours, minutes, and seconds in the input string
        var match = Regex.Match(input, @"^(?:(\d+)d)?(?:(\d+)h)?(?:(\d+)m)?(?:(\d+)s)?$");

        if (match.Success)
        {
            // Parse days, hours, minutes, and seconds 
            int.TryParse(match.Groups[1].Value, out int days);
            int.TryParse(match.Groups[2].Value, out int hours);
            int.TryParse(match.Groups[3].Value, out int minutes);
            int.TryParse(match.Groups[4].Value, out int seconds);
            // Create a TimeSpan from the parsed values
            TimeSpan duration = new TimeSpan(days, hours, minutes, seconds);
            // Add the duration to the current DateTime to get a DateTimeOffset
            return DateTimeOffset.UtcNow.Add(duration);
        }

        // "Invalid duration format: {input}, returning datetimeUTCNow");
        return DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// Helper function to try and take the string version of timespan and convert it back to a timespan object.
    /// </summary>
    /// <param name="input"> the string variant of the timespan </param>
    /// <param name="result"> the timespan equivalent of the string passed in </param>
    /// <returns> if the parse was successful or not. </returns>
    public bool TryParseTimeSpan(string input, out TimeSpan result)
    {
        result = TimeSpan.Zero;
        var regex = new Regex(@"(?:(\d+)d)?(?:(\d+)h)?(?:(\d+)m)?(?:(\d+)s)?");
        var match = regex.Match(input);

        if (!match.Success)
        {
            return false;
        }

        int days = match.Groups[1].Success ? int.Parse(match.Groups[1].Value) : 0;
        int hours = match.Groups[2].Success ? int.Parse(match.Groups[2].Value) : 0;
        int minutes = match.Groups[3].Success ? int.Parse(match.Groups[3].Value) : 0;
        int seconds = match.Groups[4].Success ? int.Parse(match.Groups[4].Value) : 0;

        result = new TimeSpan(days, hours, minutes, seconds);
        return true;
    }


    public static bool IsDirectoryWritable(string dirPath, bool throwIfFails = false)
    {
        try
        {
            using FileStream fs = File.Create(
                       Path.Combine(
                           dirPath,
                           Path.GetRandomFileName()
                       ),
                       1,
                       FileOptions.DeleteOnClose);
            return true;
        }
        catch
        {
            if (throwIfFails)
                throw;

            return false;
        }
    }

    public static void SetScaledWindowSize(float width, bool centerWindow = true)
    {
        var newLineHeight = ImGui.GetCursorPosY();
        ImGui.NewLine();
        newLineHeight = ImGui.GetCursorPosY() - newLineHeight;
        var y = ImGui.GetCursorPos().Y + ImGui.GetWindowContentRegionMin().Y - newLineHeight * 2 - ImGui.GetStyle().ItemSpacing.Y;

        SetScaledWindowSize(width, y, centerWindow, scaledHeight: true);
    }

    public static void SetScaledWindowSize(float width, float height, bool centerWindow = true, bool scaledHeight = false)
    {
        ImGui.SameLine();
        var x = width * ImGuiHelpers.GlobalScale;
        var y = scaledHeight ? height : height * ImGuiHelpers.GlobalScale;

        if (centerWindow)
        {
            CenterWindow(x, y);
        }

        ImGui.SetWindowSize(new Vector2(x, y));
    }

    public static bool ShiftPressed() => (GetKeyState(0xA1) & 0x8000) != 0 || (GetKeyState(0xA0) & 0x8000) != 0;

    public static bool RightMouseButtonDown() => (GetKeyState(0x02) & 0x8000) != 0;

    public static bool MiddleMouseButtonDown() => (GetKeyState(0x04) & 0x8000) != 0;

    public void ForceSendEnterPress() => User32.keybd_event(0x0D, 0, 0, 0);

    public static void CopyableDisplayText(string text, string tooltip = "Click to copy")
    {
        // then when the item is clicked, copy it to clipboard so we can share with others
        if (ImGui.IsItemClicked())
        {
            ImGui.SetClipboardText(text);
        }
        UiSharedService.AttachToolTip(tooltip);
    }


    public static void TextWrapped(string text)
    {
        ImGui.PushTextWrapPos(0);
        ImGui.TextUnformatted(text);
        ImGui.PopTextWrapPos();
    }

    public static Vector4 UploadColor((long, long) data) => data.Item1 == 0 ? ImGuiColors.DalamudGrey :
        data.Item1 == data.Item2 ? ImGuiColors.ParsedGreen : ImGuiColors.DalamudYellow;

    public bool ApplyNicknamesFromClipboard(string notes, bool overwrite)
    {
        var splitNicknames = notes.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries).ToList();
        var splitNicknamesStart = splitNicknames.FirstOrDefault();
        var splitNicknamesEnd = splitNicknames.LastOrDefault();
        if (!string.Equals(splitNicknamesStart, _nicknameStart, StringComparison.Ordinal) || !string.Equals(splitNicknamesEnd, _nicknameEnd, StringComparison.Ordinal))
        {
            return false;
        }

        splitNicknames.RemoveAll(n => string.Equals(n, _nicknameStart, StringComparison.Ordinal) || string.Equals(n, _nicknameEnd, StringComparison.Ordinal));

        foreach (var note in splitNicknames)
        {
            try
            {
                var splittedEntry = note.Split(":", 2, StringSplitOptions.RemoveEmptyEntries);
                var uid = splittedEntry[0];
                var comment = splittedEntry[1].Trim('"');
                if (_serverConfigs.GetNicknameForUid(uid) != null && !overwrite) continue;
                _serverConfigs.SetNicknameForUid(uid, comment);
            }
            catch
            {
                Logger.LogWarning("Could not parse {note}", note);
            }
        }

        _serverConfigs.SaveNicknames();

        return true;
    }

    public void BigText(string text, Vector4? color = null)
    {
        FontText(text, UidFont, color);
    }

    private static int FindWrapPosition(string text, float wrapWidth)
    {
        float currentWidth = 0;
        int lastSpacePos = -1;
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            currentWidth += ImGui.CalcTextSize(c.ToString()).X;
            if (char.IsWhiteSpace(c))
            {
                lastSpacePos = i;
            }
            if (currentWidth > wrapWidth)
            {
                return lastSpacePos >= 0 ? lastSpacePos : i;
            }
        }
        return -1;
    }

    private static string FormatTextForDisplay(string text, float wrapWidth)
    {
        // Normalize newlines for processing
        text = text.Replace("\r\n", "\n");
        var lines = text.Split('\n').ToList();

        bool textModified = false;

        // Traverse each line to check if it exceeds the wrap width
        for (int i = 0; i < lines.Count; i++)
        {
            float lineWidth = ImGui.CalcTextSize(lines[i]).X;

            while (lineWidth > wrapWidth)
            {
                // Find where to break the line
                int wrapPos = FindWrapPosition(lines[i], wrapWidth);
                if (wrapPos >= 0)
                {
                    // Insert a newline at the wrap position
                    string part1 = lines[i].Substring(0, wrapPos);
                    string part2 = lines[i].Substring(wrapPos).TrimStart();
                    lines[i] = part1;
                    lines.Insert(i + 1, part2);
                    textModified = true;
                    lineWidth = ImGui.CalcTextSize(part2).X;
                }
                else
                {
                    break;
                }
            }
        }

        // Join lines with \n for internal representation
        return string.Join("\n", lines);
    }

    private static unsafe int TextEditCallback(ImGuiInputTextCallbackData* data, float wrapWidth)
    {
        string text = Marshal.PtrToStringAnsi((IntPtr)data->Buf, data->BufTextLen);

        // Normalize newlines for processing
        text = text.Replace("\r\n", "\n");
        var lines = text.Split('\n').ToList();

        bool textModified = false;

        // Traverse each line to check if it exceeds the wrap width
        for (int i = 0; i < lines.Count; i++)
        {
            float lineWidth = ImGui.CalcTextSize(lines[i]).X;

            // Skip wrapping if this line ends with \r (i.e., it's a true newline)
            if (lines[i].EndsWith("\r"))
            {
                continue;
            }

            while (lineWidth > wrapWidth)
            {
                // Find where to break the line
                int wrapPos = FindWrapPosition(lines[i], wrapWidth);
                if (wrapPos >= 0)
                {
                    // Insert a newline at the wrap position
                    string part1 = lines[i].Substring(0, wrapPos);
                    string part2 = lines[i].Substring(wrapPos).TrimStart();
                    lines[i] = part1;
                    lines.Insert(i + 1, part2);
                    textModified = true;
                    lineWidth = ImGui.CalcTextSize(part2).X;
                }
                else
                {
                    break;
                }
            }
        }

        // Merge lines back to the buffer
        if (textModified)
        {
            string newText = string.Join("\n", lines); // Use \n for internal representation

            byte[] newTextBytes = Encoding.UTF8.GetBytes(newText.PadRight(data->BufSize, '\0'));
            Marshal.Copy(newTextBytes, 0, (IntPtr)data->Buf, newTextBytes.Length);
            data->BufTextLen = newText.Length;
            data->BufDirty = 1;
            data->CursorPos = Math.Min(data->CursorPos, data->BufTextLen);
        }

        return 0;
    }

    public unsafe static bool InputTextWrapMultiline(string id, ref string text, uint maxLength = 500, int lineHeight = 2, float? width = null)
    {
        float wrapWidth = width ?? ImGui.GetContentRegionAvail().X; // Determine wrap width

        // Format text for display
        text = FormatTextForDisplay(text, wrapWidth);

        bool result = ImGui.InputTextMultiline(id, ref text, maxLength,
             new(width ?? ImGui.GetContentRegionAvail().X, ImGui.GetTextLineHeightWithSpacing() * lineHeight), // Expand height calculation
             ImGuiInputTextFlags.CallbackEdit | ImGuiInputTextFlags.NoHorizontalScroll, // Flag settings
             (data) => { return TextEditCallback(data, wrapWidth); });

        // Restore \r\n for display consistency
        text = text.Replace("\n", "");

        return result;
    }

    public void BooleanToColoredIcon(bool value, bool inline = true,
        FontAwesomeIcon trueIcon = FontAwesomeIcon.Check, FontAwesomeIcon falseIcon = FontAwesomeIcon.Times, Vector4 colorTrue = default, Vector4 colorFalse = default)
    {
        using var colorgreen = ImRaii.PushColor(ImGuiCol.Text, (colorTrue == default) ? ImGuiColors.HealerGreen : colorTrue, value);
        using var colorred = ImRaii.PushColor(ImGuiCol.Text, (colorFalse == default) ? ImGuiColors.DalamudRed : colorFalse, !value);

        if (inline) ImGui.SameLine();

        if (value)
        {
            IconText(trueIcon);
        }
        else
        {
            IconText(falseIcon);
        }
    }

    public void DrawCombo<T>(string comboName, float width, IEnumerable<T> comboItems, Func<T, string> toName,
        Action<T?>? onSelected = null, T? initialSelectedItem = default, bool shouldShowLabel = true,
        ImGuiComboFlags flags = ImGuiComboFlags.None, string defaultPreviewText = "Nothing Selected..")
    {
        string comboLabel = shouldShowLabel ? $"{comboName}##{comboName}" : $"##{comboName}";
        if (!comboItems.Any())
        {
            ImGui.SetNextItemWidth(width);
            if (ImGui.BeginCombo(comboLabel, defaultPreviewText, flags))
            {
                ImGui.EndCombo();
            }
            return;
        }

        if (!_selectedComboItems.TryGetValue(comboName, out var selectedItem) && selectedItem == null)
        {
            if (!EqualityComparer<T>.Default.Equals(initialSelectedItem, default))
            {
                selectedItem = initialSelectedItem;
                _selectedComboItems[comboName] = selectedItem!;
                if (!EqualityComparer<T>.Default.Equals(initialSelectedItem, default))
                    onSelected?.Invoke(initialSelectedItem);
            }
            else
            {
                selectedItem = comboItems.First();
                _selectedComboItems[comboName] = selectedItem!;
            }
        }

        string displayText = selectedItem == null ? defaultPreviewText : toName((T)selectedItem!);

        ImGui.SetNextItemWidth(width);
        if (ImGui.BeginCombo(comboLabel, displayText, flags))
        {
            foreach (var item in comboItems)
            {
                bool isSelected = EqualityComparer<T>.Default.Equals(item, (T?)selectedItem);
                if (ImGui.Selectable(toName(item), isSelected))
                {
                    _selectedComboItems[comboName] = item!;
                    onSelected?.Invoke(item!);
                }
            }

            ImGui.EndCombo();
        }
        // Check if the item was right-clicked. If so, reset to default value.
        if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
        {
            Logger.LogTrace("Right-clicked on {comboName}. Resetting to default value.", comboName);
            selectedItem = comboItems.First();
            _selectedComboItems[comboName] = selectedItem!;
            onSelected?.Invoke((T)selectedItem!);
        }
        return;
    }

    public void DrawComboSearchable<T>(string comboName, float width, ref string searchString, IEnumerable<T> comboItems,
        Func<T, string> toName, bool showLabel = true, Action<T?>? onSelected = null, T? initialSelectedItem = default,
        string defaultPreviewText = "No Items Available...", ImGuiComboFlags flags = ImGuiComboFlags.None)
    {
        try
        {
            // Return default if there are no items to display in the combo box.
            string comboLabel = showLabel ? $"{comboName}##{comboName}" : $"##{comboName}";
            if (!comboItems.Any())
            {
                ImGui.SetNextItemWidth(width);
                if (ImGui.BeginCombo(comboLabel, defaultPreviewText, flags))
                {
                    ImGui.EndCombo();
                }
                return;
            }

            // try to get currently selected item from a dictionary storing selections for each combo box.
            if (!_selectedComboItems.TryGetValue(comboName, out var selectedItem) && selectedItem == null)
            {
                if (!EqualityComparer<T>.Default.Equals(initialSelectedItem, default))
                {
                    selectedItem = initialSelectedItem;
                    _selectedComboItems[comboName] = selectedItem!;
                    if (!EqualityComparer<T>.Default.Equals(initialSelectedItem, default))
                        onSelected?.Invoke(initialSelectedItem);
                }
                else
                {
                    selectedItem = comboItems.First();
                    _selectedComboItems[comboName] = selectedItem!;
                }
            }

            string displayText = selectedItem == null ? defaultPreviewText : toName((T)selectedItem!);

            ImGui.SetNextItemWidth(width);
            if (ImGui.BeginCombo(comboLabel, displayText, flags))
            {
                // Search filter
                ImGui.SetNextItemWidth(width);
                ImGui.InputTextWithHint("##filter", "Filter...", ref searchString, 255);
                var searchText = searchString.ToLowerInvariant();

                var filteredItems = string.IsNullOrEmpty(searchText)
                    ? comboItems
                    : comboItems.Where(item => toName(item).ToLowerInvariant().Contains(searchText));

                // display filtered content.
                foreach (var item in filteredItems)
                {
                    bool isSelected = EqualityComparer<T>.Default.Equals(item, (T?)selectedItem);
                    if (ImGui.Selectable(toName(item), isSelected))
                    {
                        Logger.LogTrace("Selected {item} from {comboName}", toName(item), comboName);
                        _selectedComboItems[comboName] = item!;
                        onSelected?.Invoke(item!);
                    }
                }
                ImGui.EndCombo();
            }
            // Check if the item was right-clicked. If so, reset to default value.
            if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
            {
                Logger.LogTrace("Right-clicked on {comboName}. Resetting to default value.", comboName);
                selectedItem = comboItems.First();
                _selectedComboItems[comboName] = selectedItem!;
                onSelected?.Invoke((T)selectedItem!);
            }
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error in DrawComboSearchable");
        }
    }

    public void DrawTimeSpanCombo(string label, TimeSpan patternMaxDuration, ref TimeSpan patternDuration, float width, string format = "hh\\:mm\\:ss", bool showLabel = true)
    {
        if (patternDuration > patternMaxDuration) patternDuration = patternMaxDuration;

        string maxDurationFormatted = patternMaxDuration.ToString(format);
        string patternDurationFormatted = patternDuration.ToString(format);

        // Button to open popup
        var pos = ImGui.GetCursorScreenPos();
        if (ImGui.Button($"{patternDurationFormatted} / {maxDurationFormatted}##TimeSpanCombo-{label}", new Vector2(width, ImGui.GetFrameHeight())))
        {
            ImGui.SetNextWindowPos(new Vector2(pos.X, pos.Y + ImGui.GetFrameHeight()));
            ImGui.OpenPopup($"TimeSpanPopup-{label}");
        }
        // just to the right of it, aligned with the button, display the label
        if (showLabel)
        {
            ImUtf8.SameLineInner();
            ImGui.TextUnformatted(label);
        }

        // Popup
        if (ImGui.BeginPopup($"TimeSpanPopup-{label}"))
        {
            DrawTimeSpanUI(ref patternDuration, patternMaxDuration, width, format);
            ImGui.EndPopup();
        }
    }

    private void DrawTimeSpanUI(ref TimeSpan patternDuration, TimeSpan maxDuration, float width, string format)
    {
        var totalColumns = GetColumnCountFromFormat(format);
        float extraPadding = ImGui.GetStyle().ItemSpacing.X;

        Vector2 patternHourTextSize;
        Vector2 patternMinuteTextSize;
        Vector2 patternSecondTextSize;
        Vector2 patternMillisecondTextSize;

        using (UidFont.Push())
        {
            patternHourTextSize = ImGui.CalcTextSize($"{patternDuration.Hours:00}h");
            patternMinuteTextSize = ImGui.CalcTextSize($"{patternDuration.Minutes:00}m");
            patternSecondTextSize = ImGui.CalcTextSize($"{patternDuration.Seconds:00}s");
            patternMillisecondTextSize = ImGui.CalcTextSize($"{patternDuration.Milliseconds:000}ms");
        }

        // Specify the number of columns. In this case, 2 for minutes and seconds.
        if (ImGui.BeginTable("TimeDurationTable", totalColumns)) // 3 columns for hours, minutes, seconds
        {
            // Setup columns based on the format
            if (format.Contains("hh")) ImGui.TableSetupColumn("##Hours", ImGuiTableColumnFlags.WidthFixed, patternHourTextSize.X + totalColumns + 1);
            if (format.Contains("mm")) ImGui.TableSetupColumn("##Minutes", ImGuiTableColumnFlags.WidthFixed, patternMinuteTextSize.X + totalColumns + 1);
            if (format.Contains("ss")) ImGui.TableSetupColumn("##Seconds", ImGuiTableColumnFlags.WidthFixed, patternSecondTextSize.X + totalColumns + 1);
            if (format.Contains("fff")) ImGui.TableSetupColumn("##Milliseconds", ImGuiTableColumnFlags.WidthFixed, patternMillisecondTextSize.X + totalColumns + 1);
            ImGui.TableNextRow();

            // Draw components based on the format
            if (format.Contains("hh"))
            {
                ImGui.TableNextColumn();
                DrawTimeComponentUI(ref patternDuration, maxDuration, "h");
            }
            if (format.Contains("mm"))
            {
                ImGui.TableNextColumn();
                DrawTimeComponentUI(ref patternDuration, maxDuration, "m");
            }
            if (format.Contains("ss"))
            {
                ImGui.TableNextColumn();
                DrawTimeComponentUI(ref patternDuration, maxDuration, "s");
            }
            if (format.Contains("fff"))
            {
                ImGui.TableNextColumn();
                DrawTimeComponentUI(ref patternDuration, maxDuration, "ms");
            }

            ImGui.EndTable();
        }
    }

    private void DrawTimeComponentUI(ref TimeSpan duration, TimeSpan maxDuration, string suffix)
    {
        string prevValue = suffix switch
        {
            "h" => $"{Math.Max(0, (duration.Hours - 1)):00}",
            "m" => $"{Math.Max(0, (duration.Minutes - 1)):00}",
            "s" => $"{Math.Max(0, (duration.Seconds - 1)):00}",
            "ms" => $"{Math.Max(0, (duration.Milliseconds - 10)):000}",
            _ => $"UNK"
        };

        string currentValue = suffix switch
        {
            "h" => $"{duration.Hours:00}h",
            "m" => $"{duration.Minutes:00}m",
            "s" => $"{duration.Seconds:00}s",
            "ms" => $"{duration.Milliseconds:000}ms",
            _ => $"UNK"
        };

        string nextValue = suffix switch
        {
            "h" => $"{Math.Min(maxDuration.Hours, (duration.Hours + 1)):00}",
            "m" => $"{Math.Min(maxDuration.Minutes, (duration.Minutes + 1)):00}",
            "s" => $"{Math.Min(maxDuration.Seconds, (duration.Seconds + 1)):00}",
            "ms" => $"{Math.Min(maxDuration.Milliseconds, (duration.Milliseconds + 10)):000}",
            _ => $"UNK"
        };

        float CurrentValBigSize;
        using (UidFont.Push())
        {
            CurrentValBigSize = ImGui.CalcTextSize(currentValue).X;
        }
        var offset = (CurrentValBigSize - ImGui.CalcTextSize(prevValue).X) / 2;
        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + offset);
        ImGui.TextDisabled(prevValue); // Previous value (centered)
        ImGui.SetCursorPosY(ImGui.GetCursorPosY() - 5f);
        BigText(currentValue);

        // adjust the value with the mouse wheel
        if (ImGui.IsItemHovered() && ImGui.GetIO().MouseWheel != 0)
        {
            int hours = duration.Hours;
            int minutes = duration.Minutes;
            int seconds = duration.Seconds;
            int milliseconds = duration.Milliseconds;

            int delta = -(int)ImGui.GetIO().MouseWheel;
            if (suffix == "h") { hours += delta; }
            if (suffix == "m") { minutes += delta; }
            if (suffix == "s") { seconds += delta; }
            if (suffix == "ms") { milliseconds += delta * 10; }
            // Rollover and clamp logic
            if (milliseconds < 0) { milliseconds += 1000; seconds--; }
            if (milliseconds > 999) { milliseconds -= 1000; seconds++; }
            if (seconds < 0) { seconds += 60; minutes--; }
            if (seconds > 59) { seconds -= 60; minutes++; }
            if (minutes < 0) { minutes += 60; hours--; }
            if (minutes > 59) { minutes -= 60; hours++; }

            hours = Math.Clamp(hours, 0, maxDuration.Hours);
            minutes = Math.Clamp(minutes, 0, (hours == maxDuration.Hours ? maxDuration.Minutes : 59));
            seconds = Math.Clamp(seconds, 0, (minutes == (hours == maxDuration.Hours ? maxDuration.Minutes : 59) ? maxDuration.Seconds : 59));
            milliseconds = Math.Clamp(milliseconds, 0, (seconds == (minutes == (hours == maxDuration.Hours ? maxDuration.Minutes : 59) ? maxDuration.Seconds : 59) ? maxDuration.Milliseconds : 999));

            // update the duration
            duration = new TimeSpan(0, hours, minutes, seconds, milliseconds);

            Logger.LogDebug($"Duration changed to {duration.ToString("hh\\:mm\\:ss\\:fff")}");
        }
        ImGui.SetCursorPosY(ImGui.GetCursorPosY() - 5f);
        var offset2 = (CurrentValBigSize - ImGui.CalcTextSize(prevValue).X) / 2;
        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + offset2);
        ImGui.TextDisabled(nextValue); // Previous value (centered)
    }
    private int GetColumnCountFromFormat(string format)
    {
        int columnCount = 0;
        if (format.Contains("hh")) columnCount++;
        if (format.Contains("mm")) columnCount++;
        if (format.Contains("ss")) columnCount++;
        if (format.Contains("fff")) columnCount++;
        return columnCount;
    }

    public void SetCursorXtoCenter(float width)
    {
        // push the big boi font for the UID
        ImGui.SetCursorPosX((ImGui.GetWindowContentRegionMax().X - ImGui.GetWindowContentRegionMin().X) / 2 - width / 2);
    }

    public void DrawHelpText(string helpText, bool inner = false)
    {
        if (inner) { ImUtf8.SameLineInner(); }
        else { ImGui.SameLine(); }
        IconText(FontAwesomeIcon.QuestionCircle, ImGui.GetColorU32(ImGuiCol.TextDisabled));
        AttachToolTip(helpText);
    }

    public bool DrawOtherPluginState()
    {
        var check = FontAwesomeIcon.Check;
        var cross = FontAwesomeIcon.SquareXmark;
        ImGui.TextUnformatted("Optional Plugins:");

        ImGui.SameLine();
        ImGui.TextUnformatted("Penumbra");
        ImGui.SameLine();
        IconText(_penumbraExists ? check : cross, GetBoolColor(_penumbraExists));
        ImGui.SameLine();
        AttachToolTip($"Penumbra is " + (_penumbraExists ? "available and up to date." : "unavailable or not up to date."));
        ImGui.Spacing();

        ImGui.SameLine();
        ImGui.TextUnformatted("Glamourer");
        ImGui.SameLine();
        IconText(_glamourerExists ? check : cross, GetBoolColor(_glamourerExists));
        ImGui.SameLine();
        AttachToolTip($"Glamourer is " + (_glamourerExists ? "available and up to date." : "unavailable or not up to date."));
        ImGui.Spacing();

        ImGui.SameLine();
        ImGui.TextUnformatted("Customize+");
        ImGui.SameLine();
        IconText(_customizePlusExists ? check : cross, GetBoolColor(_customizePlusExists));
        ImGui.SameLine();
        AttachToolTip($"Customize+ is " + (_customizePlusExists ? "available and up to date." : "unavailable or not up to date."));
        ImGui.Spacing();

        ImGui.SameLine();
        ImGui.TextUnformatted("Moodles");
        ImGui.SameLine();
        IconText(_moodlesExists ? check : cross, GetBoolColor(_moodlesExists));
        ImGui.SameLine();
        AttachToolTip($"Moodles is " + (_moodlesExists ? "available and up to date." : "unavailable or not up to date."));
        ImGui.Spacing();

        return true;
    }

    public Vector2 GetIconButtonSize(FontAwesomeIcon icon)
    {
        using var font = IconFont.Push();
        return ImGuiHelpers.GetButtonSize(icon.ToIconString());
    }

    public Vector2 GetIconData(FontAwesomeIcon icon)
    {
        using var font = IconFont.Push();
        return ImGui.CalcTextSize(icon.ToIconString());
    }

    public void IconText(FontAwesomeIcon icon, uint color)
    {
        FontText(icon.ToIconString(), IconFont, color);
    }

    public void IconText(FontAwesomeIcon icon, Vector4? color = null)
    {
        IconText(icon, color == null ? ImGui.GetColorU32(ImGuiCol.Text) : ImGui.GetColorU32(color.Value));
    }

    public void LoadLocalization(string languageCode)
    {
        _localization.SetupWithLangCode(languageCode);
        Strings.ToS = new Strings.ToSStrings();
    }

    // for grabbing key states (we did something similar with the hardcore module)
    [LibraryImport("user32")]
    internal static partial short GetKeyState(int nVirtKey);


    private static void CenterWindow(float width, float height, ImGuiCond cond = ImGuiCond.None)
    {
        var center = ImGui.GetMainViewport().GetCenter();
        ImGui.SetWindowPos(new Vector2(center.X - width / 2, center.Y - height / 2), cond);
    }

    [GeneratedRegex(@"^(?:[a-zA-Z]:\\[\w\s\-\\]+?|\/(?:[\w\s\-\/])+?)$", RegexOptions.ECMAScript, 5000)]
    private static partial Regex PathRegex();

    private void FontText(string text, IFontHandle font, Vector4? color = null)
    {
        FontText(text, font, color == null ? ImGui.GetColorU32(ImGuiCol.Text) : ImGui.GetColorU32(color.Value));
    }

    private void FontText(string text, IFontHandle font, uint color)
    {
        using var pushedFont = font.Push();
        using var pushedColor = ImRaii.PushColor(ImGuiCol.Text, color);
        ImGui.TextUnformatted(text);
    }

    /// <summary> Retrieves the various UID text color based on the current server state.</summary>
    /// <returns> The color of the UID text in Vector4 format .</returns>
    public Vector4 GetUidColor()
    {
        return ApiController.ServerState switch
        {
            ServerState.Connecting => ImGuiColors.DalamudYellow,
            ServerState.Reconnecting => ImGuiColors.DalamudRed,
            ServerState.Connected => ImGuiColors.ParsedGreen,
            ServerState.Disconnected => ImGuiColors.DalamudYellow,
            ServerState.Disconnecting => ImGuiColors.DalamudYellow,
            ServerState.Unauthorized => ImGuiColors.DalamudRed,
            ServerState.VersionMisMatch => ImGuiColors.DalamudRed,
            ServerState.Offline => ImGuiColors.DalamudRed,
            ServerState.NoSecretKey => ImGuiColors.DalamudYellow,
            _ => ImGuiColors.DalamudRed
        };
    }

    /// <summary> Retrieves the various UID text based on the current server state.</summary>
    /// <returns> The text of the UID.</returns>
    public string GetUidText()
    {
        return ApiController.ServerState switch
        {
            ServerState.Reconnecting => "Reconnecting",
            ServerState.Connecting => "Connecting",
            ServerState.Disconnected => "Disconnected",
            ServerState.Disconnecting => "Disconnecting",
            ServerState.Unauthorized => "Unauthorized",
            ServerState.VersionMisMatch => "Version mismatch",
            ServerState.Offline => "Unavailable",
            ServerState.NoSecretKey => "No Secret Key",
            ServerState.Connected => _apiController.DisplayName, // displays when connected, your UID
            _ => string.Empty
        };
    }

    public sealed record IconScaleData(Vector2 IconSize, Vector2 NormalizedIconScale, float OffsetX, float IconScaling);
}
