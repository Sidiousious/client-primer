using Dalamud.Interface;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using GagSpeak.PlayerData.Handlers;
using GagSpeak.PlayerData.PrivateRooms;
using GagSpeak.Services.ConfigurationServices;
using GagSpeak.Services.Mediator;
using GagSpeak.Toybox.Debouncer;
using GagSpeak.Toybox.Services;
using GagSpeak.WebAPI;
using ImGuiNET;
using GagspeakAPI.Dto.Toybox;
using OtterGui;
using System.Numerics;
using System.Timers;

namespace GagSpeak.UI.UiRemote;

/// <summary>
/// I Blame ImPlot for its messiness as a result for this abysmal display of code here.
/// </summary>
public class RemoteController : RemoteBase
{
    // the class includes are shared however (i think), so dont worry about that.
    private readonly UiSharedService _uiShared;
    private readonly DeviceHandler _intifaceHandler; // these SHOULD all be shared. but if not put into Service.
    private readonly ToyboxRemoteService _remoteService;
    private readonly ApiController _apiController;
    private bool _isExpanded = false;

    public RemoteController(ILogger<RemoteController> logger,
        GagspeakMediator mediator, UiSharedService uiShared,
        DeviceHandler deviceHandler, ToyboxRemoteService remoteService,
        ApiController apiController, PrivateRoom privateRoom) 
        : base(logger, mediator, uiShared, remoteService, deviceHandler, privateRoom.RoomName)
    {
        // grab the shared services
        _uiShared = uiShared;
        _intifaceHandler = deviceHandler;
        _remoteService = remoteService;
        _apiController = apiController;
        // initialize our private room data with the private room passed in on startup so we can properly interact with the correct data.
        PrivateRoomData = privateRoom;
        IsOpen = true;
        // disable dumb dalamud UI forced buttons
        AllowPinning = false;
        AllowClickthrough = false;

        // create the buttons for opening side windows.
        TitleBarButtons = new()
        {
            new TitleBarButton()
            {
                Icon = FontAwesomeIcon.Users,
                Click = (msg) =>
                {
                    _logger.LogInformation("Opening User List");
                    UpdateSizeConstraints(!IsExpanded);
                },
                IconOffset = new(2,1),
                ShowTooltip = () =>
                {
                    ImGui.BeginTooltip();
                    ImGui.Text("Invite Online Users");
                    ImGui.EndTooltip();
                }
            },
            new TitleBarButton()
            {
                Icon = FontAwesomeIcon.CommentDots,
                Click = (msg) =>
                {
                    _logger.LogInformation("Opening Chat Window");
                    UpdateSizeConstraints(!IsExpanded);
                },
                IconOffset = new(2,1),
                ShowTooltip = () =>
                {
                    ImGui.BeginTooltip();
                    ImGui.Text("Open Chat Window");
                    ImGui.EndTooltip();
                }
            }
        };


        // close the window when our user leaves this room.
        Mediator.Subscribe<ToyboxPrivateRoomLeft>(this, (msg) =>
        {
            // if the name of the room being closed matches the name of the private room data, close the window.
            if (msg.RoomName == PrivateRoomData.RoomName)
            {
                IsOpen = false;
            }
        });

        // close window on disconnect message.
        Mediator.Subscribe<ToyboxDisconnectedMessage>(this, (_) => IsOpen = false);
    }

