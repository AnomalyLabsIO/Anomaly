namespace Anomaly.Launcher.Services.Localization;

/// <summary>
/// Compile-time constants for every translation key used by the Launcher.
/// Keeping these as constants catches typos at compile time.
/// </summary>
internal static class TrKeys
{
    // MainWindow — username
    public const string MainWindow_Welcome_Prefix = "launcher.mainwindow.welcome_prefix";
    public const string MainWindow_Username_Tooltip = "launcher.mainwindow.username_tooltip";
    public const string MainWindow_Username_Label = "launcher.mainwindow.username_label";
    public const string MainWindow_Username_Watermark = "launcher.mainwindow.username_watermark";
    public const string MainWindow_Username_Save = "launcher.mainwindow.username_save";
    public const string MainWindow_Username_Cancel = "launcher.mainwindow.username_cancel";
    public const string MainWindow_Username_CopyUserId = "launcher.mainwindow.username_copy_user_id";

    // MainWindow — bottom bar
    public const string MainWindow_LaunchVanilla = "launcher.mainwindow.launch_vanilla";
    public const string MainWindow_LaunchModded = "launcher.mainwindow.launch_modded";
    public const string MainWindow_KillGame = "launcher.mainwindow.kill_game";
    public const string MainWindow_Refresh = "launcher.mainwindow.refresh";
    public const string MainWindow_LaunchArgs_Watermark = "launcher.mainwindow.launch_args_watermark";
    public const string MainWindow_LaunchArgs_Tooltip = "launcher.mainwindow.launch_args_tooltip";

    // MainWindow — mods panel
    public const string MainWindow_Mods_Header = "launcher.mainwindow.mods_header";

    // State 1: Game Not Installed
    public const string State1_Header = "launcher.state1.header";
    public const string State1_DropZone_Primary = "launcher.state1.drop_zone_primary";
    public const string State1_DropZone_Secondary = "launcher.state1.drop_zone_secondary";
    public const string State1_SetPath = "launcher.state1.set_path";
    public const string State1_Path_Watermark = "launcher.state1.path_watermark";
    public const string State1_InstallGame = "launcher.state1.install_game";

    // State 2: Vanilla Updated
    public const string State2_Header = "launcher.state2.header";
    public const string State2_Body = "launcher.state2.body";
    public const string State2_SyncAnomaly = "launcher.state2.sync_anomaly";

    // State 3: Online Build Mismatch
    public const string State3_Header = "launcher.state3.header";
    public const string State3_YourBuild = "launcher.state3.your_build";
    public const string State3_LatestBuild = "launcher.state3.latest_build";
    public const string State3_Update = "launcher.state3.update";

    // State 4: All Good
    public const string State4_Header = "launcher.state4.header";
    public const string State4_ValidateFiles = "launcher.state4.validate_files";
    public const string State4_SyncAnomaly = "launcher.state4.sync_anomaly";

    // Info panel
    public const string InfoPanel_Header = "launcher.info.header";
    public const string InfoPanel_Branch = "launcher.info.branch";
    public const string InfoPanel_LocalBuildId = "launcher.info.local_build_id";
    public const string InfoPanel_OnlineBuildId = "launcher.info.online_build_id";
    public const string InfoPanel_OnlineChecking = "launcher.info.online_checking";
    public const string InfoPanel_MelonLoader = "launcher.info.melonloader";
    public const string InfoPanel_VanillaGaDate = "launcher.info.vanilla_ga_date";
    public const string InfoPanel_ModdedGaDate = "launcher.info.modded_ga_date";
    public const string InfoPanel_Unknown = "launcher.info.unknown";
    public const string InfoPanel_NotAvailable = "launcher.info.not_available";
    public const string InfoPanel_NotInstalled = "launcher.info.not_installed";

