namespace Anomaly.Client.Api.Internal.Localization;

/// <summary>
/// Compile-time constants for Anomaly's own translation keys (anomaly.* namespace).
/// Internal — third-party mods declare their own keys in their own namespaces.
/// </summary>
internal static class TrKeys
{
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
    public const string Settings_ClassDColor = "anomaly.settings.class_dcolor";
    public const string Settings_ClassDColorDescription = "anomaly.settings.class_dcolor_description";

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
