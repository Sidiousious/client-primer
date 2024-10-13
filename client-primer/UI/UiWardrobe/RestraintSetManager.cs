using Dalamud.Interface;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using GagSpeak.GagspeakConfiguration.Models;
using GagSpeak.Interop.Ipc;
using GagSpeak.PlayerData.Data;
using GagSpeak.PlayerData.Handlers;
using GagSpeak.Services.Mediator;
using GagSpeak.UI.Components;
using GagSpeak.Utils;
using ImGuiNET;
using OtterGui.Classes;
using OtterGui.Text;
using Penumbra.GameData.Enums;
using System.Numerics;

namespace GagSpeak.UI.UiWardrobe;

public class RestraintSetManager : DisposableMediatorSubscriberBase
{
    private readonly UiSharedService _uiShared;
    private readonly IpcCallerGlamourer _ipcGlamourer;
    private readonly RestraintSetEditor _editor;
    private readonly SetPreviewComponent _setPreview;
    private readonly WardrobeHandler _handler;
    private readonly GagManager _padlockHandler;

    public RestraintSetManager(ILogger<RestraintSetManager> logger,
        GagspeakMediator mediator, UiSharedService uiSharedService,
        IpcCallerGlamourer ipcGlamourer, RestraintSetEditor editor,
        SetPreviewComponent setPreview, WardrobeHandler handler,
        GagManager padlockHandler) : base(logger, mediator)
    {
        _uiShared = uiSharedService;
        _ipcGlamourer = ipcGlamourer;
        _editor = editor;
        _handler = handler;
        _padlockHandler = padlockHandler;
        _setPreview = setPreview;

        CreatedRestraintSet = new RestraintSet();

        Mediator.Subscribe<RestraintSetToggledMessage>(this, (msg) => LastHoveredIndex = -1);

        Mediator.Subscribe<TooltipSetItemToRestraintSetMessage>(this, (msg) =>
        {
            if (_handler.ClonedSetForEdit is not null)
            {
                _handler.ClonedSetForEdit.DrawData[msg.Slot].GameItem = msg.Item;
                Logger.LogDebug($"Set ["+msg.Slot+"] to ["+msg.Item.Name+"] on edited set ["+_handler.ClonedSetForEdit.Name+"]", LoggerType.Restraints);
            }
            else
            {
                Logger.LogError("No Restraint Set is currently being edited.");
            }
        });
    }

    private RestraintSet CreatedRestraintSet;
    public bool CreatingRestraintSet = false;

    private int LastHoveredIndex = -1; // -1 indicates no item is currently hovered
    private LowerString RestraintSetSearchString = LowerString.Empty;
    private List<RestraintSet> FilteredSetList
    {
        get
        {
            var allSets = _handler.GetAllSetsForSearch();
            var enabledSet = allSets.FirstOrDefault(set => set.Enabled);
            var filteredSets = allSets
                .Where(set => !set.Enabled && set.Name.Contains(RestraintSetSearchString, StringComparison.OrdinalIgnoreCase))
                .ToList();

            return enabledSet != null ? new List<RestraintSet> { enabledSet }.Concat(filteredSets).ToList() : filteredSets;
        }
    }

    public void DrawManageSets(Vector2 cellPadding)
    {
        // if we are creating a pattern
        if (CreatingRestraintSet)
        {
            DrawRestraintSetCreatingHeader();
            ImGui.Separator();
            _editor.DrawRestraintSetEditor(CreatedRestraintSet, cellPadding);
            return; // perform early returns so we dont access other methods
        }

        // if we are simply viewing the main page       
        if (_handler.ClonedSetForEdit is null)
        {
            DrawSetListing(cellPadding);
            return; // perform early returns so we dont access other methods
        }

        // if we are editing an restraintSet
        if (_handler.ClonedSetForEdit is not null)
        {
            DrawRestraintSetEditorHeader();
            ImGui.Separator();
            if (_handler.RestraintSetCount > 0 && _handler.ClonedSetForEdit is not null)
            {
                _editor.DrawRestraintSetEditor(_handler.ClonedSetForEdit, cellPadding);
            }
        }
    }

