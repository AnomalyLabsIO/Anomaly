#if ANOMALY_BOOTSTRAP
namespace Anomaly.Bootstrap;
#else
namespace Anomaly.Client.Api.Internal.Localization;
#endif

/// <summary>
/// Compile-time constants for Anomaly's own translation keys (anomaly.* namespace).
/// Internal — third-party mods declare their own keys in their own namespaces.
/// </summary>
internal static class TrKeys
{
    // Early Bootstrap protection failure dialogs
    public const string Bootstrap_EarlyInitFailed = "anomaly.bootstrap.failure.early_init_failed";
    public const string Bootstrap_ProtectionUnavailable = "anomaly.bootstrap.failure.protection_unavailable";
    public const string Bootstrap_BeforeModsFailed = "anomaly.bootstrap.failure.before_mods_failed";
    public const string Bootstrap_BeforeGameStateChanged = "anomaly.bootstrap.failure.before_game_state_changed";
    public const string Bootstrap_GameStartFailed = "anomaly.bootstrap.failure.game_start_failed";
    public const string Bootstrap_ReadyStateChanged = "anomaly.bootstrap.failure.ready_state_changed";
    public const string Bootstrap_Unregistered = "anomaly.bootstrap.failure.unregistered";
    public const string Bootstrap_HeartbeatStopped = "anomaly.bootstrap.failure.heartbeat_stopped";
    public const string Bootstrap_AuthStateUnverified = "anomaly.bootstrap.failure.auth_state_unverified";
    public const string Bootstrap_AuthStateChanged = "anomaly.bootstrap.failure.auth_state_changed";
    public const string Bootstrap_AuthStateRestart = "anomaly.bootstrap.failure.auth_state_restart";
    public const string Bootstrap_AlreadyInstalled = "anomaly.bootstrap.failure.already_installed";
    public const string Bootstrap_LoaderUnsupported = "anomaly.bootstrap.failure.loader_unsupported";
    public const string Bootstrap_InstallVerificationFailed = "anomaly.bootstrap.failure.install_verification_failed";
    public const string Bootstrap_CanaryPatchFailed = "anomaly.bootstrap.failure.canary_patch_failed";
    public const string Bootstrap_CanaryRestoreFailed = "anomaly.bootstrap.failure.canary_restore_failed";
    public const string Bootstrap_CanaryModified = "anomaly.bootstrap.failure.canary_modified";
    public const string Bootstrap_BackendAddressMissing = "anomaly.bootstrap.failure.backend_address_missing";
    public const string Bootstrap_LeaseOrderChanged = "anomaly.bootstrap.failure.lease_order_changed";
    public const string Bootstrap_IlHookSourceChanged = "anomaly.bootstrap.failure.il_hook_source_changed";
    public const string Bootstrap_DeadlineDelayed = "anomaly.bootstrap.failure.deadline_delayed";
    public const string Bootstrap_EntryPreservationReplaced = "anomaly.bootstrap.failure.entry_preservation_replaced";
    public const string Bootstrap_AuditReplaced = "anomaly.bootstrap.failure.audit_replaced";
    public const string Bootstrap_InterceptorInventoryChanged = "anomaly.bootstrap.failure.interceptor_inventory_changed";
    public const string Bootstrap_CallbackInventoryChanged = "anomaly.bootstrap.failure.callback_inventory_changed";
    public const string Bootstrap_PatchInventoryChanged = "anomaly.bootstrap.failure.patch_inventory_changed";
    public const string Bootstrap_TrustedInventoryChanged = "anomaly.bootstrap.failure.trusted_inventory_changed";
    public const string Bootstrap_IlHookNestingExceeded = "anomaly.bootstrap.failure.il_hook_nesting_exceeded";
    public const string Bootstrap_IlHookOperationMissing = "anomaly.bootstrap.failure.il_hook_operation_missing";
    public const string Bootstrap_IlHookCompletionOrderChanged = "anomaly.bootstrap.failure.il_hook_completion_order_changed";
    public const string Bootstrap_IlHookCallbackMissing = "anomaly.bootstrap.failure.il_hook_callback_missing";
    public const string Bootstrap_IlHookRefreshIncomplete = "anomaly.bootstrap.failure.il_hook_refresh_incomplete";
    public const string Bootstrap_MutationAccountingChanged = "anomaly.bootstrap.failure.mutation_accounting_changed";
    public const string Bootstrap_UnspecifiedObservation = "anomaly.bootstrap.failure.unspecified_observation";
    public const string Bootstrap_NativeAlreadyInstalled = "anomaly.bootstrap.failure.native_already_installed";
    public const string Bootstrap_NativePlatformUnsupported = "anomaly.bootstrap.failure.native_platform_unsupported";
    public const string Bootstrap_NativePrototypeMissing = "anomaly.bootstrap.failure.native_prototype_missing";
    public const string Bootstrap_EntryPreserverAlreadyInstalled = "anomaly.bootstrap.failure.entry_preserver_already_installed";
    public const string Bootstrap_ClrPlatformUnsupported = "anomaly.bootstrap.failure.clr_platform_unsupported";
    public const string Bootstrap_DetourPlatformUnavailable = "anomaly.bootstrap.failure.detour_platform_unavailable";
    public const string Bootstrap_DetourEndpointsChanged = "anomaly.bootstrap.failure.detour_endpoints_changed";
    public const string Bootstrap_ClrPrecodeUnreadable = "anomaly.bootstrap.failure.clr_precode_unreadable";
    public const string Bootstrap_ClrAdapterUnavailable = "anomaly.bootstrap.failure.clr_adapter_unavailable";
    public const string Bootstrap_ClrMethodUnreadable = "anomaly.bootstrap.failure.clr_method_unreadable";
    public const string Bootstrap_ClrLayoutUnsupported = "anomaly.bootstrap.failure.clr_layout_unsupported";
    public const string Bootstrap_ClrCodeSlotUnreadable = "anomaly.bootstrap.failure.clr_code_slot_unreadable";
    public const string Bootstrap_ClrImplementationUnconfirmed = "anomaly.bootstrap.failure.clr_implementation_unconfirmed";
    public const string Bootstrap_DetourTargetsStub = "anomaly.bootstrap.failure.detour_targets_stub";
    public const string Bootstrap_ProtectionFailed = "anomaly.bootstrap.failure.protection_failed";
    public const string Bootstrap_AuthStateUnreadable = "anomaly.bootstrap.failure.auth_state_unreadable";
    public const string Bootstrap_InstallationFailed = "anomaly.bootstrap.failure.installation_failed";
    public const string Bootstrap_CheckpointFailed = "anomaly.bootstrap.failure.checkpoint_failed";
    public const string Bootstrap_HeartbeatFailed = "anomaly.bootstrap.failure.heartbeat_failed";
    public const string Bootstrap_BackendUnprotected = "anomaly.bootstrap.failure.backend_unprotected";
    public const string Bootstrap_CallbackDispatchChanged = "anomaly.bootstrap.failure.callback_dispatch_changed";
    public const string Bootstrap_InterceptorMissing = "anomaly.bootstrap.failure.interceptor_missing";
    public const string Bootstrap_CallbackChanged = "anomaly.bootstrap.failure.callback_changed";
    public const string Bootstrap_PatchShieldCallbackMissing = "anomaly.bootstrap.failure.patch_shield_callback_missing";
    public const string Bootstrap_DeferredNativeModification = "anomaly.bootstrap.failure.deferred_native_modification";
    public const string Bootstrap_CriticalPatch = "anomaly.bootstrap.failure.critical_patch";
    public const string Bootstrap_CriticalDetour = "anomaly.bootstrap.failure.critical_detour";
    public const string Bootstrap_NativeBaselineFailed = "anomaly.bootstrap.failure.native_baseline_failed";
    public const string Bootstrap_NativeEntryChanged = "anomaly.bootstrap.failure.native_entry_changed";
    public const string Bootstrap_MissingProtection = "anomaly.bootstrap.failure.missing_protection";
    public const string Bootstrap_ClrEntrypointMismatch = "anomaly.bootstrap.failure.clr_entrypoint_mismatch";
    public const string Bootstrap_FailureTitle = "anomaly.bootstrap.failure.failure_title";
    public const string Bootstrap_FailureDialog = "anomaly.bootstrap.failure.failure_dialog";

