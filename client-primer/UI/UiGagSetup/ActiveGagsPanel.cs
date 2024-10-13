using Dalamud.Interface;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Utility.Raii;
using GagSpeak.PlayerData.Data;
using GagSpeak.PlayerData.Handlers;
using GagSpeak.PlayerData.Services;
using GagSpeak.Services.Mediator;
using GagSpeak.Utils;
using GagspeakAPI.Enums;
using GagspeakAPI.Extensions;
using ImGuiNET;
using OtterGui.Text;
using System.Numerics;

namespace GagSpeak.UI.UiGagSetup;

public class ActiveGagsPanel : DisposableMediatorSubscriberBase
{
    private readonly UiSharedService _uiSharedService;
    private readonly PlayerCharacterData _playerManager; // for grabbing lock data
    private readonly GagManager _gagManager;
    private readonly AppearanceHandler _handler;
    private readonly AppearanceService _appearanceChangeService;

    public ActiveGagsPanel(ILogger<ActiveGagsPanel> logger,
        GagspeakMediator mediator, UiSharedService uiSharedService,
        GagManager gagManager, PlayerCharacterData playerManager,
        AppearanceHandler handler, AppearanceService appearanceChangeService)
        : base(logger, mediator)
    {
        _uiSharedService = uiSharedService;
        _playerManager = playerManager;
        _gagManager = gagManager;
        _handler = handler;
        _appearanceChangeService = appearanceChangeService;

        Mediator.Subscribe<ActiveGagsUpdated>(this, (_) =>
        {
            if (_playerManager.AppearanceData == null)
            {
                Logger.LogWarning("Appearance data is null, cannot update active gags.");
                return;
            }
            // update our combo items.
            _uiSharedService._selectedComboItems["Gag Type 0"] = _playerManager.AppearanceData.GagSlots[0].GagType.ToGagType();
            _uiSharedService._selectedComboItems["Gag Type 1"] = _playerManager.AppearanceData.GagSlots[1].GagType.ToGagType();
            _uiSharedService._selectedComboItems["Gag Type 2"] = _playerManager.AppearanceData.GagSlots[2].GagType.ToGagType();
        });

        Mediator.Subscribe<ActiveLocksUpdated>(this, (_) =>
        {
            if (_playerManager.AppearanceData == null)
            {
                Logger.LogWarning("Appearance data is null, cannot update active locks.");
                return;
            }
            _uiSharedService._selectedComboItems["Lock Type 0"] = _playerManager.AppearanceData.GagSlots[0].Padlock.ToPadlock();
            _uiSharedService._selectedComboItems["Lock Type 1"] = _playerManager.AppearanceData.GagSlots[1].Padlock.ToPadlock();
            _uiSharedService._selectedComboItems["Lock Type 2"] = _playerManager.AppearanceData.GagSlots[2].Padlock.ToPadlock();
            _gagManager.ActiveSlotPadlocks[0] = _playerManager.AppearanceData.GagSlots[0].Padlock.ToPadlock();
            _gagManager.ActiveSlotPadlocks[1] = _playerManager.AppearanceData.GagSlots[1].Padlock.ToPadlock();
            _gagManager.ActiveSlotPadlocks[2] = _playerManager.AppearanceData.GagSlots[2].Padlock.ToPadlock();
            Logger.LogInformation(
                "Lock 0: " + _playerManager.AppearanceData.GagSlots[0].Padlock.ToPadlock() + " || " +
                "Lock 1: " + _playerManager.AppearanceData.GagSlots[1].Padlock.ToPadlock() + " || " +
                "Lock 2: " + _playerManager.AppearanceData.GagSlots[2].Padlock.ToPadlock());
        });
    }

    private string GagTypeOnePath => $"GagImages\\{_playerManager.AppearanceData!.GagSlots[0].GagType}.png" ?? $"ItemMouth\\None.png";
    private string GagTypeTwoPath => $"GagImages\\{_playerManager.AppearanceData!.GagSlots[1].GagType}.png" ?? $"ItemMouth\\None.png";
    private string GagTypeThreePath => $"GagImages\\{_playerManager.AppearanceData!.GagSlots[2].GagType}.png" ?? $"ItemMouth\\None.png";
    private string GagPadlockOnePath => $"PadlockImages\\{_playerManager.AppearanceData!.GagSlots[0].Padlock}.png" ?? $"Padlocks\\None.png";
    private string GagPadlockTwoPath => $"PadlockImages\\{_playerManager.AppearanceData!.GagSlots[1].Padlock}.png" ?? $"Padlocks\\None.png";
    private string GagPadlockThreePath => $"PadlockImages\\{_playerManager.AppearanceData!.GagSlots[2].Padlock}.png" ?? $"Padlocks\\None.png";