    private void DrawSetListing(Vector2 cellPadding)
    {
        var region = ImGui.GetContentRegionAvail();
        var topLeftSideHeight = region.Y;

        using (var managerTable = ImRaii.Table("RestraintsManagerTable", 2, ImGuiTableFlags.SizingFixedFit | ImGuiTableFlags.BordersInnerV))
        {
            if (!managerTable) return;
            // setup the columns
            ImGui.TableSetupColumn("SetList", ImGuiTableColumnFlags.WidthFixed, 300f);
            ImGui.TableSetupColumn("PreviewSet", ImGuiTableColumnFlags.WidthStretch);

            ImGui.TableNextRow(); ImGui.TableNextColumn();

            var regionSize = ImGui.GetContentRegionAvail();

            using (var leftChild = ImRaii.Child($"###SelectableListWardrobe", regionSize with { Y = topLeftSideHeight }, false, ImGuiWindowFlags.NoDecoration))
            {
                DrawCreateRestraintSetHeader();
                ImGui.Separator();
                DrawSearchFilter(regionSize.X, ImGui.GetStyle().ItemInnerSpacing.X);
                ImGui.Separator();
                if (_handler.RestraintSetCount > 0)
                {
                    DrawRestraintSetSelectableMenu();
                }
            }

            ImGui.TableNextColumn();

            regionSize = ImGui.GetContentRegionAvail();

            using (var rightChild = ImRaii.Child($"###WardrobeSetPreview", regionSize with { Y = topLeftSideHeight }, false, ImGuiWindowFlags.NoDecoration))
            {
                var startYpos = ImGui.GetCursorPosY();
                Vector2 textSize;
                using (_uiShared.UidFont.Push()) { textSize = ImGui.CalcTextSize("Set Preview"); }

                using (ImRaii.Child("PreviewRestraintSetChild", new Vector2(UiSharedService.GetWindowContentRegionWidth(), 47)))
                {
                    // now calculate it so that the cursors Yposition centers the button in the middle height of the text
                    ImGui.SetCursorPosX(ImGui.GetCursorPosX() + (ImGui.GetContentRegionAvail().X / 2 - textSize.X / 2));
                    ImGui.SetCursorPosY(startYpos + 3f);
                    _uiShared.BigText("Set Preview");
                }
                ImGui.Separator();

                var previewRegion = new Vector2(ImGui.GetContentRegionAvail().X - ImGui.GetStyle().WindowPadding.X, ImGui.GetContentRegionAvail().Y);
                if (LastHoveredIndex != -1 && LastHoveredIndex < FilteredSetList.Count)
                {
                    _setPreview.DrawRestraintSetPreviewCentered(FilteredSetList[LastHoveredIndex], previewRegion);
                }
                else if (_handler.ActiveSet != null)
                {
                    _setPreview.DrawRestraintSetPreviewCentered(_handler.ActiveSet, previewRegion);
                }
            }
        }
    }

    private void DrawCreateRestraintSetHeader()
    {
        // use button wrounding
        using var rounding = ImRaii.PushStyle(ImGuiStyleVar.FrameRounding, 12f);
        var startYpos = ImGui.GetCursorPosY();
        var iconSize = _uiShared.GetIconButtonSize(FontAwesomeIcon.Plus);
        Vector2 textSize;
        using (_uiShared.UidFont.Push())
        {
            textSize = ImGui.CalcTextSize("New RestraintSet");
        }
        var centerYpos = (textSize.Y - iconSize.Y);

        using (ImRaii.Child("CreateRestraintSetHeader", new Vector2(UiSharedService.GetWindowContentRegionWidth(), iconSize.Y + (centerYpos - startYpos) * 2)))
        {
            // now calculate it so that the cursors Yposition centers the button in the middle height of the text
            ImGui.SameLine(10 * ImGuiHelpers.GlobalScale);
            ImGui.SetCursorPosY(ImGui.GetCursorPosY() + centerYpos);
            // draw out the icon button
            if (_uiShared.IconButton(FontAwesomeIcon.Plus))
            {
                // reset the createdRestraintSet to a new restraintSet, and set editing restraintSet to true
                CreatedRestraintSet = new RestraintSet();
                CreatingRestraintSet = true;
            }
            UiSharedService.AttachToolTip("Create a new Restraint Set");
            // now next to it we need to draw the header text
            ImGui.SameLine(10 * ImGuiHelpers.GlobalScale + iconSize.X + ImGui.GetStyle().ItemSpacing.X);
            ImGui.SetCursorPosY(startYpos);
            _uiShared.BigText("New RestraintSet");
        }
    }