    // Auth overlay
    public const string Overlay_Authenticating = "anomaly.overlay.authenticating";
    public const string Overlay_ConnectionAuthenticating = "anomaly.overlay.connection_authenticating";
    public const string Overlay_PasswordRequired = "anomaly.overlay.password_required";
    public const string PasswordPrompt_Title = "anomaly.password_prompt.title";
    public const string PasswordPrompt_Hint = "anomaly.password_prompt.hint";
    public const string Prompt_Cancel = "anomaly.prompt.cancel";
    public const string Prompt_Connect = "anomaly.prompt.connect";
    public const string Prompt_Save = "anomaly.prompt.save";
    public const string Prompt_Dismiss = "anomaly.prompt.dismiss";
    public const string Prompt_TrustConnect = "anomaly.prompt.trust_connect";
    public const string Prompt_AllowContent = "anomaly.prompt.allow_content";
    public const string Prompt_SkipContent = "anomaly.prompt.skip_content";
    public const string Prompt_SaveError = "anomaly.prompt.save_error";
    public const string Prompt_NicknameTitle = "anomaly.prompt.nickname_title";
    public const string Prompt_NicknameDetails = "anomaly.prompt.nickname_details";
    public const string Prompt_NicknameInvalid = "anomaly.prompt.nickname_invalid";
    public const string Prompt_KeyboardHint = "anomaly.prompt.keyboard_hint";

