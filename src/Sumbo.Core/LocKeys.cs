namespace Sumbo.Core;

/// <summary>
/// Canonical localization key IDs. Code references these <c>const</c>s at compile time while the
/// translated values live in the embedded <c>lang.{ko,en}.json</c> catalogs — so a translator edits
/// language JSON alone without touching C#. <see cref="All"/> is the authoritative key set that
/// <c>LocalizationCatalogTests</c> asserts every language table covers (drift guard).
/// <para>
/// Format keys use numbered <c>{0}</c>/<c>{1}</c> placeholders (consumed by
/// <see cref="LocalizationCatalog.Format"/> → <see cref="string.Format(string, object?[])"/>), never C#
/// interpolation, so the substitution survives the catalog lookup. Windows mnemonic accelerators
/// (e.g. <c>(&amp;W)</c>) are carried inside each language value.
/// </para>
/// </summary>
public static class LocKeys
{
    // ── App-wide ──
    public const string App_Title = "app.title"; // window title + tray tooltip (brand — same in every language)

    // ── Menu / action labels (mirror right-click menu; reused by panel buttons) ──
    public const string Menu_Target = "menu.target";
    public const string Menu_Size_Source = "menu.size.source";
    public const string Menu_Size_Half = "menu.size.half";
    public const string Menu_Size_Quarter = "menu.size.quarter";
    public const string Menu_Size_Fullscreen = "menu.size.fullscreen";
    public const string Menu_Anchor_None = "menu.anchor.none";
    public const string Menu_Anchor_TopLeft = "menu.anchor.topLeft";
    public const string Menu_Anchor_TopRight = "menu.anchor.topRight";
    public const string Menu_Anchor_BottomLeft = "menu.anchor.bottomLeft";
    public const string Menu_Anchor_BottomRight = "menu.anchor.bottomRight";
    public const string Menu_Anchor_Top = "menu.anchor.top";
    public const string Menu_Anchor_Bottom = "menu.anchor.bottom";
    public const string Menu_Anchor_Left = "menu.anchor.left";
    public const string Menu_Anchor_Right = "menu.anchor.right";
    public const string Menu_Mode_ClickForward = "menu.mode.clickForward";
    public const string Menu_Mode_ClickThrough = "menu.mode.clickThrough";
    public const string Menu_Mode_Lock = "menu.mode.lock";
    public const string Menu_Mode_Border = "menu.mode.border";
    public const string Menu_Region_Select = "menu.region.select";
    public const string Menu_Region_Clear = "menu.region.clear";
    public const string Menu_Region_Save = "menu.region.save";
    public const string Menu_Region_Saved = "menu.region.saved";
    public const string Menu_Profile_Save = "menu.profile.save";
    public const string Menu_Profile_Load = "menu.profile.load";
    public const string Menu_Group_Add = "menu.group.add";
    public const string Menu_Group_Clear = "menu.group.clear";
    public const string Menu_Group_Start = "menu.group.start";
    public const string Menu_Group_Stop = "menu.group.stop";
    public const string Menu_Settings = "menu.settings";

    // ── Disabled placeholder menu items ──
    public const string Menu_Placeholder_NoRegions = "menu.placeholder.noRegions";
    public const string Menu_Placeholder_NoProfiles = "menu.placeholder.noProfiles";

    // ── Common buttons ──
    public const string Common_Ok = "common.ok";
    public const string Common_Cancel = "common.cancel";

    // ── Name/interval prompts ──
    public const string Prompt_RegionName_Title = "prompt.regionName.title";
    public const string Prompt_RegionName_Default = "prompt.regionName.default";
    public const string Prompt_ProfileName_Title = "prompt.profileName.title";
    public const string Prompt_ProfileName_Default = "prompt.profileName.default";
    public const string Prompt_GroupInterval_Title = "prompt.groupInterval.title";