    private void DrawRestraintSetCreatingHeader()
    {
        // use button wrounding
        using var rounding = ImRaii.PushStyle(ImGuiStyleVar.FrameRounding, 12f);
        var startYpos = ImGui.GetCursorPosY();
        var iconSize = _uiShared.GetIconButtonSize(FontAwesomeIcon.Plus);
        Vector2 textSize;
        using (_uiShared.UidFont.Push())
        {
            textSize = ImGui.CalcTextSize($"Creating RestraintSet: {CreatedRestraintSet.Name}");
        }
        var centerYpos = (textSize.Y - iconSize.Y);
        using (ImRaii.Child("EditRestraintSetHeader", new Vector2(UiSharedService.GetWindowContentRegionWidth(), iconSize.Y + (centerYpos - startYpos) * 2)))
        {
            // now calculate it so that the cursors Yposition centers the button in the middle height of the text
            ImGui.SameLine(10 * ImGuiHelpers.GlobalScale);
            ImGui.SetCursorPosY(ImGui.GetCursorPosY() + centerYpos);

            // the "fuck go back" button.
            if (_uiShared.IconButton(FontAwesomeIcon.ArrowLeft))
            {
                // reset the createdRestraintSet to a new restraintSet, and set editing restraintSet to true
                CreatedRestraintSet = new RestraintSet();
                CreatingRestraintSet = false;
            }
            UiSharedService.AttachToolTip("Exit to Restraint Set List");
            // now next to it we need to draw the header text
            ImGui.SameLine(10 * ImGuiHelpers.GlobalScale + iconSize.X + ImGui.GetStyle().ItemSpacing.X);
            ImGui.SetCursorPosY(startYpos);
            using (_uiShared.UidFont.Push())
            {
                UiSharedService.ColorText(CreatedRestraintSet.Name, ImGuiColors.ParsedPink);
            }

            // now calculate it so that the cursors Yposition centers the button in the middle height of the text
            ImGui.SameLine(ImGui.GetWindowContentRegionMin().X + UiSharedService.GetWindowContentRegionWidth() - iconSize.X - ImGui.GetStyle().ItemSpacing.X);
            ImGui.SetCursorPosY(ImGui.GetCursorPosY() + centerYpos);
            // the "fuck go back" button.
            using (var disabled = ImRaii.Disabled(CreatedRestraintSet.Name == string.Empty))
            {
                if (_uiShared.IconButton(FontAwesomeIcon.Save))
                {
                    // add the newly created restraintSet to the list of restraintSets
                    _handler.AddNewRestraintSet(CreatedRestraintSet);
                    // reset to default and turn off creating status.
                    CreatedRestraintSet = new RestraintSet();
                    CreatingRestraintSet = false;
                }
                UiSharedService.AttachToolTip("Save and Create Restraint Set");
            }
        }
    }