    // File transfer overlay
    public const string FileTransfer_Preparing = "anomaly.overlay.file_transfer.preparing";
    public const string FileTransfer_Downloading = "anomaly.overlay.file_transfer.downloading";
    public const string FileTransfer_Connecting = "anomaly.overlay.file_transfer.connecting";
    public const string FileTransfer_LoadingLevel = "anomaly.overlay.file_transfer.loading_level";

    // News / main menu announcement
    public const string News_Anomaly_Title = "anomaly.news.anomaly.title";
    public const string News_Anomaly_Body = "anomaly.news.anomaly.body";
    public const string News_Anomaly_Date = "anomaly.news.anomaly.date";

    // Local installed-component and startup diagnostics panel
    public const string NewsDiagnostics_Title = "anomaly.news.diagnostics.title";
    public const string NewsDiagnostics_Collecting = "anomaly.news.diagnostics.collecting";
    public const string NewsDiagnostics_Ready = "anomaly.news.diagnostics.ready";
    public const string NewsDiagnostics_Versions = "anomaly.news.diagnostics.versions";
    public const string NewsDiagnostics_Issues = "anomaly.news.diagnostics.issues";
    public const string NewsDiagnostics_Clear = "anomaly.news.diagnostics.clear";
    public const string NewsDiagnostics_Mods = "anomaly.news.diagnostics.mods";
    public const string NewsDiagnostics_Plugins = "anomaly.news.diagnostics.plugins";
    public const string NewsDiagnostics_Dependencies = "anomaly.news.diagnostics.dependencies";
    public const string NewsDiagnostics_Runtime = "anomaly.news.diagnostics.runtime";
    public const string NewsDiagnostics_History = "anomaly.news.diagnostics.history";
    public const string NewsDiagnostics_Empty = "anomaly.news.diagnostics.empty";
    public const string NewsDiagnostics_Details = "anomaly.news.diagnostics.details";
    public const string NewsDiagnostics_Source = "anomaly.news.diagnostics.source";
    public const string NewsDiagnostics_Time = "anomaly.news.diagnostics.time";
    public const string NewsDiagnostics_Unknown = "anomaly.news.diagnostics.unknown";
    public const string NewsDiagnostics_Copy = "anomaly.news.diagnostics.copy";
    public const string NewsDiagnostics_Copied = "anomaly.news.diagnostics.copied";
    public const string NewsDiagnostics_Copy_Failed = "anomaly.news.diagnostics.copy_failed";
    public const string NewsDiagnostics_Help = "anomaly.news.diagnostics.help";
    public const string NewsDiagnostics_Social = "anomaly.news.diagnostics.social";
    public const string NewsDiagnostics_Kind_Mod = "anomaly.news.diagnostics.kind.mod";
    public const string NewsDiagnostics_Kind_Plugin = "anomaly.news.diagnostics.kind.plugin";
    public const string NewsDiagnostics_Kind_Dependency = "anomaly.news.diagnostics.kind.dependency";
    public const string NewsDiagnostics_Kind_Managedmodule = "anomaly.news.diagnostics.kind.managedmodule";
    public const string NewsDiagnostics_State_Unknown = "anomaly.news.diagnostics.state.unknown";
    public const string NewsDiagnostics_State_Present = "anomaly.news.diagnostics.state.present";
    public const string NewsDiagnostics_State_Registered = "anomaly.news.diagnostics.state.registered";
    public const string NewsDiagnostics_State_Loaded = "anomaly.news.diagnostics.state.loaded";
    public const string NewsDiagnostics_State_Failed = "anomaly.news.diagnostics.state.failed";
    public const string NewsDiagnostics_State_Disabled = "anomaly.news.diagnostics.state.disabled";
    public const string NewsDiagnostics_Issues_Reported = "anomaly.news.diagnostics.issues_reported";
    public const string NewsDiagnostics_Id = "anomaly.news.diagnostics.id";
    public const string NewsDiagnostics_Path = "anomaly.news.diagnostics.path";
    public const string NewsDiagnostics_Type = "anomaly.news.diagnostics.type";
    public const string NewsDiagnostics_Code = "anomaly.news.diagnostics.code";
    public const string NewsDiagnostics_Runtime_Api = "anomaly.news.diagnostics.runtime.api";
    public const string NewsDiagnostics_Runtime_Bootstrap = "anomaly.news.diagnostics.runtime.bootstrap";
    public const string NewsDiagnostics_Runtime_Unity = "anomaly.news.diagnostics.runtime.unity";
    public const string NewsDiagnostics_Runtime_Dotnet = "anomaly.news.diagnostics.runtime.dotnet";
    public const string NewsDiagnostics_Runtime_Harmony = "anomaly.news.diagnostics.runtime.harmony";
    public const string NewsDiagnostics_Runtime_Interop = "anomaly.news.diagnostics.runtime.interop";
    public const string NewsDiagnostics_Runtime_Os = "anomaly.news.diagnostics.runtime.os";
    public const string NewsDiagnostics_Runtime_Gpu = "anomaly.news.diagnostics.runtime.gpu";
    public const string NewsDiagnostics_Runtime_Graphics = "anomaly.news.diagnostics.runtime.graphics";
    public const string NewsDiagnostics_Runtime_Resolution = "anomaly.news.diagnostics.runtime.resolution";
    public const string NewsDiagnostics_Kind_Loadfailure = "anomaly.news.diagnostics.kind.loadfailure";
    public const string NewsDiagnostics_Kind_Initializationerror = "anomaly.news.diagnostics.kind.initializationerror";
    public const string NewsDiagnostics_Kind_Missingdependency = "anomaly.news.diagnostics.kind.missingdependency";
    public const string NewsDiagnostics_Kind_Reportederror = "anomaly.news.diagnostics.kind.reportederror";
    public const string NewsDiagnostics_Kind_Featurehealth = "anomaly.news.diagnostics.kind.featurehealth";
    public const string NewsDiagnostics_Kind_Diagnosticreport = "anomaly.news.diagnostics.kind.diagnosticreport";
    public const string NewsDiagnostics_Kind_Evidenceunavailable = "anomaly.news.diagnostics.kind.evidenceunavailable";
    public const string NewsDiagnostics_Kind_Interruptedactivity = "anomaly.news.diagnostics.kind.interruptedactivity";
    public const string NewsDiagnostics_Interrupted_Activity = "anomaly.news.diagnostics.interrupted_activity";
    public const string NewsDiagnostics_History_Limit = "anomaly.news.diagnostics.history_limit";
    public const string NewsDiagnostics_Missing_Dependency_Detail = "anomaly.news.diagnostics.missing_dependency_detail";