    // the search filters for our gag dropdowns.
    public string[] Filters = new string[3] { "", "", "" };

    // Draw the active gags tab
    public void DrawActiveGagsPanel()
    {
        if (_playerManager.CoreDataNull)
        {
            Logger.LogWarning("Core data is null, cannot draw active gags panel.");
            return;
        }
        Vector2 bigTextSize = new Vector2(0, 0);
        using (_uiSharedService.UidFont.Push()) { bigTextSize = ImGui.CalcTextSize("HeightDummy"); }

        var region = ImGui.GetContentRegionAvail();
        try
        {
            var lock1 = _playerManager.AppearanceData!.GagSlots[0].Padlock.ToPadlock();
            var lock2 = _playerManager.AppearanceData!.GagSlots[1].Padlock.ToPadlock();
            var lock3 = _playerManager.AppearanceData!.GagSlots[2].Padlock.ToPadlock();

            // Gag Label 1
            _uiSharedService.BigText("Inner Gag:");
            // Gag Timer 1
            if (lock1 is Padlocks.FiveMinutesPadlock or Padlocks.TimerPasswordPadlock or Padlocks.OwnerTimerPadlock or Padlocks.DevotionalTimerPadlock or Padlocks.MimicPadlock)
            {
                ImGui.SameLine();
                DisplayTimeLeft(
                    endTime: _playerManager.AppearanceData.GagSlots[0].Timer,
                    padlock: _playerManager.AppearanceData.GagSlots[0].Padlock.ToPadlock(),
                    userWhoSetLock: _playerManager.AppearanceData.GagSlots[0].Assigner,
                    yPos: ImGui.GetCursorPosY() + ((bigTextSize.Y - ImGui.GetTextLineHeight()) / 2) + 5f);
            }
            // Selection 1
            DrawGagAndLockSection(0, GagTypeOnePath, GagPadlockOnePath, (lock1 != Padlocks.None),
                _playerManager.AppearanceData.GagSlots[0].GagType.ToGagType(), (lock1 == Padlocks.None ? _gagManager.ActiveSlotPadlocks[0] : lock1));

            // Gag Label 2
            _uiSharedService.BigText("Central Gag:");
            // Gag Timer 2
            if (lock2 is Padlocks.FiveMinutesPadlock or Padlocks.TimerPasswordPadlock or Padlocks.OwnerTimerPadlock or Padlocks.DevotionalTimerPadlock or Padlocks.MimicPadlock)
            {
                ImGui.SameLine();
                DisplayTimeLeft(
                    endTime: _playerManager.AppearanceData.GagSlots[1].Timer,
                    padlock: _playerManager.AppearanceData.GagSlots[1].Padlock.ToPadlock(),
                    userWhoSetLock: _playerManager.AppearanceData.GagSlots[1].Assigner,
                    yPos: ImGui.GetCursorPosY() + ((bigTextSize.Y - ImGui.GetTextLineHeight()) / 2) + 5f);
            }
            // Selection 2
            DrawGagAndLockSection(1, GagTypeTwoPath, GagPadlockTwoPath, (lock2 != Padlocks.None),
                _playerManager.AppearanceData.GagSlots[1].GagType.ToGagType(), (lock2 == Padlocks.None ? _gagManager.ActiveSlotPadlocks[1] : lock2));

            // Gag Label 3
            _uiSharedService.BigText("Outer Gag:");
            // Gag Timer 3
            if (lock3 is Padlocks.FiveMinutesPadlock or Padlocks.TimerPasswordPadlock or Padlocks.OwnerTimerPadlock or Padlocks.DevotionalTimerPadlock or Padlocks.MimicPadlock)
            {
                ImGui.SameLine();
                DisplayTimeLeft(
                    endTime: _playerManager.AppearanceData.GagSlots[2].Timer,
                    padlock: _playerManager.AppearanceData.GagSlots[2].Padlock.ToPadlock(),
                    userWhoSetLock: _playerManager.AppearanceData.GagSlots[2].Assigner,
                    yPos: ImGui.GetCursorPosY() + ((bigTextSize.Y - ImGui.GetTextLineHeight()) / 2) + 5f);
            }
            // Selection 3
            DrawGagAndLockSection(2, GagTypeThreePath, GagPadlockThreePath, (lock3 != Padlocks.None),
                _playerManager.AppearanceData.GagSlots[2].GagType.ToGagType(), (lock3 == Padlocks.None ? _gagManager.ActiveSlotPadlocks[2] : lock3));
        }
        catch (Exception ex)
        {
            Logger.LogError($"Error: {ex}");
        }
    }