    private void DrawRestraintSetEditorHeader()
    {
        // use button wrounding
        using var rounding = ImRaii.PushStyle(ImGuiStyleVar.FrameRounding, 12f);
        var startYpos = ImGui.GetCursorPosY();
        var importSize = _uiShared.GetIconTextButtonSize(FontAwesomeIcon.FileImport, "Import Gear");
        var iconSize = _uiShared.GetIconButtonSize(FontAwesomeIcon.Plus);
        Vector2 textSize;
        using (_uiShared.UidFont.Push())
        {
            textSize = ImGui.CalcTextSize($"{_handler.ClonedSetForEdit.Name}");
        }
        var centerYpos = (textSize.Y - iconSize.Y);
        using (ImRaii.Child("EditRestraintSetHeader", new Vector2(UiSharedService.GetWindowContentRegionWidth(), iconSize.Y + (centerYpos - startYpos) * 2)))
        {
            // now calculate it so that the cursors Yposition centers the button in the middle height of the text
            ImGui.SameLine(10 * ImGuiHelpers.GlobalScale);
            ImGui.SetCursorPosY(ImGui.GetCursorPosY() + centerYpos);

            // the "fuck go back" button.
            if (_uiShared.IconButton(FontAwesomeIcon.ArrowLeft))
            {
                // reset the createdRestraintSet to a new restraintSet, and set editing restraintSet to true
                _handler.CancelEditingSet();
                return;
            }
            UiSharedService.AttachToolTip("Revert edits and return to Restraint Set List");
            // now next to it we need to draw the header text
            ImGui.SameLine(10 * ImGuiHelpers.GlobalScale + iconSize.X + ImGui.GetStyle().ItemSpacing.X);
            ImGui.SetCursorPosY(startYpos);
            using (_uiShared.UidFont.Push())
            {
                UiSharedService.ColorText(_handler.ClonedSetForEdit.Name, ImGuiColors.ParsedPink);
            }

            // now calculate it so that the cursors Yposition centers the button in the middle height of the text
            float width = ImGui.GetWindowContentRegionMin().X + UiSharedService.GetWindowContentRegionWidth() - importSize - iconSize.X * 2 - ImGui.GetStyle().ItemSpacing.X * 3;
            ImGui.SameLine(width);

            ImGui.SetCursorPosY(ImGui.GetCursorPosY() + centerYpos);
            var currentYpos = ImGui.GetCursorPosY();
            // draw revert button at the same location but right below that button
            if (_uiShared.IconTextButton(FontAwesomeIcon.FileImport, "Import Gear", 
                disabled: !IpcCallerGlamourer.APIAvailable || _handler.ClonedSetForEdit is null || !UiSharedService.CtrlPressed()))
            {
                _ipcGlamourer.SetRestraintEquipmentFromState(_handler.ClonedSetForEdit!);
                Logger.LogDebug("EquipmentImported from current State");
            }
            UiSharedService.AttachToolTip("Imports your Actor's Equipment Data from your current appearance.");

            ImGui.SameLine();
            ImGui.SetCursorPosY(currentYpos);
            // for saving contents
            if (_uiShared.IconButton(FontAwesomeIcon.Save))
            {
                // Save the changes from our edits and apply them to the set we cloned for edits
                _handler.SaveEditedSet();
            }
            UiSharedService.AttachToolTip("Save changes to Restraint Set & Return to the main list");

            // right beside it to the right, we need to draw the delete button
            ImGui.SameLine();
            ImGui.SetCursorPosY(currentYpos);
            if (_uiShared.IconButton(FontAwesomeIcon.Trash, null, null, !UiSharedService.CtrlPressed()))
            {
                // reset the createdPattern to a new pattern, and set editing pattern to true
                _handler.RemoveRestraintSet(_handler.ClonedSetForEdit!.RestraintId);
            }
            UiSharedService.AttachToolTip("Delete Restraint Set");
        }
    }

    /// <summary> Draws the search filter for our user pair list (whitelist) </summary>
    public void DrawSearchFilter(float availableWidth, float spacingX)
    {
        var buttonSize = _uiShared.GetIconTextButtonSize(FontAwesomeIcon.Ban, "Clear");
        ImGui.SetNextItemWidth(availableWidth - buttonSize - spacingX);
        string filter = RestraintSetSearchString;
        if (ImGui.InputTextWithHint("##RestraintFilter", "Search for Restraint Set", ref filter, 255))
        {
            RestraintSetSearchString = filter;
            LastHoveredIndex = -1;
        }
        ImUtf8.SameLineInner();
        using var disabled = ImRaii.Disabled(string.IsNullOrEmpty(RestraintSetSearchString));
        if (_uiShared.IconTextButton(FontAwesomeIcon.Ban, "Clear"))
        {
            RestraintSetSearchString = string.Empty;
            LastHoveredIndex = -1;
        }
    }