    // Main menu customization
    public const string Menu_Welcome = "anomaly.menu.welcome";
    public const string Menu_AuthConnected = "anomaly.menu.auth_connected";
    public const string Menu_AuthReady = "anomaly.menu.auth_ready";
    public const string Menu_AuthNotSignedIn = "anomaly.menu.auth_not_signed_in";
    public const string Menu_AuthUnavailable = "anomaly.menu.auth_unavailable";

    // Shared between preloader and main menu version labels
    public const string Version_ModSuffix = "anomaly.version_modded_suffix";

    // Mods settings tab
    public const string Settings_TabMods = "anomaly.settings.tab_mods";
    public const string Settings_ModsEmptyState = "anomaly.settings.mods_empty";
    public const string Settings_ModsUnsupportedType = "anomaly.settings.mods_unsupported_type";

    // Mods settings feedback and preferences
    public const string Settings_Reset = "anomaly.settings.reset";
    public const string Settings_RetrySave = "anomaly.settings.retry_save";
    public const string Settings_Disabled = "anomaly.settings.disabled";
    public const string Settings_Enabled = "anomaly.settings.enabled";
    public const string Settings_EmptyValue = "anomaly.settings.empty_value";
    public const string Settings_ValueSummary = "anomaly.settings.value_summary";
    public const string Settings_InvalidValue = "anomaly.settings.invalid_value";
    public const string Settings_ApplyFailed = "anomaly.settings.apply_failed";
    public const string Settings_ResetFailed = "anomaly.settings.reset_failed";
    public const string Settings_SelectValidValue = "anomaly.settings.select_valid_value";
    public const string Settings_UnavailableChoice = "anomaly.settings.unavailable_choice";
    public const string Settings_OutsideSliderRange = "anomaly.settings.outside_slider_range";
    public const string Settings_InvalidType = "anomaly.settings.invalid_type";
    public const string Settings_InvalidRange = "anomaly.settings.invalid_range";
    public const string Settings_StorageUnavailable = "anomaly.settings.storage_unavailable";
    public const string Settings_SaveUnverified = "anomaly.settings.save_unverified";
    public const string Settings_SaveDisabled = "anomaly.settings.save_disabled";
    public const string Settings_LoadError = "anomaly.settings.load_error";
    public const string Settings_WriteError = "anomaly.settings.write_error";
    public const string Settings_SaveError = "anomaly.settings.save_error";
    public const string Settings_StorageInspectionFailed = "anomaly.settings.storage_inspection_failed";
    public const string Settings_LocaleOverride = "anomaly.settings.locale_override";