    // ── Dialogs (main window / mirror shell) ──
    public const string Dialog_CloneFailed_Caption = "dialog.cloneFailed.caption";
    public const string Dialog_CloneFailed_Body = "dialog.cloneFailed.body"; // fallback body when the failure carries no message (e.g. zero-size source)
    public const string Dialog_ClickThroughUnavailable_Body = "dialog.clickThroughUnavailable.body"; // {0} = chord
    public const string Dialog_ClickThroughUnavailable_Caption = "dialog.clickThroughUnavailable.caption";
    public const string Dialog_ClickForwardUnsupported_Body = "dialog.clickForwardUnsupported.body"; // {0} = win32 error
    public const string Dialog_ClickForwardUnsupported_Caption = "dialog.clickForwardUnsupported.caption";
    public const string Dialog_RegionSaveFailed_Caption = "dialog.regionSaveFailed.caption";
    public const string Dialog_ProfileSaveFailed_Caption = "dialog.profileSaveFailed.caption";
    public const string Dialog_ProfileRestore_Body = "dialog.profileRestore.body"; // {0} = profile name
    public const string Dialog_ProfileRestore_Caption = "dialog.profileRestore.caption";
    public const string Dialog_GroupEmpty_Body = "dialog.groupEmpty.body";
    public const string Dialog_GroupSingle_Body = "dialog.groupSingle.body";
    public const string Dialog_GroupSwitch_Caption = "dialog.groupSwitch.caption";
    public const string Dialog_GroupAllMissing_Body = "dialog.groupAllMissing.body";
    public const string Dialog_GroupMemberMissing_Body = "dialog.groupMemberMissing.body"; // {0} = member title

    // ── Startup / global dialogs (Program, SumboAppContext, CloneManager) ──
    public const string Dialog_DwmDisabled_Body = "dialog.dwmDisabled.body";
    public const string Dialog_DwmDisabled_Caption = "dialog.dwmDisabled.caption";
    public const string Dialog_HotkeyConflict_Body = "dialog.hotkeyConflict.body"; // {0} = failed chords
    public const string Dialog_HotkeyConflict_Caption = "dialog.hotkeyConflict.caption";
    public const string Dialog_AutoStartFailed_Body = "dialog.autoStartFailed.body"; // {0} = exception message
    public const string Dialog_AutoStartFailed_Caption = "dialog.autoStartFailed.caption";

    // ── Tray menu (TrayHost) ──
    public const string Tray_ToggleVisible = "tray.toggleVisible";
    public const string Tray_AutoStart = "tray.autoStart";
    public const string Tray_MinimizeToTray = "tray.minimizeToTray";
    public const string Tray_Exit = "tray.exit";
    public const string Tray_ResidentNotice_Title = "tray.residentNotice.title"; // one-time balloon when close hides to tray
    public const string Tray_ResidentNotice_Body = "tray.residentNotice.body";

    // ── Settings panel (language / startup) ──
    public const string Settings_Section_Language = "settings.section.language";
    public const string Settings_Language_Ko = "settings.language.ko";
    public const string Settings_Language_En = "settings.language.en";
    public const string Settings_Section_Startup = "settings.section.startup";

    // ── Main window control panel ──
    public const string Main_SearchPlaceholder = "main.searchPlaceholder";
    public const string Main_StatusRunning = "main.statusRunning";
    public const string Main_HideUi = "main.hideUi";
    public const string Main_Opacity = "main.opacity";
    public const string Main_Display_Title = "main.display.title";
    public const string Main_Display_Subtitle = "main.display.subtitle";
    public const string Main_Behavior_Subtitle = "main.behavior.subtitle";
    public const string Main_Display_Size = "main.display.size";
    public const string Main_Display_Anchor = "main.display.anchor";
    public const string Main_Anchor_Center = "main.anchor.center";
    public const string Main_Display_AlwaysOnTop = "main.display.alwaysOnTop";
    public const string Main_Display_HideUi_Hint = "main.display.hideUi.hint"; // hint under the hide-UI button (ESC restores)
    public const string Main_Mode_ClickForward_Desc = "main.mode.clickForward.desc";
    public const string Main_Mode_ClickThrough_Desc = "main.mode.clickThrough.desc";
    public const string Main_Mode_Lock_Desc = "main.mode.lock.desc";
    public const string Main_Mode_Border_Desc = "main.mode.border.desc";
    public const string Main_Mode_Aot_Desc = "main.mode.aot.desc";
    public const string Main_Nav_Region = "main.nav.region";
    public const string Main_Nav_Behavior = "main.nav.behavior";
    public const string Main_Nav_Hotkeys = "main.nav.hotkeys";
    public const string Main_Nav_Group = "main.nav.group";
    public const string Main_Nav_Settings = "main.nav.settings";
    public const string Main_Nav_About = "main.nav.about";