    // Status messages
    public const string Status_Ready = "launcher.status.ready";
    public const string Status_UserIdCopied = "launcher.status.user_id_copied";
    public const string Status_NoIdentity = "launcher.status.no_identity";
    public const string Status_GameRunningVanilla = "launcher.status.game_running_vanilla";
    public const string Status_GameRunningModded = "launcher.status.game_running_modded";
    public const string Status_FailedLaunchVanilla = "launcher.status.failed_launch_vanilla";
    public const string Status_FailedLaunchModded = "launcher.status.failed_launch_modded";
    public const string Status_WineMissing = "launcher.status.wine_missing";
    public const string Status_ExeMissing = "launcher.status.exe_missing";
    public const string Status_EnterPath = "launcher.status.enter_path";
    public const string Status_ExeNotInDir = "launcher.status.exe_not_in_dir";
    public const string Status_InvalidPath = "launcher.status.invalid_path";
    public const string Status_VanillaPathSet = "launcher.status.vanilla_path_set";
    public const string Status_NoExeInDrop = "launcher.status.no_exe_in_drop";
    public const string Status_OpeningInstall = "launcher.status.opening_install";
    public const string Status_OpeningUpdate = "launcher.status.opening_update";
    public const string Status_OpeningValidate = "launcher.status.opening_validate";
    public const string Status_InstallerNotFound = "launcher.status.installer_not_found";
    public const string Status_InstallerLaunched = "launcher.status.installer_launched";
    public const string Status_FailedInstaller = "launcher.status.failed_installer";
    public const string Status_GameExitedAbnormally = "launcher.status.game_exited_abnormally";
    public const string Status_GameExitedNormally = "launcher.status.game_exited_normally";

    // Crash dialog — AXAML
    public const string Crash_OpenReportsFolder = "launcher.crash.open_reports_folder";
    public const string Crash_CopyToClipboard = "launcher.crash.copy_to_clipboard";
    public const string Crash_Close = "launcher.crash.close";

    // Crash dialog — ViewModel
    public const string Crash_Title_Single = "launcher.crash.title_single";
    public const string Crash_Title_Multiple = "launcher.crash.title_multiple";
    public const string Crash_Label_ReportId = "launcher.crash.label_report_id";
    public const string Crash_Label_Timestamp = "launcher.crash.label_timestamp";
    public const string Crash_Label_Trigger = "launcher.crash.label_trigger";
    public const string Crash_Label_Exception = "launcher.crash.label_exception";
    public const string Crash_Label_Message = "launcher.crash.label_message";
    public const string Crash_Header_StackTrace = "launcher.crash.header_stack_trace";
    public const string Crash_Header_LoadedMods = "launcher.crash.header_loaded_mods";
    public const string Crash_Header_SystemInfo = "launcher.crash.header_system_info";
    public const string Crash_None = "launcher.crash.none";
    public const string Crash_Label_OS = "launcher.crash.label_os";
    public const string Crash_Label_CPU = "launcher.crash.label_cpu";
    public const string Crash_Label_RAM = "launcher.crash.label_ram";
    public const string Crash_Label_GPU = "launcher.crash.label_gpu";
    public const string Crash_Label_Unity = "launcher.crash.label_unity";
    public const string Crash_Label_MelonLoader = "launcher.crash.label_melonloader";

    // Mod item
    public const string ModItem_VersionWarning = "launcher.mod_item.version_warning";
    public const string ModItem_UnknownVersion = "launcher.mod_item.unknown_version";

    // Steam download dialog — entry points
    public const string SteamDl_Entry_GameNotInstalled = "launcher.steamdl.entry.game_not_installed";
    public const string SteamDl_Entry_PinOlderBuild = "launcher.steamdl.entry.pin_older_build";
    public const string SteamDl_Entry_ChangeVersion = "launcher.steamdl.entry.change_version";

    // Steam download dialog — window
    public const string SteamDl_Dialog_Title = "launcher.steamdl.dialog.title";

    // Steam download dialog — page 1 (pick version)
    public const string SteamDl_Page1_Header = "launcher.steamdl.page1.header";
    public const string SteamDl_Page1_PrivacyBanner = "launcher.steamdl.page1.privacy_banner";
    public const string SteamDl_Page1_PrivacyLinkText = "launcher.steamdl.page1.privacy_link_text";
    public const string SteamDl_Page1_PickFromCatalog = "launcher.steamdl.page1.pick_from_catalog";
    public const string SteamDl_Page1_PasteManifest = "launcher.steamdl.page1.paste_manifest";
    public const string SteamDl_Page1_ManifestIdWatermark = "launcher.steamdl.page1.manifest_id_watermark";
    public const string SteamDl_Page1_OpenSteamDb = "launcher.steamdl.page1.open_steamdb";
    public const string SteamDl_Page1_ShowAllBuilds = "launcher.steamdl.page1.show_all_builds";
    public const string SteamDl_Page1_CompatibleBadge = "launcher.steamdl.page1.compatible_badge";
    public const string SteamDl_Page1_IncompatibleBadge = "launcher.steamdl.page1.incompatible_badge";
    public const string SteamDl_Page1_SignInAndDownload = "launcher.steamdl.page1.sign_in_and_download";
    public const string SteamDl_Page1_Cancel = "launcher.steamdl.page1.cancel";
    public const string SteamDl_Page1_EmptyCatalog = "launcher.steamdl.page1.empty_catalog";