    // Credits
    public const string Credits_ModdingSupport = "anomaly.credits.modding_support";
    public const string Credits_Creator = "anomaly.credits.creator";
    public const string Credits_CreatorMaintainer = "anomaly.credits.creator_maintainer";
    public const string Credits_Developer = "anomaly.credits.developer";
    public const string Credits_Helper = "anomaly.credits.helper";

    // File transfer consent
    public const string FileTransfer_AwaitingConsent = "anomaly.overlay.file_transfer.awaiting_consent";

    // Built-in console commands
    public const string Commands_NameDescription = "anomaly.commands.name_description";
    public const string Commands_NotConnected = "anomaly.commands.not_connected";
    public const string Commands_NoNickname = "anomaly.commands.no_nickname";
    public const string Commands_WhoAmIDescription = "anomaly.commands.who_am_idescription";
    public const string Commands_NoEnrollment = "anomaly.commands.no_enrollment";
    public const string Commands_EnrollmentCopied = "anomaly.commands.enrollment_copied";
    public const string Commands_StatusDescription = "anomaly.commands.status_description";
    public const string Commands_Usage = "anomaly.commands.usage";
    public const string Commands_StatusCopied = "anomaly.commands.status_copied";
    public const string Commands_StatusUnavailable = "anomaly.commands.status_unavailable";
    public const string Commands_IdentityDescription = "anomaly.commands.identity_description";
    public const string Commands_IdentityUsage = "anomaly.commands.identity_usage";
    public const string Commands_IdentityFailed = "anomaly.commands.identity_failed";
    public const string Commands_IdentityRotated = "anomaly.commands.identity_rotated";
    public const string Commands_ModFaulted = "anomaly.commands.mod_faulted";
    public const string Commands_Error = "anomaly.commands.error";