    // ── Single-window shell ──
    public const string Main_Nav_Targets = "main.nav.targets";     // rail targets icon + targets panel title
    public const string Main_Nav_Profiles = "main.nav.profiles";   // rail profiles icon
    public const string Main_StopMirror = "main.stopMirror";       // stop-mirroring button at the bottom of the targets panel
    public const string Main_Mirror_Hint = "main.mirror.hint";     // idle canvas hint ({0} = pick-window chord)

    // ── Region / profiles panels ──
    public const string Main_Region_Subtitle = "main.region.subtitle";       // region panel guidance (drag usage)
    public const string Main_Region_Current = "main.region.current";         // current-region readout label
    public const string Main_Profiles_Subtitle = "main.profiles.subtitle";   // profiles panel guidance
    public const string Main_Item_Apply = "main.item.apply";                 // saved-list row apply button (delete button is glyph-only)
    public const string Main_ConfirmDelete_Body = "main.confirmDelete.body"; // {0} = item name
    public const string Main_ConfirmDelete_Caption = "main.confirmDelete.caption";

    // ── Group / hotkeys / settings / about panels ──
    public const string Main_Settings_Subtitle = "main.settings.subtitle";   // settings panel guidance
    public const string Main_Group_Subtitle = "main.group.subtitle";         // group rotation panel guidance
    public const string Main_Group_Members = "main.group.members";           // group member list label
    public const string Main_Group_Empty = "main.group.empty";               // empty-group notice
    public const string Main_Hotkeys_Subtitle = "main.hotkeys.subtitle";     // hotkeys panel guidance
    public const string Main_Hotkeys_Conflict = "main.hotkeys.conflict";     // registration-conflict marker
    public const string Main_Hotkey_ToggleVisible = "main.hotkey.toggleVisible";
    public const string Main_Hotkey_PickWindow = "main.hotkey.pickWindow";
    public const string Main_Hotkey_ClickThrough = "main.hotkey.clickThrough";
    public const string Main_Hotkey_OpacityUp = "main.hotkey.opacityUp";
    public const string Main_Hotkey_OpacityDown = "main.hotkey.opacityDown";
    public const string Main_Hotkey_RegionSelect = "main.hotkey.regionSelect";
    public const string Main_Hotkey_GroupSwitch = "main.hotkey.groupSwitch";
    public const string Main_About_Subtitle = "main.about.subtitle";         // tagline / description
    public const string Main_About_Version = "main.about.version";           // "Version {0}" ({0} = version)
    public const string Main_About_UpdateNote = "main.about.updateNote";     // notice that auto-update is not available yet
    public const string Main_About_UpdateCheck = "main.about.updateCheck";   // opens the releases page to check for a newer version
    public const string Main_About_SupportNote = "main.about.supportNote";   // donation invitation line
    public const string Main_About_Support = "main.about.support";           // opens GitHub Sponsors
    public const string Main_About_License = "main.about.license";           // license + copyright line
    public const string Main_About_Source = "main.about.source";             // opens the source repository

