using Dalamud.Interface;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using GagSpeak.PlayerData.Data;
using GagSpeak.PlayerData.Handlers;
using GagSpeak.PlayerData.Pairs;
using GagSpeak.Services.ConfigurationServices;
using GagSpeak.Services.Mediator;
using GagSpeak.UI.Handlers;
using GagSpeak.Utils;
using GagSpeak.WebAPI;
using GagspeakAPI.Dto.Permissions;
using ImGuiNET;
using OtterGui;
using OtterGui.Text;
using System.Drawing;
using System.Numerics;

namespace GagSpeak.UI.UiPuppeteer;

public class PuppeteerUI : WindowMediatorSubscriberBase
{
    private readonly UiSharedService _uiShared;
    private readonly UserPairListHandler _userPairListHandler;
    private readonly ClientConfigurationManager _clientConfigs;
    private readonly PuppeteerHandler _puppeteerHandler;
    private readonly AliasTable _aliasTable;
    public PuppeteerUI(ILogger<PuppeteerUI> logger, GagspeakMediator mediator,
        UiSharedService uiSharedService, ClientConfigurationManager clientConfigs, 
        UserPairListHandler userPairListHandler, PuppeteerHandler handler,
        AliasTable aliasTable) : base(logger, mediator, "Puppeteer UI")
    {
        _uiShared = uiSharedService;
        _clientConfigs = clientConfigs;
        _userPairListHandler = userPairListHandler;
        _puppeteerHandler = handler;
        _aliasTable = aliasTable;

        AllowPinning = false;
        AllowClickthrough = false;
        // define initial size of window and to not respect the close hotkey.
        this.SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(545, 370),
            MaximumSize = new Vector2(1000, float.MaxValue)
        };
        RespectCloseHotkey = false;
    }

    protected override void PreDrawInternal()
    {
        // include our personalized theme for this window here if we have themes enabled.
    }
    protected override void PostDrawInternal()
    {
        // include our personalized theme for this window here if we have themes enabled.
    }
    protected override void DrawInternal()
    {
        // _logger.LogInformation(ImGui.GetWindowSize().ToString()); <-- USE FOR DEBUGGING ONLY.
        // get information about the window region, its item spacing, and the top left side height.
        var region = ImGui.GetContentRegionAvail();
        var itemSpacing = ImGui.GetStyle().ItemSpacing;
        var topLeftSideHeight = region.Y;
        var cellPadding = ImGui.GetStyle().CellPadding;

        // create the draw-table for the selectable and viewport displays
        ImGui.PushStyleVar(ImGuiStyleVar.CellPadding, new Vector2(5f * _uiShared.GetFontScalerFloat(), 0));
        try
        {
            using (var table = ImRaii.Table($"PuppeteerUiWindowTable", 2, ImGuiTableFlags.SizingFixedFit | ImGuiTableFlags.BordersInnerV))
            {
                if (!table) return;
                // setup the columns for the table
                ImGui.TableSetupColumn("##LeftColumn", ImGuiTableColumnFlags.WidthFixed, 200f * ImGuiHelpers.GlobalScale);
                ImGui.TableSetupColumn("##RightColumn", ImGuiTableColumnFlags.WidthStretch);
                ImGui.TableNextColumn();

                var regionSize = ImGui.GetContentRegionAvail();
                ImGui.PushStyleVar(ImGuiStyleVar.SelectableTextAlign, new Vector2(0.5f, 0.5f));

                using (var leftChild = ImRaii.Child($"###PuppeteerLeft", regionSize with { Y = topLeftSideHeight }, false, ImGuiWindowFlags.NoDecoration))
                {
                    var iconTexture = _uiShared.GetLogo();
                    if (!(iconTexture is { } wrap))
                    {
                        /*_logger.LogWarning("Failed to render image!");*/
                    }
                    else
                    {
                        UtilsExtensions.ImGuiLineCentered("###PuppeteerLogo", () =>
                        {
                            ImGui.Image(wrap.ImGuiHandle, new(125f * _uiShared.GetFontScalerFloat(), 125f * _uiShared.GetFontScalerFloat()));
                            if (ImGui.IsItemHovered())
                            {
                                ImGui.BeginTooltip();
                                ImGui.Text($"You found a wild easter egg, Y I P P E E !!!");
                                ImGui.EndTooltip();
                            }
                        });
                    }
                    // add separator
                    ImGui.Spacing();
                    ImGui.Separator();
                    // Add the tab menu for the left side
                    _userPairListHandler.DrawPairsNoGroups(region.X);
                }
                // pop pushed style variables and draw next column.
                ImGui.PopStyleVar();
                ImGui.TableNextColumn();
                // display right half viewport based on the tab selection
                using (var rightChild = ImRaii.Child($"###PuppeteerRightSide", Vector2.Zero, false))
                {
                    DrawPuppeteer(cellPadding);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error: {ex}");
        }
        finally
        {
            ImGui.PopStyleVar();
        }
    }

    // Main Right-half Draw function for puppeteer.
    private void DrawPuppeteer(Vector2 DefaultCellPadding)
    {
        if (_puppeteerHandler.SelectedPair == null)
        {
            ImGui.Text("Select a pair to view their puppeteer setup.");
            return;
        }
        var region = ImGui.GetContentRegionAvail();
        var itemSpacing = ImGui.GetStyle().ItemSpacing;

        // draw title
        DrawPuppeteerHeader(DefaultCellPadding);
        ImGui.Separator();

        using (var headerGroup = ImRaii.Group())
        {
            if (_puppeteerHandler.StorageBeingEdited.CharacterName != string.Empty)
            {
                ImGui.Text("Scanning for Trigger Messages from: ");
                ImGui.SameLine();
                var text = $"{_puppeteerHandler.StorageBeingEdited.CharacterName} @ {_puppeteerHandler.StorageBeingEdited.CharacterWorld}";
                UiSharedService.ColorText(text, ImGuiColors.ParsedPink);
            }
            else
            {
                UiSharedService.ColorText("No CharacterName to listen to for this Pair." + Environment.NewLine
                    + "Have them press 'Update Pair with Name'" + Environment.NewLine
                    + "They is found under the UserPair Actions Menu", ImGuiColors.DalamudRed);
            }
        }

        ImGui.Separator();
        using (var disabledEdited = ImRaii.Disabled(_puppeteerHandler.StorageBeingEdited.CharacterName == string.Empty))
        {
            // Create a Tabbar for the sub-sections of the puppeteer.
            using var tabBar = ImRaii.TabBar("Puppeteer Sub-Tabs");

            if (tabBar)
            {
                var pairCharaInfo = ImRaii.TabItem("Pair Character");
                if (pairCharaInfo)
                {
                    DrawSelectedPairTriggerPhrase(region.X);

                    // draw example usage
                    if (!string.IsNullOrEmpty(_puppeteerHandler.SelectedPair.UserPairOwnUniquePairPerms.TriggerPhrase))
                    {
                        // if trigger phrase exists, see if it has splits to contain multiple.
                        bool hasSplits = _puppeteerHandler.SelectedPair.UserPairOwnUniquePairPerms.TriggerPhrase.Contains("|");
                        var displayText = hasSplits ? _puppeteerHandler.SelectedPair.UserPairOwnUniquePairPerms.TriggerPhrase.Split('|')[0]
                                                    : _puppeteerHandler.SelectedPair.UserPairOwnUniquePairPerms.TriggerPhrase;
                        // example display
                        ImGui.Text($"Example Usage from : {_puppeteerHandler.SelectedPair.UserData.AliasOrUID}");
                        ImGui.TextColored(new Vector4(1.0f, 1.0f, 0.0f, 1.0f), $"<{_puppeteerHandler.SelectedPair.UserData.AliasOrUID}> " +
                        $"{displayText} {_puppeteerHandler.SelectedPair.UserPairOwnUniquePairPerms.StartChar} " + $"glamour apply Hogtied | p | [me] " +
                        $"{_puppeteerHandler.SelectedPair.UserPairOwnUniquePairPerms.EndChar}");
                        UiSharedService.AttachToolTip($"The spaces between the brackets and commands/trigger phrases are optional.");
                    }


                    bool allowSitRequests = _puppeteerHandler.SelectedPair.UserPairOwnUniquePairPerms.AllowSitRequests;
                    if (ImGui.Checkbox("Allow Sit Commands", ref allowSitRequests))
                    {
                        _logger.LogTrace($"Updated own pair permission: AllowSitCommands to {allowSitRequests}");
                        _ = _uiShared.ApiController.UserUpdateOwnPairPerm(new UserPairPermChangeDto(_puppeteerHandler.SelectedPair.UserData,
                            new KeyValuePair<string, object>("AllowSitRequests", allowSitRequests)));
                    }
                    UiSharedService.AttachToolTip($"Allows {_puppeteerHandler.SelectedPair.UserData.AliasOrUID} to make you perform /sit and /groundsit");

                    bool allowMotionRequests = _puppeteerHandler.SelectedPair.UserPairOwnUniquePairPerms.AllowMotionRequests;
                    if (ImGui.Checkbox("Allow Emotes & Expressions", ref allowMotionRequests))
                    {
                        _logger.LogTrace($"Updated own pair permission: AllowEmotesExpressions to {allowMotionRequests}");
                        _ = _uiShared.ApiController.UserUpdateOwnPairPerm(new UserPairPermChangeDto(_puppeteerHandler.SelectedPair.UserData,
                            new KeyValuePair<string, object>("AllowMotionRequests", allowMotionRequests)));
                    }
                    UiSharedService.AttachToolTip($"Allows {_puppeteerHandler.SelectedPair.UserData.AliasOrUID} to make you perform emotes and expressions");

                    bool allowAllRequests = _puppeteerHandler.SelectedPair.UserPairOwnUniquePairPerms.AllowAllRequests;
                    if (ImGui.Checkbox("Allow All Commands", ref allowAllRequests))
                    {
                        _logger.LogTrace($"Updated own pair permission: AllowAllCommands to {allowAllRequests}");
                        _ = _uiShared.ApiController.UserUpdateOwnPairPerm(new UserPairPermChangeDto(_puppeteerHandler.SelectedPair.UserData,
                            new KeyValuePair<string, object>("AllowAllRequests", allowAllRequests)));
                    }
                    UiSharedService.AttachToolTip($"Allows {_puppeteerHandler.SelectedPair.UserData.AliasOrUID} to make you perform any command");

                }
                pairCharaInfo.Dispose();

                // create glamour tab (applying the visuals)
                var aliasList = ImRaii.TabItem("Your Alias List");
                if (aliasList)
                {
                    _aliasTable.DrawAliasListTable(_puppeteerHandler.SelectedPair.UserData.UID, DefaultCellPadding.Y);
                }
                aliasList.Dispose();
            }
        }
    }

    private void DrawPuppeteerHeader(Vector2 DefaultCellPadding)
    {
        if (_puppeteerHandler.SelectedPair == null) return;

        using var rounding = ImRaii.PushStyle(ImGuiStyleVar.FrameRounding, 12f);
        var startYpos = ImGui.GetCursorPosY();
        var iconSize = _uiShared.GetIconButtonSize(FontAwesomeIcon.Plus);
        Vector2 textSize;
        using (_uiShared.UidFont.Push())
        {
            textSize = ImGui.CalcTextSize($"Setup for {_puppeteerHandler.SelectedPair.UserData.AliasOrUID}");
        }
        var centerYpos = (textSize.Y - iconSize.Y);
        using (ImRaii.Child("EditAliasStorageForPair", new Vector2(UiSharedService.GetWindowContentRegionWidth(), iconSize.Y + (centerYpos - startYpos) * 2 - DefaultCellPadding.Y)))
        {
            // now next to it we need to draw the header text
            ImGui.SameLine(ImGui.GetStyle().ItemSpacing.X);
            ImGui.SetCursorPosY(startYpos);
            using (_uiShared.UidFont.Push())
            {
                UiSharedService.ColorText($"Setup for {_puppeteerHandler.SelectedPair.UserData.AliasOrUID}", ImGuiColors.ParsedPink);
            }

            // now calculate it so that the cursors Yposition centers the button in the middle height of the text
            ImGui.SameLine(ImGui.GetWindowContentRegionMin().X + UiSharedService.GetWindowContentRegionWidth() - iconSize.X - ImGui.GetStyle().ItemSpacing.X);
            ImGui.SetCursorPosY(ImGui.GetCursorPosY() + centerYpos - DefaultCellPadding.Y);
            using (var disabledEdited = ImRaii.Disabled(_puppeteerHandler.StorageBeingEdited.CharacterName == string.Empty))
            {
                if (_uiShared.IconButton(FontAwesomeIcon.Save))
                {
                    _puppeteerHandler.UpdatedEditedStorage();
                }
                UiSharedService.AttachToolTip("Update Changes made for this pair.");
            }
        }
    }

    private string _tempTriggerStorage = null!;
    private string _tempStartChar = null!;
    private string _tempEndChar = null!;
    private void DrawSelectedPairTriggerPhrase(float width)
    {
        ImGui.AlignTextToFramePadding();
        ImGui.Text("Trigger Phrase");
        _uiShared.DrawHelpText("This is the phrase your pair needs to say to make YOU execute a command.");
        ImGui.SetNextItemWidth(width * 0.8f);
        // store temp value to contain within the text input
        var TriggerPhrase = _tempTriggerStorage ?? _puppeteerHandler.SelectedPair!.UserPairOwnUniquePairPerms.TriggerPhrase;
        if (ImGui.InputTextWithHint($"##{_puppeteerHandler.SelectedPair!.UserData.AliasOrUID}sTrigger", "Leave Blank for no trigger phrase...",
            ref TriggerPhrase, 64, ImGuiInputTextFlags.EnterReturnsTrue))
        {
            _tempTriggerStorage = TriggerPhrase;
        }
        if (ImGui.IsItemDeactivatedAfterEdit())
        {
            _puppeteerHandler.SelectedPair.UserPairOwnUniquePairPerms.TriggerPhrase = TriggerPhrase;
            _tempTriggerStorage = null!;
            // TODO: publish to mediator our update so we push it
        }
        UiSharedService.AttachToolTip("You can create multiple trigger phrases by placing a | between phrases.");

        // on the same line inner, draw the start char input directly beside it.
        ImUtf8.SameLineInner();
        ImGui.SetNextItemWidth(20*ImGuiHelpers.GlobalScale);
        // draw out the start and end characters
        var startChar = _tempStartChar ?? _puppeteerHandler.SelectedPair.UserPairOwnUniquePairPerms.StartChar.ToString();
        if (ImGui.InputText($"##{_puppeteerHandler.SelectedPair.UserData.AliasOrUID}sStarChar", ref startChar, 1, ImGuiInputTextFlags.EnterReturnsTrue))
        {
            _tempStartChar = startChar;
        }
        if (ImGui.IsItemDeactivatedAfterEdit())
        {
            if (string.IsNullOrEmpty(startChar) || startChar == " ")
            {
                startChar = "(";
            }
            _puppeteerHandler.SelectedPair.UserPairOwnUniquePairPerms.StartChar = startChar[0];
            _tempStartChar = null!;
            // TODO: publish to mediator our update so we push it
        }
        UiSharedService.AttachToolTip($"Custom Start Character that replaces the left enclosing bracket.\n" +
            "Replaces the [ ( ] in Ex: [ TriggerPhrase (commandToExecute) ]");

        // on same line inner, draw the end char.
        ImUtf8.SameLineInner();
        ImGui.SetNextItemWidth(20 * ImGuiHelpers.GlobalScale);
        var endChar = _tempEndChar ?? _puppeteerHandler.SelectedPair.UserPairOwnUniquePairPerms.EndChar.ToString();
        if (ImGui.InputText($"##{_puppeteerHandler.SelectedPair.UserData.AliasOrUID}sStarChar", ref endChar, 1, ImGuiInputTextFlags.EnterReturnsTrue))
        {
            _tempEndChar = endChar;
        }
        if (ImGui.IsItemDeactivatedAfterEdit())
        {
            if (string.IsNullOrEmpty(endChar) || endChar == " ")
            {
                endChar = ")";
            }
            _puppeteerHandler.SelectedPair.UserPairOwnUniquePairPerms.EndChar = endChar[0];
            _tempEndChar = null!;
            // TODO: publish to mediator our update so we push it
        }
        UiSharedService.AttachToolTip($"Custom End Character that replaces the right enclosing bracket.\n" +
            "Replaces the [ ) ] in Ex: [ TriggerPhrase (commandToExecute) ]");
    }
}