    // Connection failure notice
    public const string Connection_FailureTitle = "anomaly.connection.failure_title";
    public const string Connection_FailureDetails = "anomaly.connection.failure_details";

    // Authentication and mod admission
    public const string Auth_FeaturesUnavailable = "anomaly.auth.features_unavailable";
    public const string Auth_FeaturesAction = "anomaly.auth.features_action";
    public const string Auth_NetworkNotReady = "anomaly.auth.network_not_ready";
    public const string Auth_NetworkAction = "anomaly.auth.network_action";
    public const string Auth_ReceiverUnavailable = "anomaly.auth.receiver_unavailable";
    public const string Auth_ReceiverAction = "anomaly.auth.receiver_action";
    public const string Auth_ProfileUnavailable = "anomaly.auth.profile_unavailable";
    public const string Auth_ProfileAction = "anomaly.auth.profile_action";
    public const string Auth_ModInventoryUnavailable = "anomaly.auth.mod_inventory_unavailable";
    public const string Auth_ModInventoryInvalid = "anomaly.auth.mod_inventory_invalid";
    public const string Auth_ModInventoryTooLarge = "anomaly.auth.mod_inventory_too_large";
    public const string Auth_PrepareFailed = "anomaly.auth.prepare_failed";
    public const string Auth_PrepareAction = "anomaly.auth.prepare_action";
    public const string Auth_IntegrityFailed = "anomaly.auth.integrity_failed";
    public const string Auth_IntegrityAction = "anomaly.auth.integrity_action";
    public const string Auth_ConnectionEnded = "anomaly.auth.connection_ended";
    public const string Auth_NoResponseAction = "anomaly.auth.no_response_action";
    public const string Auth_RetryInvalid = "anomaly.auth.retry_invalid";
    public const string Auth_RetryAction = "anomaly.auth.retry_action";
    public const string Auth_Rejected = "anomaly.auth.rejected";
    public const string Auth_RejectedAction = "anomaly.auth.rejected_action";
    public const string Auth_SessionMismatch = "anomaly.auth.session_mismatch";
    public const string Auth_SessionAction = "anomaly.auth.session_action";
    public const string Auth_VersionMismatch = "anomaly.auth.version_mismatch";
    public const string Auth_VersionAction = "anomaly.auth.version_action";
    public const string Auth_HandshakeInvalid = "anomaly.auth.handshake_invalid";
    public const string Auth_HandshakeAction = "anomaly.auth.handshake_action";
    public const string Auth_KeyChanged = "anomaly.auth.key_changed";
    public const string Auth_KeyChangedAction = "anomaly.auth.key_changed_action";
    public const string Auth_TrustStoreUnavailable = "anomaly.auth.trust_store_unavailable";
    public const string Auth_TrustStoreAction = "anomaly.auth.trust_store_action";
    public const string Auth_TrustTitle = "anomaly.auth.trust_title";
    public const string Auth_TrustDetails = "anomaly.auth.trust_details";
    public const string Auth_TrustSaveFailed = "anomaly.auth.trust_save_failed";
    public const string Auth_TrustSaveAction = "anomaly.auth.trust_save_action";
    public const string Auth_CustomRejectedAction = "anomaly.auth.custom_rejected_action";
    public const string Auth_IntegrityRejected = "anomaly.auth.integrity_rejected";
    public const string Auth_IntegrityRejectedAction = "anomaly.auth.integrity_rejected_action";
    public const string Auth_ModRequirements = "anomaly.auth.mod_requirements";
    public const string Auth_ModResponseAction = "anomaly.auth.mod_response_action";
    public const string Auth_CustomReason = "anomaly.auth.custom_reason";
    public const string Auth_ReviewRequirements = "anomaly.auth.review_requirements";
    public const string Auth_ModMissing = "anomaly.auth.mod_missing";
    public const string Auth_ModWrongVersion = "anomaly.auth.mod_wrong_version";
    public const string Auth_ModBlocked = "anomaly.auth.mod_blocked";
    public const string Auth_ModUnexpected = "anomaly.auth.mod_unexpected";
    public const string Auth_ModPresent = "anomaly.auth.mod_present";
    public const string Auth_ModIssuesOmitted = "anomaly.auth.mod_issues_omitted";
    public const string Auth_ModRequirementsAction = "anomaly.auth.mod_requirements_action";
    public const string Auth_PasswordFailed = "anomaly.auth.password_failed";
    public const string Auth_PasswordAction = "anomaly.auth.password_action";
    public const string Auth_RotationProfileUnavailable = "anomaly.auth.rotation_profile_unavailable";
    public const string Auth_RotationDisconnect = "anomaly.auth.rotation_disconnect";
    public const string Auth_RotationError = "anomaly.auth.rotation_error";
    public const string Auth_NoResponse = "anomaly.auth.no_response";
    public const string Auth_ServerAvailabilityAction = "anomaly.auth.server_availability_action";
    public const string Auth_ReconnectFailed = "anomaly.auth.reconnect_failed";
    public const string Auth_ReconnectAction = "anomaly.auth.reconnect_action";