    public PrivateRoom PrivateRoomData { get; set; } = null!; // this is a shared service, so it should be fine.

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        // anything else we should add here we can add here.
    }

    /// <summary>
    /// Will display personal devices, their motors and additional options. </para>
    /// </summary>
    public override void DrawCenterBar(ref float xPos, ref float yPos, ref float width)
    {
        // grab the content region of the current section
        var CurrentRegion = ImGui.GetContentRegionAvail();
        ImGui.SetCursorPosY(ImGui.GetCursorPosY() - ImGuiHelpers.GlobalScale * 5);
        using (var color = ImRaii.PushColor(ImGuiCol.ChildBg, new Vector4(0.2f, 0.2f, 0.2f, 0.930f)))
        {
            // create a child for the center bar
            using (var canterBar = ImRaii.Child($"###CenterBarDrawPersonal", new Vector2(CurrentRegion.X, 45f), false))
            {
                UiSharedService.ColorText("CenterBar dummy placement", ImGuiColors.ParsedGreen);
            }
        }
    }

    private string NextChatMessage = string.Empty;
    public override void DrawExtraDetails()
    {
        // grab the content region of the current section
        var CurrentRegion = ImGui.GetContentRegionAvail();

        // create a child for the center bar
        using (var extraDetailsSideTab = ImRaii.Child($"###ExtraDetailsDraw", CurrentRegion, false))
        {
            ImGuiUtil.Center("Private Room Chat");
            ImGui.Separator();

            // Calculate the height for the chat log, leaving space for the input text field
            float inputTextHeight = ImGui.GetFrameHeightWithSpacing();
            float chatLogHeight = CurrentRegion.Y - inputTextHeight;

            // Create a child for the chat log
            using (var chatlogChild = ImRaii.Child($"###ChatlogChild", new Vector2(CurrentRegion.X, chatLogHeight - inputTextHeight), false))
            {
                PrivateRoomData.PrivateRoomChatlog.PrintImgui();
            }

            // Now draw out the input text field
            var nextMessageRef = NextChatMessage;
            ImGui.SetNextItemWidth(CurrentRegion.X);
            if (ImGui.InputTextWithHint("##ChatInputBox", "chat message here...", ref nextMessageRef, 300))
            {
                // Update the stored message
                NextChatMessage = nextMessageRef;
            }

            // Check if the input text field is focused and the Enter key is pressed
            if (ImGui.IsItemFocused() && ImGui.IsKeyPressed(ImGuiKey.Enter))
            {
                // Send the message to the server
                _logger.LogInformation($"Sending Message: {NextChatMessage}");
                _apiController.PrivateRoomSendMessage(new RoomMessageDto
                    (PrivateRoomData.GetParticipant(_apiController.UID).User, PrivateRoomData.RoomName, NextChatMessage)).ConfigureAwait(false);
                NextChatMessage = string.Empty;
            }
        }
    }


    /// <summary>
    /// This method is also an override function, as depending on the use.
    /// We may also implement unique buttons here on the side that execute different functionalities.
    /// </summary>
    /// <param name="region"> The region of the side button section of the UI </param>
    public override void DrawSideButtonsTable(Vector2 region)
    {
        // push our styles
        using var styleColor = ImRaii.PushColor(ImGuiCol.Button, new Vector4(.2f, .2f, .2f, .2f))
            .Push(ImGuiCol.ButtonHovered, new Vector4(.3f, .3f, .3f, .4f))
            .Push(ImGuiCol.ButtonActive, _remoteService.LushPinkButton);
        using var styleVar = ImRaii.PushStyle(ImGuiStyleVar.FrameRounding, 40);

        // grab the content region of the current section
        var CurrentRegion = ImGui.GetContentRegionAvail();
        var yPos2 = ImGui.GetCursorPosY();

        // setup a child for the table cell space
        using (var leftChild = ImRaii.Child($"###ButtonsList", CurrentRegion with { Y = region.Y }, false, ImGuiWindowFlags.NoDecoration))
        {
            var InitPos = ImGui.GetCursorPosY();
            if (RemoteOnline)
            {
                ImGuiUtil.Center($"{DurationStopwatch.Elapsed.ToString(@"mm\:ss")}");
            }

            // move our yposition down to the top of the frame height times a .3f scale of the current region
            ImGui.SetCursorPosY(InitPos + CurrentRegion.Y * .1f);
            ImGui.Separator();
            ImGui.SetCursorPosY(ImGui.GetCursorPosY() + CurrentRegion.Y * .025f);

            // attempt to obtain an image wrap for it
            var spinnyArrow = _uiShared.GetImageFromDirectoryFile("arrows-spin.png");
            if (!(spinnyArrow is { } wrap))
            {
                _logger.LogWarning("Failed to render image!");
            }
            else
            {
                Vector4 buttonColor = IsLooping ? _remoteService.LushPinkButton : _remoteService.SideButton;
                // aligns the image in the center like we want.
                if (_uiShared.DrawScaledCenterButtonImage("LoopButton", new Vector2(50, 50),
                    buttonColor, new Vector2(40, 40), wrap))
                {
                    IsLooping = !IsLooping;
                    if (IsFloating) { IsFloating = false; }
                }
            }

            // move it down from current position by another .2f scale
            ImGui.SetCursorPosY(ImGui.GetCursorPosY() + CurrentRegion.Y * .05f);

            var circlesDot = _uiShared.GetImageFromDirectoryFile("circle-dot.png");
            if (!(circlesDot is { } wrap2))
            {
                _logger.LogWarning("Failed to render image!");
            }
            else
            {
                Vector4 buttonColor2 = IsFloating ? _remoteService.LushPinkButton : _remoteService.SideButton;
                // aligns the image in the center like we want.
                if (_uiShared.DrawScaledCenterButtonImage("FloatButton", new Vector2(50, 50),
                    buttonColor2, new Vector2(40, 40), wrap2))
                {
                    IsFloating = !IsFloating;
                    if (IsLooping) { IsLooping = false; }
                }
            }

            ImGui.SetCursorPosY(CurrentRegion.Y * .775f);

            var power = _uiShared.GetImageFromDirectoryFile("power.png");
            if (!(power is { } wrap3))
            {
                _logger.LogWarning("Failed to render image!");
            }
            else
            {
                Vector4 buttonColor3 = RemoteOnline ? _remoteService.LushPinkButton : _remoteService.SideButton;
                // aligns the image in the center like we want.
                if (_uiShared.DrawScaledCenterButtonImage("PowerToggleButton", new Vector2(50, 50),
                    buttonColor3, new Vector2(40, 40), wrap3))
                {
                    if (!RemoteOnline)
                    {
                        _logger.LogTrace("Starting Recording!");
                        StartVibrating();
                    }
                    else
                    {
                        _logger.LogTrace("Stopping Recording!");
                        StopVibrating();
                    }
                }
            }
        }
        // pop what we appended
        styleColor.Pop(3);
        styleVar.Pop();
    }

    /// <summary>
    /// This override DOES NOT STORE VIBRATION DATA
    /// 
    /// Instead, it transmits the data to the other connected users, or rather, the user we spesify.
    /// TODO: Embed this logic.
    /// </summary>
    public override void RecordData(object? sender, ElapsedEventArgs e)
    {
        // THIS IS NOT GOING TO US!!! It's going off to OTHER CLIENTS.

        // manage that somewhere else, potentially the remote service or something idk!

        // if any devices are currently connected, and our intiface client is connected,
        /*if (_intifaceHandler.AnyDeviceConnected && _intifaceHandler.ConnectedToIntiface)
        {
            //_logger.LogTrace("Sending Vibration Data to Devices!");
            // send the vibration data to all connected devices
            if (IsLooping && !IsDragging && StoredLoopDataBlock.Count > 0)
            {
                //_logger.LogTrace($"{(byte)Math.Round(StoredLoopDataBlock[BufferLoopIndex])}");
                _intifaceHandler.SendVibeToAllDevices((byte)Math.Round(StoredLoopDataBlock[BufferLoopIndex]));
            }
            else
            {
                //_logger.LogTrace($"{(byte)Math.Round(CirclePosition[1])}");
                _intifaceHandler.SendVibeToAllDevices((byte)Math.Round(CirclePosition[1]));
            }
        }*/
    }

    public override void OnClose()
    {
        Mediator.Publish(new RemoveWindowMessage(this));
    }
}