    private void DrawRestraintSetSelectableMenu()
    {
        // display the selectable for each restraintSet using a for loop to keep track of the index
        for (int i = 0; i < FilteredSetList.Count; i++)
        {
            var set = FilteredSetList[i];
            DrawRestraintSetSelectable(set, i);

            if (ImGui.IsItemHovered())
                LastHoveredIndex = i;

            // if the item is right clicked, open the popup
            if (ImGui.IsItemClicked(ImGuiMouseButton.Right) && LastHoveredIndex == i && !FilteredSetList[i].Enabled)
            {
                ImGui.OpenPopup($"RestraintSetContext{i}");
            }
        }

        if (LastHoveredIndex != -1 && LastHoveredIndex < FilteredSetList.Count)
        {
            if (ImGui.BeginPopup($"RestraintSetContext{LastHoveredIndex}"))
            {
                if (ImGui.Selectable("Clone Restraint Set") && FilteredSetList[LastHoveredIndex] != null)
                {
                    _handler.CloneRestraintSet(FilteredSetList[LastHoveredIndex]);
                }
                if (ImGui.Selectable("Delete Set") && FilteredSetList[LastHoveredIndex] != null)
                {
                    _handler.RemoveRestraintSet(FilteredSetList[LastHoveredIndex].RestraintId);
                }
                ImGui.EndPopup();
            }
        }
    }