    // Server content consent and failures
    public const string Content_ApprovalTitle = "anomaly.content.approval_title";
    public const string Content_ApprovalDetails = "anomaly.content.approval_details";
    public const string Content_ValidationFailed = "anomaly.content.validation_failed";
    public const string Content_ActivationFailed = "anomaly.content.activation_failed";
    public const string Content_FailureAction = "anomaly.content.failure_action";

    // Sanitized status report
    public const string Status_Title = "anomaly.status.title";
    public const string Status_Versions = "anomaly.status.versions";
    public const string Status_Profile = "anomaly.status.profile";
    public const string Status_ProfileUnavailable = "anomaly.status.profile_unavailable";
    public const string Status_ProfileReady = "anomaly.status.profile_ready";
    public const string Status_ProfileIncomplete = "anomaly.status.profile_incomplete";
    public const string Status_Connection = "anomaly.status.connection";
    public const string Status_ConnectionReady = "anomaly.status.connection_ready";
    public const string Status_ConnectionPreparing = "anomaly.status.connection_preparing";
    public const string Status_Disconnected = "anomaly.status.disconnected";
    public const string Status_FeaturesHealthy = "anomaly.status.features_healthy";
    public const string Status_FeaturesAttention = "anomaly.status.features_attention";
    public const string Status_FeatureDetail = "anomaly.status.feature_detail";
    public const string Status_FeaturesOmitted = "anomaly.status.features_omitted";
    public const string Status_Log = "anomaly.status.log";
    public const string Status_CrashReports = "anomaly.status.crash_reports";
    public const string Status_Footer = "anomaly.status.footer";
    public const string Settings_Security = "anomaly.settings.security";
    public const string Settings_AllowAuthModifications = "anomaly.settings.allow_auth_modifications";
    public const string Settings_AllowAuthModificationsDescription = "anomaly.settings.allow_auth_modifications_description";
    public const string Menu_DefaultUsername = "anomaly.menu.default_username";
    public const string News_EarlyAccess = "anomaly.news.early_access";
    public const string FileTransfer_Percent = "anomaly.overlay.file_transfer.percent";
}