    /// <summary>Every key above — the authoritative set each language catalog must fully cover.</summary>
    public static readonly string[] All =
    {
        App_Title,
        Menu_Target, Menu_Size_Source, Menu_Size_Half, Menu_Size_Quarter, Menu_Size_Fullscreen,
        Menu_Anchor_None, Menu_Anchor_TopLeft, Menu_Anchor_TopRight,
        Menu_Anchor_BottomLeft, Menu_Anchor_BottomRight, Menu_Anchor_Top, Menu_Anchor_Bottom,
        Menu_Anchor_Left, Menu_Anchor_Right, Menu_Mode_ClickForward,
        Menu_Mode_ClickThrough, Menu_Mode_Lock, Menu_Mode_Border, Menu_Region_Select,
        Menu_Region_Clear, Menu_Region_Save, Menu_Region_Saved, Menu_Profile_Save,
        Menu_Profile_Load, Menu_Group_Add, Menu_Group_Clear,
        Menu_Group_Start, Menu_Group_Stop, Menu_Settings,
        Menu_Placeholder_NoRegions, Menu_Placeholder_NoProfiles,
        Common_Ok, Common_Cancel,
        Prompt_RegionName_Title, Prompt_RegionName_Default, Prompt_ProfileName_Title,
        Prompt_ProfileName_Default, Prompt_GroupInterval_Title,
        Dialog_CloneFailed_Caption, Dialog_CloneFailed_Body, Dialog_ClickThroughUnavailable_Body, Dialog_ClickThroughUnavailable_Caption,
        Dialog_ClickForwardUnsupported_Body, Dialog_ClickForwardUnsupported_Caption, Dialog_RegionSaveFailed_Caption,
        Dialog_ProfileSaveFailed_Caption, Dialog_ProfileRestore_Body, Dialog_ProfileRestore_Caption,
        Dialog_GroupEmpty_Body, Dialog_GroupSingle_Body, Dialog_GroupSwitch_Caption, Dialog_GroupAllMissing_Body,
        Dialog_GroupMemberMissing_Body,
        Dialog_DwmDisabled_Body, Dialog_DwmDisabled_Caption, Dialog_HotkeyConflict_Body,
        Dialog_HotkeyConflict_Caption, Dialog_AutoStartFailed_Body, Dialog_AutoStartFailed_Caption,
        Tray_ToggleVisible, Tray_AutoStart, Tray_MinimizeToTray, Tray_Exit,
        Tray_ResidentNotice_Title, Tray_ResidentNotice_Body,
        Settings_Section_Language, Settings_Language_Ko, Settings_Language_En, Settings_Section_Startup,
        Main_SearchPlaceholder, Main_StatusRunning,
        Main_HideUi, Main_Opacity, Main_Display_Title,
        Main_Display_Subtitle, Main_Behavior_Subtitle, Main_Display_Size, Main_Display_Anchor, Main_Anchor_Center,
        Main_Display_AlwaysOnTop,
        Main_Display_HideUi_Hint, Main_Mode_ClickForward_Desc,
        Main_Mode_ClickThrough_Desc, Main_Mode_Lock_Desc, Main_Mode_Border_Desc, Main_Mode_Aot_Desc,
        Main_Nav_Region, Main_Nav_Behavior,
        Main_Nav_Hotkeys, Main_Nav_Group, Main_Nav_Settings, Main_Nav_About,
        Main_Nav_Targets, Main_Nav_Profiles, Main_StopMirror, Main_Mirror_Hint,
        Main_Region_Subtitle, Main_Region_Current, Main_Profiles_Subtitle, Main_Item_Apply,
        Main_ConfirmDelete_Body, Main_ConfirmDelete_Caption,
        Main_Settings_Subtitle, Main_Group_Subtitle, Main_Group_Members, Main_Group_Empty,
        Main_Hotkeys_Subtitle, Main_Hotkeys_Conflict,
        Main_Hotkey_ToggleVisible, Main_Hotkey_PickWindow, Main_Hotkey_ClickThrough,
        Main_Hotkey_OpacityUp, Main_Hotkey_OpacityDown, Main_Hotkey_RegionSelect, Main_Hotkey_GroupSwitch,
        Main_About_Subtitle, Main_About_Version, Main_About_UpdateNote,
        Main_About_UpdateCheck, Main_About_SupportNote, Main_About_Support, Main_About_License, Main_About_Source,
    };
}