    private void DrawRestraintSetSelectable(RestraintSet set, int idx)
    {
        // grab the name of the set
        var name = set.Name;
        // grab the description of the set
        var description = set.Description;
        // grab who the set was locked by
        var lockedBy = set.LockedBy;

        // define our sizes
        var startYpos = ImGui.GetCursorPosY();
        var toggleSize = _uiShared.GetIconButtonSize(set.Enabled ? FontAwesomeIcon.ToggleOn : FontAwesomeIcon.ToggleOff);
        var lockSize = _uiShared.GetIconButtonSize(set.Locked ? FontAwesomeIcon.Lock : FontAwesomeIcon.Unlock);
        var nameTextSize = ImGui.CalcTextSize(set.Name);
        var descriptionTextSize = ImGui.CalcTextSize(set.Description);
        var lockedByTextSize = ImGui.CalcTextSize(lockedBy);

        // determine the height of this selection and what kind of selection it is.
        var isActiveSet = (set.Enabled == true);
        var isLockedSet = (set.Locked == true);

        // if it is the active set, dont push the color, otherwise push the color

        using var color = ImRaii.PushColor(ImGuiCol.ChildBg, ImGui.GetColorU32(ImGuiCol.FrameBgHovered), !isActiveSet && LastHoveredIndex == idx);
        using (ImRaii.Child($"##EditRestraintSetHeader{idx}", new Vector2(UiSharedService.GetWindowContentRegionWidth(), ImGui.GetFrameHeight() * 2 - 5f)))
        {
            var maxAllowedWidth = ImGui.GetWindowContentRegionMin().X + UiSharedService.GetWindowContentRegionWidth() - toggleSize.X - ImGui.GetStyle().ItemSpacing.X * 3;
            // create a group for the bounding area
            using (var group = ImRaii.Group())
            {
                // scooch over a bit like 5f
                ImGui.SetCursorPosX(ImGui.GetCursorPosX() + 5f);
                var originalCursorPos = ImGui.GetCursorPos();
                // Move the Y pos down a bit, only for drawing this text
                ImGui.SetCursorPosY(originalCursorPos.Y + 2.5f);
                // Draw the text with the desired color
                UiSharedService.ColorText(name, ImGuiColors.DalamudWhite2);
                if (set.LockType == Padlocks.FiveMinutesPadlock.ToName() || set.LockType == Padlocks.TimerPasswordPadlock.ToName() || set.LockType == Padlocks.OwnerTimerPadlock.ToName())
                {
                    ImGui.SameLine();
                    UiSharedService.DrawTimeLeftFancy(set.LockedUntil);
                }
                // Restore the original cursor position
                ImGui.SetCursorPos(originalCursorPos);

            }

            // now draw the lower section out.
            using (var group = ImRaii.Group())
            {
                // scooch over a bit like 5f
                ImGui.SetCursorPosX(ImGui.GetCursorPosX() + 5f);
                if (isLockedSet)
                {
                    ImGui.SetCursorPosY(ImGui.GetCursorPosY() - 2.5f);
                    UiSharedService.ColorText("Locked By:", ImGuiColors.DalamudGrey2);
                    ImGui.SameLine();
                    UiSharedService.ColorText(lockedBy, ImGuiColors.DalamudGrey3);
                }
                else
                {
                    // if the trimmed descriptions ImGui.CalcTextSize() is larger than the maxAllowedWidth, then trim it.
                    var trimmedDescription = description.Length > 50 ? description.Substring(0, 50) + "..." : description;
                    // Measure the text size
                    var textSize = ImGui.CalcTextSize(trimmedDescription).X;

                    // If the text size exceeds the maximum allowed width, trim it further
                    while (textSize > maxAllowedWidth && trimmedDescription.Length > 3)
                    {
                        trimmedDescription = trimmedDescription.Substring(0, trimmedDescription.Length - 4) + "...";
                        textSize = ImGui.CalcTextSize(trimmedDescription).X;
                    }
                    // move the Y pos up a bit.
                    ImGui.SetCursorPosY(ImGui.GetCursorPosY() - 2.5f);
                    UiSharedService.ColorText(trimmedDescription, ImGuiColors.DalamudGrey2);
                }
            }
            // now, head to the sameline of the full width minus the width of the button
            ImGui.SameLine(ImGui.GetWindowContentRegionMin().X + UiSharedService.GetWindowContentRegionWidth() - toggleSize.X - ImGui.GetStyle().ItemSpacing.X);
            ImGui.SetCursorPosY((ImGui.GetCursorPosY() - (ImGui.GetFrameHeight() * 2 - toggleSize.Y) / 2) - 2.5f);
            // draw out the icon button
            var currentYpos = ImGui.GetCursorPosY();
            using (var rounding = ImRaii.PushStyle(ImGuiStyleVar.FrameRounding, 12f))
            {
                bool disabled = FilteredSetList.Any(x => x.Locked) || !_handler.WardrobeEnabled || !_handler.RestraintSetsEnabled;
                string ttText = set.Enabled ? (set.Locked ? "Cannot Disable a Locked Set!" : "Disable Active Restraint Set")
                                            : (!_handler.WardrobeEnabled || !_handler.RestraintSetsEnabled) ? "Wardrobe / Restraint set Permissions not Active."
                                            : (disabled ? "Can't Enable another Set while active Set is Locked!" : "Enable Restraint Set");
                if (_uiShared.IconButton(set.Enabled ? FontAwesomeIcon.ToggleOn : FontAwesomeIcon.ToggleOff, null, set.Name, disabled))
                {
                    // set the enabled state of the restraintSet based on its current state so that we toggle it
                    if (set.Enabled)
                        _handler.DisableRestraintSet(_handler.GetRestraintSetIndexByName(set.Name));
                    else
                        _handler.EnableRestraintSet(_handler.GetRestraintSetIndexByName(set.Name));
                    // toggle the state & early return so we dont access the child clicked button
                    return;
                }
                UiSharedService.AttachToolTip(ttText);
            }
        }
        if (!isActiveSet)
        {
            if (ImGui.IsItemClicked())
            {
                _handler.StartEditingSet(set);
            }
        }
        // if this is the active set, draw a seperator below it
        if (isActiveSet)
        {
            // obtain the width to use.
            ImGui.SetCursorPosX(ImGui.GetCursorPosX() + ImGui.GetStyle().ItemInnerSpacing.X);
            using (var group = ImRaii.Group())
            {
                var width = ImGui.GetContentRegionAvail().X - ImGui.GetStyle().ItemSpacing.X;

                TimeSpan remainingTime = (set.LockedUntil - DateTimeOffset.UtcNow);
                string remainingTimeStr = $"{remainingTime.Days}d{remainingTime.Hours}h{remainingTime.Minutes}m{remainingTime.Seconds}s";
                var lockedDescription = set.Locked ? $"Locked for {remainingTimeStr}" : "Self-lock: XdXhXmXs format..";
                // draw the padlock dropdown
                var isLockedByPair = set.LockedBy != Globals.SelfApplied && set.LockType.ToPadlock() != Padlocks.None;
                var padlockType = set.Locked ? set.LockType.ToPadlock() : _padlockHandler.ActiveSlotPadlocks[3];

                var padlockList = isLockedByPair ? GenericHelpers.NoMimicPadlockList : GenericHelpers.NoOwnerPadlockList;

                using (ImRaii.Disabled(set.Locked || set.LockType != "None"))
                {
                    _uiShared.DrawCombo("RestraintSetLock" + set.Name, (width - 1 - _uiShared.GetIconButtonSize(FontAwesomeIcon.Lock).X - ImGui.GetStyle().ItemInnerSpacing.X),
                        padlockList, (padlock) => padlock.ToName(),
                    (i) =>
                    {
                        _padlockHandler.ActiveSlotPadlocks[3] = i;
                    }, padlockType, false);

                    // if we have been locked by a pair, and our combo's selected padlock doesn't match the locked padlock, we should update it.
                    if (isLockedByPair && _uiShared.GetSelectedComboItem<Padlocks>("RestraintSetLock" + set.Name) != set.LockType.ToPadlock())
                    {
                        // update the padlock previews and selected combo item.
                        _uiShared.SetSelectedComboItem("RestraintSetLock" + set.Name, set.LockType.ToPadlock());
                        _padlockHandler.ActiveSlotPadlocks[3] = set.LockType.ToPadlock();
                    }
                }
                ImUtf8.SameLineInner();
                // draw the lock button
                if (_uiShared.IconButton(set.Locked ? FontAwesomeIcon.Lock : FontAwesomeIcon.Unlock, null, set.Name.ToString(), padlockType == Padlocks.None))
                {
                    if (_padlockHandler.RestraintPasswordValidate(set, set.Locked))
                    {
                        if (set.Locked)
                        {
                            Logger.LogTrace($"Unlocking Restraint Set {set.Name}");
                            // allow using set.EnabledBy here because it will check against the assigner when unlocking.
                            _handler.UnlockRestraintSet(_handler.GetRestraintSetIndexByName(set.Name), set.EnabledBy);
                            _padlockHandler.ActiveSlotPadlocks[3] = Padlocks.None;
                        }
                        else
                        {
                            Logger.LogTrace($"Locking Restraint Set {set.Name}");
                            Logger.LogTrace("Parsing Timer with value[" + _padlockHandler.ActiveSlotTimers[3] + "]");
                            _handler.LockRestraintSet(
                                _handler.GetRestraintSetIndexByName(set.Name),
                                _padlockHandler.ActiveSlotPadlocks[3].ToName(),
                                _padlockHandler.ActiveSlotPasswords[3],
                                UiSharedService.GetEndTimeUTC(_padlockHandler.ActiveSlotTimers[3]),
                                Globals.SelfApplied);
                        }
                    }
                    else
                    {
                        Logger.LogDebug($"Failed to validate password for Restraint Set {set.Name}");
                    }
                    // reset the password and timer
                    _padlockHandler.ResetInputs();
                }
                UiSharedService.AttachToolTip(_padlockHandler.ActiveSlotPadlocks[3] == Padlocks.None ? "Select a padlock type before locking" :
                    set.Locked == false ? "Self-Lock this Restraint Set" :
                    set.LockedBy != Globals.SelfApplied ? "Only" + set.LockedBy + "can unlock your set." : "Unlock this set.");
                // display associated password field for padlock type.
                _padlockHandler.DisplayPadlockFields(3, set.Locked, width);
            }
            ImGui.Separator();
        }
    }
}