    private static readonly HashSet<Padlocks> TwoRowLocks = new HashSet<Padlocks>
    {
        Padlocks.None, Padlocks.MetalPadlock, Padlocks.FiveMinutesPadlock, Padlocks.OwnerPadlock, Padlocks.OwnerTimerPadlock, 
        Padlocks.DevotionalPadlock, Padlocks.DevotionalTimerPadlock, Padlocks.MimicPadlock
    };


    private void DrawGagAndLockSection(int slotNumber, string gagTypePath, string lockTypePath, bool currentlyLocked, GagType gagType, Padlocks padlockType)
    {
        using (var gagAndLockOuterGroup = ImRaii.Group())
        {
            // Display gag image
            var gagOneTexture = _uiSharedService.GetImageFromDirectoryFile(gagTypePath);
            if (!(gagOneTexture is { } wrapGag))
            {
                Logger.LogWarning("Failed to render image!");
            }
            else
            {
                ImGui.Image(wrapGag.ImGuiHandle, new Vector2(80, 80));
            }
            ImGui.SameLine();

            // Display combo for gag type and lock
            var GroupCursorY = ImGui.GetCursorPosY();
            using (var gagAndLockInnerGroup = ImRaii.Group())
            {
                if (TwoRowLocks.Contains(padlockType)) ImGui.SetCursorPosY(GroupCursorY + ImGui.GetFrameHeight() / 2);

                using (ImRaii.Disabled(currentlyLocked))
                {
                    _uiSharedService.DrawComboSearchable($"Gag Type {slotNumber}", 250f, ref Filters[slotNumber],
                    Enum.GetValues<GagType>(), (gag) => gag.GagName(), false,
                    (i) =>
                    {
                        // locate the GagData that matches the alias of i
                        var SelectedGag = i;
                        // obtain the previous gag prior to changing.
                        var PreviousGag = _playerManager.AppearanceData!.GagSlots[slotNumber].GagType.ToGagType();
                        // if the previous gagtype was none, simply equip it.
                        if (PreviousGag == GagType.None)
                        {
                            Logger.LogDebug($"Equipping gag {SelectedGag}", LoggerType.GagManagement);
                            // publish the logic update change
                            _ = Task.Run(async () =>
                            {
                                _gagManager.OnGagTypeChanged((GagLayer)slotNumber, SelectedGag, true, true);
                                await _handler.GagApplied(SelectedGag);
                            });
                        }
                        // if the previous gagtype was not none, unequip the previous and equip the new.
                        else
                        {
                            // set up a task for removing and reapplying the gag glamours, and the another for updating the GagManager.
                            Logger.LogDebug($"Changing gag from {PreviousGag} to {SelectedGag}", LoggerType.GagManagement);
                            // Run this an async task here over mediator call because we need to reliably wait for the glamour to be applied.
                            _ = Task.Run(async () =>
                            {
                                // unequip the previous gag.
                                _gagManager.OnGagTypeChanged((GagLayer)slotNumber, SelectedGag, true, true);
                                await _handler.GagRemoved(PreviousGag);
                                // after its disabled, apply the new version.
                                await _handler.GagApplied(SelectedGag);
                            });
                        }
                    }, gagType);
                }

                // draw the padlock dropdown
                using (ImRaii.Disabled(currentlyLocked || gagType == GagType.None))
                {
                    _uiSharedService.DrawCombo($"Lock Type {slotNumber}", (248 - _uiSharedService.GetIconButtonSize(FontAwesomeIcon.Lock).X),
                    GenericHelpers.NoOwnerPadlockList, (padlock) => padlock.ToName(),
                    (i) =>
                    {
                        _gagManager.ActiveSlotPadlocks[slotNumber] = i;
                    }, padlockType, false);
                }
                ImGui.SameLine(0, 2);

                using (var padlockDisabled = ImRaii.Disabled(padlockType == Padlocks.None))
                {
                    // draw the lock button
                    if (_uiSharedService.IconButton(currentlyLocked ? FontAwesomeIcon.Unlock : FontAwesomeIcon.Lock, null, slotNumber.ToString()))
                    {
                        if (_gagManager.PasswordValidated(slotNumber, currentlyLocked))
                        {
                            Mediator.Publish(new GagLockToggle(
                                new PadlockData(
                                    (GagLayer)slotNumber,
                                    _gagManager.ActiveSlotPadlocks[slotNumber],
                                    _gagManager.ActiveSlotPasswords[slotNumber],
                                    UiSharedService.GetEndTimeUTC(_gagManager.ActiveSlotTimers[slotNumber]),
                                    Globals.SelfApplied),
                                currentlyLocked ? NewState.Unlocked : NewState.Locked));
                        }
                        else
                        {
                            // reset the inputs
                            _gagManager.ResetInputs();
                            // if the padlock was a timer padlock and we are currently locked trying to unlock, fire the event for it.
                            if (padlockType is Padlocks.PasswordPadlock or Padlocks.TimerPasswordPadlock or Padlocks.CombinationPadlock)
                                UnlocksEventManager.AchievementEvent(UnlocksEvent.GagUnlockGuessFailed);
                        }
                    }
                    UiSharedService.AttachToolTip(currentlyLocked ? "Attempt Unlocking " : "Lock " + "this gag.");
                }
                // display associated password field for padlock type.
                _gagManager.DisplayPadlockFields(slotNumber, currentlyLocked);
            }
            // Display lock image if we should
            if (padlockType != Padlocks.None && currentlyLocked)
            {
                ImGui.SameLine();
                using (var lockGroup = ImRaii.Group())
                {
                    var lockTexture = _uiSharedService.GetImageFromDirectoryFile(lockTypePath);
                    if (!(lockTexture is { } wrapLock))
                    {
                        Logger.LogWarning("Failed to render image!");
                    }
                    else
                    {
                        ImGui.Image(wrapLock.ImGuiHandle, new Vector2(80, 80));
                        if (ImGui.IsItemHovered())
                        {
                            ImGui.BeginTooltip();
                            ImGui.Text("Locked with a " + padlockType.ToString());
                            ImGui.EndTooltip();
                        }
                    }
                }
            }
        }
    }
    private void DisplayTimeLeft(DateTimeOffset endTime, Padlocks padlock, string userWhoSetLock, float yPos)
    {
        var prefixText = userWhoSetLock != Globals.SelfApplied
            ? userWhoSetLock +"'s " : (padlock is Padlocks.MimicPadlock ? "The Devious " : "Self-Applied ");
        var gagText = padlock.ToName() + " has";
        var color = ImGuiColors.ParsedGold;
        switch(padlock)
        {
            case Padlocks.MetalPadlock:
            case Padlocks.CombinationPadlock:
            case Padlocks.PasswordPadlock:
            case Padlocks.FiveMinutesPadlock:
            case Padlocks.TimerPasswordPadlock:
                color = ImGuiColors.ParsedGold; break;
            case Padlocks.OwnerPadlock:
            case Padlocks.OwnerTimerPadlock:
                color = ImGuiColors.ParsedPink; break;
            case Padlocks.DevotionalPadlock:
            case Padlocks.DevotionalTimerPadlock:
                color = ImGuiColors.TankBlue; break;
            case Padlocks.MimicPadlock:
                color = ImGuiColors.ParsedGreen; break;
        }
        ImGui.SameLine();
        ImGui.SetCursorPosY(yPos);
        UiSharedService.ColorText(prefixText + gagText, color);
        ImUtf8.SameLineInner();
        ImGui.SetCursorPosY(yPos);
        UiSharedService.DrawTimeLeftFancy(endTime, color);
    }
}
