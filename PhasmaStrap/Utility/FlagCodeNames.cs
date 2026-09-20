namespace PhasmaStrap.Utility
{
    public static class FlagCodeNames
    {
        public static readonly string[] Names =
        {
            "FFlagHandleAltEnterFullscreenManually", "DFFlagDisableDPIScale", "FIntDebugForceMSAASamples",
            "DFFlagTextureQualityOverrideEnabled", "DFIntTextureQualityOverride", "DFStringTelemetryV2Url",
            "FFlagEnableTelemetryProtocol", "DFFlagGraphicsQualityUsageTelemetry", "DFFlagGpuVsCpuBoundTelemetry",
            "DFFlagSendRenderFidelityTelemetry", "DFFlagReportRenderDistanceTelemetry", "DFFlagCollectAudioPluginTelemetry",
            "DFFlagEnableFmodErrorsTelemetry", "DFFlagRccLoadSoundLengthTelemetryEnabled", "DFFlagReportAssetRequestV1Telemetry",
            "DFFlagRobloxTelemetryAddDeviceRAMPointsV2", "DFFlagEnableTelemetryV2FRMStats", "DFFlagEnableSkipUpdatingGlobalTelemetryInfo2",
            "DFFlagEmitSafetyTelemetryInCallbackEnable", "DFFlagRobloxTelemetryV2PointEncoding", "DFFlagDSTelemetryV2ReplaceSeparator",
            "FFlagOpenTelemetryEnabled", "FLogRobloxTelemetry", "FFlagEnableTelemetryService1",
            "FFlagPropertiesEnableTelemetry", "DFStringWebviewUrlAllowlist", "DFFlagWindowsWebViewTelemetryEnabled",
            "DFIntMacWebViewTelemetryThrottleHundredthsPercent", "DFIntWindowsWebViewTelemetryThrottleHundredthsPercent", "FIntStudioWebView2TelemetryHundredthsPercent",
            "FFlagSyncWebViewCookieToEngine2", "FFlagUpdateHTTPCookieStorageFromWKWebView", "DFFlagVoiceChatCullingRecordEventIngestTelemetry",
            "DFFlagVoiceChatJoinProfilingUsingTelemetryStat_RCC", "DFFlagVoiceChatPossibleDuplicateSubscriptionsTelemetry", "DFIntVoiceChatTaskStatsTelemetryThrottleHundrethsPercent",
            "FFlagEnableLuaVoiceChatAnalyticsV2", "FFlagLuaVoiceChatAnalyticsBanMessage", "FFlagLuaVoiceChatAnalyticsUseCounterV2",
            "FFlagLuaVoiceChatAnalyticsUseEventsV2", "FFlagLuaVoiceChatAnalyticsUsePointsV2", "FFlagVoiceChatCullingEnableMutedSubsTelemetry",
            "FFlagVoiceChatCullingEnableStaleSubsTelemetry", "FFlagVoiceChatCustomAudioDeviceEnableNeedMorePlayoutTelemetry", "FFlagVoiceChatCustomAudioDeviceEnableNeedMorePlayoutTelemetry3",
            "FFlagVoiceChatCustomAudioMixerEnableUpdateSourcesTelemetry2", "FFlagVoiceChatDontSendTelemetryForPubIceTrickle", "FFlagVoiceChatPeerConnectionTelemetryDetails",
            "FFlagVoiceChatRobloxAudioDeviceUpdateRecordedBufferTelemetryEnabled", "FFlagVoiceChatSubscriptionsDroppedTelemetry", "FIntLuaVoiceChatAnalyticsPointsThrottle",
            "FIntVoiceChatPerfSensitiveTelemetryIntervalSeconds", "FStringTencentAuthPath", "FLogTencentAuthPath",
            "FStringXboxExperienceGuidelinesUrl", "FStringExperienceGuidelinesExplainedPageUrl", "DFFlagPolicyServiceReportIsNotSubjectToChinaPolicies",
            "DFFlagPolicyServiceReportDetailIsNotSubjectToChinaPolicies", "DFIntPolicyServiceReportDetailIsNotSubjectToChinaPoliciesHundredthsPercentage", "DFFlagDebugPrintDataPingBreakDown",
            "FFlagDebugLightGridShowChunks", "FStringDebugShowFlagState", "FFlagEnableBubbleChatFromChatService",
            "FFlagChatTranslationSettingEnabled3", "FFlagFastGPULightCulling3", "FFlagDebugForceFSMCPULightCulling",
            "FFlagDebugDisplayUnthemedInstances", "DFIntDebugFRMQualityLevelOverride", "DFIntCSGLevelOfDetailSwitchingDistanceStatic",
            "DFIntCSGLevelOfDetailSwitchingDistance", "DFIntCSGLevelOfDetailSwitchingDistanceL12", "DFIntCSGLevelOfDetailSwitchingDistanceL23",
            "DFIntCSGLevelOfDetailSwitchingDistanceL34", "FIntCameraMaxZoomDistance", "FFlagD3D11SupportBGRA",
            "FFlagEnableFPSAndFrameTime", "FFlagFixOutdatedParticles2", "FFlagFixOutdatedTimeScaleParticles",
            "FFlagFixParticleAttachmentCulling", "FFlagFixParticleEmissionBias2", "FFlagDebugGraphicsDisableDirect3D11",
            "FFlagDebugGraphicsPreferD3D11", "FFlagDebugGraphicsPreferVulkan", "FFlagDebugGraphicsPreferOpenGL",
            "FFlagRenderFixFog", "FIntFRMMinGrassDistance", "FIntFRMMaxGrassDistance",
            "FIntGrassMovementReducedMotionFactor", "FIntNewInGameMenuPercentRollout3", "FFlagEnableInGameMenuControls",
            "FFlagEnableInGameMenuModernization", "FFlagEnableInGameMenuChrome", "FFlagFixReportButtonCutOff",
            "DFFlagDebugRenderForceTechnologyVoxel", "FFlagDebugForceFutureIsBrightPhase2", "FFlagDebugForceFutureIsBrightPhase3",
            "FFlagRenderUnifiedLighting14", "FIntFullscreenTitleBarTriggerDelayMillis", "FIntDebugTextureManagerSkipMips",
            "DFIntDebugRestrictGCDistance", "DFIntDebugDynamicRenderKiloPixels", "FIntRomarkStartWithGraphicQualityLevel",
            "FFlagDisablePostFx", "DFFlagTaskSchedulerAvoidSleep", "FIntRenderShadowIntensity",
            "DFFlagDebugPauseVoxelizer", "FIntRenderShadowmapBias", "DFFlagUseVisBugChecks",
            "FFlagEnableVisBugChecks27", "FFlagVisBugChecksThreadYield", "FFlagDebugSkyGray",
            "FFlagSkyUseRGBEEncoding", "FStringDebugHighlightSpecificFont", "DFIntLCCageDeformLimit",
            "FIntTerrainArraySliceSize", "FFlagMovePrerender", "FFlagMovePrerenderV2",
            "FStringBuggyRenderpassList2", "FStringVulkanBuggyRenderpassList2", "FFlagEnableInGameMenuChromeABTest4",
            "FFlagEnableHamburgerIcon", "FFlagEnableUnibarV4IA", "FFlagEnableAlwaysOpenUnibar2",
            "FFlagUseNewUnibarIcon", "FFlagUseSelfieViewFlatIcon", "FFlagUnibarRespawn",
            "FFlagEnableChromePinIntegrations2", "FFlagEnableUnibarMaxDefaultOpen", "FFlagUpdateHealthBar",
            "FFlagUseNewPinIcon", "DFIntRenderClampRoughnessMax", "DFIntGraphicsOptimizationModeFRMFrameRateTarget",
            "DFIntGraphicsOptimizationModeMaxFrameTimeTargetMs", "DFIntGraphicsOptimizationModeMinFrameTimeTargetMs", "FFlagDebugRenderingSetDeterministic",
            "FFlagRenderNoLowFrmBloom", "FFlagFRMRefactor", "FIntMaquettesFrameRateBufferPercentage",
            "DFIntTaskSchedulerTargetFps", "FFlagTaskSchedulerLimitTargetFpsTo2402", "FFlagDebugEnablePseudolocalization",
            "FFlagDebugDisplayFPS", "DFIntTextureCompositorActiveJobs", "FIntFontSizePadding",
            "DFIntCanHideGuiGroupId", "DFIntBandwidthManagerApplicationDefaultBps", "DFIntBandwidthManagerDataSenderMaxWorkCatchupMs",
            "DFIntSignalRCoreServerTimeoutMs", "DFIntSignalRCoreRpcQueueSize", "DFIntSignalRCoreHubBaseRetryMs",
            "DFIntSignalRCoreHandshakeTimeoutMs", "DFIntSignalRCoreKeepAlivePingPeriodMs", "DFIntSignalRCoreHubMaxBackoffMs",
            "DFIntRccMaxPayloadSnd", "DFIntCliMaxPayloadRcv", "DFIntCliMaxPayloadSnd",
            "DFIntRccMaxPayloadRcv", "DFIntCliTcMaxPayloadRcv", "DFIntRccTcMaxPayloadRcv",
            "DFIntCliTcMaxPayloadSnd", "DFIntRccTcMaxPayloadSnd", "DFIntMaxDataPayloadSize",
            "DFIntMaxUREPayloadSingleLimit", "DFIntTotalRepPayloadLimit", "DFIntNumAssetsMaxToPreload",
            "FStringGetPlayerImageDefaultTimeout", "DFIntNetworkStopProducingPacketsToProcessThresholdMs", "DFIntMaxWaitTimeBeforeForcePacketProcessMS",
            "DFIntClientPacketMaxDelayMs", "DFIntClientPacketMinMicroseconds", "DFIntClientPacketExcessMicroseconds",
            "DFIntClientPacketMaxFrameMicroseconds", "DFIntMaxProcessPacketsJobScaling", "DFIntMaxProcessPacketsStepsAccumulated",
            "DFIntMaxProcessPacketsStepsPerCyclic", "DFIntConnectionMTUSize", "FIntRakNetResendBufferArrayLength",
            "FFlagLargeReplicatorEnabled7", "FFlagLargeReplicatorWrite5", "FFlagLargeReplicatorRead5",
            "FFlagLargeReplicatorSerializeRead3", "FFlagLargeReplicatorSerializeWrite3", "FFlagFixSensitivityTextPrecision",
            "FIntRobloxGuiBlurIntensity", "FFlagEnablePreferredTextSizeScale", "FFlagEnablePreferredTextSizeSettingInMenus2",
            "FFlagTextureUseACR3", "FIntTextureUseACRHundredthPercent", "FFlagDebugCheckRenderThreading",
            "FFlagAdServiceEnabled", "FFlagEnableSponsoredAdsGameCarouselTooltip3", "FFlagEnableSponsoredAdsPerTileTooltipExperienceFooter",
            "FFlagEnableSponsoredAdsSeeAllGamesListTooltip", "FFlagEnableSponsoredTooltipForAvatarCatalog2", "FFlagLuaAppSponsoredGridTiles",
            "FFlagLuaAppEnableFoundationColors7", "FFlagUIBloxMoveDetailsPageToLuaApps", "DFFlagEnableMeshPreloading2",
            "DFFlagEnableSoundPreloading", "DFFlagEnableTexturePreloading", "DFFlagTeleportClientAssetPreloadingEnabled9",
            "FFlagPreloadAllFonts", "FFlagPreloadTextureItemsOption4", "DFFlagTeleportPreloadingMetrics5",
            "FFlagOptimizeCFrameUpdates4", "FFlagOptimizeCFrameUpdatesIC4", "FFlagReconnectDisabled",
            "FStringReconnectDisabledReason", "FStringWhitelistVerifiedUserId", "FFlagNewCameraControls",
            "FFlagDebugForceChatDisabled", "FFlagEnableRibbonPlugin3", "FFlagAlwaysShowVRToggleV3",
            "FFlagDisableFeedbackSoothsayerCheck", "FIntV1MenuLanguageSelectionFeaturePerMillageRollout", "FFlagAddHapticsToggle",
            "FFlagGameBasicSettingsFramerateCap5", "DFFlagPerformanceControlEnableMemoryProbing3", "FFlagClearCacheableContentProviderOnGameLaunch",
            "DFFlagAlwaysSkipDiskCache", "FFlagUseCachedAudibilityMeasurements", "DFIntCachedPatchLoadDelayMilliseconds",
            "DFIntHttpCacheCleanScheduleAfterMs", "DFIntHttpCacheCleanUpToAvailableSpaceMiB", "DFIntHttpCacheAsyncWriterMaxPendingSize",
            "DFIntHttpCacheEvictionExemptionMapMaxSize", "DFIntHttpCacheReportSlowWritesMinDuration", "DFIntMemCacheMaxCapacityMB",
            "DFIntFileCacheReserveSize", "DFIntThirdPartyInMemoryCacheCapacity", "DFIntSoundServiceCacheCleanupMaxAgeDays",
            "DFIntUserIdPlayerNameCacheLifetimeSeconds", "DFIntAssetCacheErrorLogHundredthsPercent", "DFFlagHttpTrackSyncWriteCachePhase",
            "DFIntHttpCachePerfSamplingRate", "DFIntHttpCachePerfHundredthsPercent", "DFIntReportCacheDirSizesHundredthsPercent",
            "DFIntInterpolationNumParallelTasks", "DFIntMegaReplicatorNumParallelTasks", "DFIntNetworkClusterPacketCacheNumParallelTasks",
            "DFIntReplicationDataCacheNumParallelTasks", "FIntLuaGcParallelMinMultiTasks", "FIntSmoothClusterTaskQueueMaxParallelTasks",
            "DFIntPhysicsReceiveNumParallelTasks", "FIntTaskSchedulerAutoThreadLimit", "FIntSimWorldTaskQueueParallelTasks",
            "DFIntRuntimeConcurrency", "FIntTaskSchedulerAsyncTasksMinimumThreadCount",
        };

        private static readonly Dictionary<string, int> _index = BuildIndex();

        private static Dictionary<string, int> BuildIndex()
        {
            var index = new Dictionary<string, int>(StringComparer.Ordinal);
            for (int i = 0; i < Names.Length; i++)
                index.TryAdd(Names[i], i);
            return index;
        }

        public static bool TryGetIndex(string name, out int index) => _index.TryGetValue(name, out index);
    }
}