    // Steam download dialog — page 2 (QR sign-in)
    public const string SteamDl_Page2_Header = "launcher.steamdl.page2.header";
    public const string SteamDl_Page2_Instructions = "launcher.steamdl.page2.instructions";
    public const string SteamDl_Page2_RememberMe = "launcher.steamdl.page2.remember_me";
    public const string SteamDl_Page2_Cancel = "launcher.steamdl.page2.cancel";

    // Steam download dialog — page 3 (progress)
    public const string SteamDl_Page3_Header = "launcher.steamdl.page3.header";
    public const string SteamDl_Page3_Cancel = "launcher.steamdl.page3.cancel";

    // Steam download dialog — done
    public const string SteamDl_Done_Header = "launcher.steamdl.done.header";
    public const string SteamDl_Done_Body = "launcher.steamdl.done.body";
    public const string SteamDl_Done_UseBuild = "launcher.steamdl.done.use_build";

    // Steam download dialog — error
    public const string SteamDl_Error_Header = "launcher.steamdl.error.header";
    public const string SteamDl_Error_TryAgain = "launcher.steamdl.error.try_again";
    public const string SteamDl_Error_Close = "launcher.steamdl.error.close";

    // Steam download dialog — status messages
    public const string SteamDl_SignIn_RefreshingToken = "launcher.steamdl.signin.refreshing_token";
    public const string SteamDl_SignIn_WaitingForChallenge = "launcher.steamdl.signin.waiting_for_challenge";
    public const string SteamDl_SignIn_WaitingForScan = "launcher.steamdl.signin.waiting_for_scan";
    public const string SteamDl_SignIn_Success = "launcher.steamdl.signin.success";
    public const string SteamDl_Download_Starting = "launcher.steamdl.download.starting";
    public const string SteamDl_Download_Progress = "launcher.steamdl.download.progress";
    public const string SteamDl_Download_Speed = "launcher.steamdl.download.speed";

    // Steam download dialog — error messages
    public const string SteamDl_Error_NoVersionSelected = "launcher.steamdl.error.no_version_selected";
    public const string SteamDl_Error_QrExpired = "launcher.steamdl.error.qr_expired";
    public const string SteamDl_Error_UserDeclined = "launcher.steamdl.error.user_declined";
    public const string SteamDl_Error_Network = "launcher.steamdl.error.network";
    public const string SteamDl_Error_TokenExpired = "launcher.steamdl.error.token_expired";
    public const string SteamDl_Error_Unexpected = "launcher.steamdl.error.unexpected";
    public const string SteamDl_Error_NotOwned = "launcher.steamdl.error.not_owned";
    public const string SteamDl_Error_DiskSpace = "launcher.steamdl.error.disk_space";
    public const string SteamDl_Error_IncompatibleConfirm = "launcher.steamdl.error.incompatible_confirm";

    // Steam download dialog — version table
    public const string SteamDl_Column_Date = "launcher.steamdl.column.date";
    public const string SteamDl_Column_ManifestId = "launcher.steamdl.column.manifest_id";
    public const string SteamDl_Column_GameVersion = "launcher.steamdl.column.game_version";
    public const string SteamDl_Column_Supported = "launcher.steamdl.column.supported";
    public const string SteamDl_Status_Supported = "launcher.steamdl.status.supported";
    public const string SteamDl_Status_Patchable = "launcher.steamdl.status.patchable";
    public const string SteamDl_Status_Unsupported = "launcher.steamdl.status.unsupported";
    public const string SteamDl_Status_Unknown = "launcher.steamdl.status.unknown";
    public const string SteamDl_Catalog_Loading = "launcher.steamdl.catalog.loading";
    public const string SteamDl_Catalog_Empty = "launcher.steamdl.catalog.empty";
    public const string SteamDl_Catalog_Refresh = "launcher.steamdl.catalog.refresh";
}
