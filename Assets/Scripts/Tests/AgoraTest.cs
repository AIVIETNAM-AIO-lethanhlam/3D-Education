using System;
using System.Collections;
using UnityEngine;
using Agora.Rtc;

#if UNITY_ANDROID
using UnityEngine.Android;
#endif

/// <summary>
/// Small Agora RTC voice service used by ChatPageController.
///
/// IMPORTANT FOR CURRENT TESTING:
/// - token1001 must be generated for UID 1001 + channelName.
/// - token1002 must be generated for UID 1002 + channelName.
/// - Build phone A with Client1001 and phone B with Client1002.
///
/// The temporary tokens are only for development. For production, replace them
/// with short-lived tokens returned by a trusted backend / Supabase Edge Function.
/// </summary>
public class AgoraTest : MonoBehaviour
{
    public enum TestClient
    {
        Client1001,
        Client1002
    }

    [Header("Agora")]
    [SerializeField] private string appId = "2c1ff186b7b94a99b6f54ad9ec3f56c4";
    [SerializeField] private string channelName = "voice-test";

    [Header("Select Client (development only)")]
    [Tooltip("Build one phone as Client1001 and the other as Client1002.")]
    [SerializeField] private TestClient selectedClient = TestClient.Client1001;

    [Header("Temporary RTC Tokens (development only)")]
    [Tooltip("Temporary RTC token generated for channelName and UID 1001.")]
    [SerializeField] private string token1001 = "PASTE_TOKEN_FOR_UID_1001_HERE";

    [Tooltip("Temporary RTC token generated for channelName and UID 1002.")]
    [SerializeField] private string token1002 = "PASTE_TOKEN_FOR_UID_1002_HERE";

    public event Action<string, uint> LocalJoined;
    // Compatibility event expected by ChatPageController.
    public event Action<string, uint> JoinedChannel;
    public event Action<uint> RemoteUserJoined;
    public event Action<uint> RemoteUserLeft;
    public event Action<int, string> AgoraError;

    public string ChannelName => channelName;
    // Compatibility property expected by ChatPageController.
    public string ConfiguredChannelName => channelName;
    public bool IsJoined { get; private set; }
    public bool IsMuted { get; private set; }
    public bool IsSpeakerOn { get; private set; }

    // Runtime UID is selected from the two Supabase user IDs in the conversation.
    // This is important when THE SAME APK is installed on both phones: both builds
    // must not join Agora as UID 1001.
    private uint runtimeUid;

    public uint CurrentUid =>
        callUid != 0u ? callUid : runtimeUid != 0u
            ? runtimeUid
            : (selectedClient == TestClient.Client1001 ? 1001u : 1002u);

    private string CurrentToken => runtimeToken;

    /// <summary>
    /// Deterministically assigns the two chat participants to Agora test UIDs
    /// 1001 and 1002. Both phones calculate the same mapping from the same pair
    /// of Supabase UUIDs, so one becomes 1001 and the other 1002 even when both
    /// phones use the exact same APK.
    /// </summary>
    public void PrepareForConversation(string currentUserId, string partnerUserId)
    {
        if (string.IsNullOrWhiteSpace(currentUserId) ||
            string.IsNullOrWhiteSpace(partnerUserId) ||
            string.Equals(currentUserId, partnerUserId, StringComparison.OrdinalIgnoreCase))
        {
            runtimeUid = 0u;
            Debug.LogWarning("[AgoraTest] Could not derive RTC UID from conversation users; using Inspector fallback.");
            return;
        }

        int order = string.Compare(
            currentUserId.Trim(),
            partnerUserId.Trim(),
            StringComparison.OrdinalIgnoreCase);

        runtimeUid = order < 0 ? 1001u : 1002u;

        Debug.Log(
            $"[AgoraTest] Conversation RTC identity selected: UID={runtimeUid}. " +
            "Same APK can now be used on both phones.");
    }

    private IRtcEngine rtcEngine;
    private AgoraEventHandler eventHandler;
    private bool joinInProgress;
    private string requestedChannel = string.Empty;
    private string runtimeToken = string.Empty;
    private string runtimeAppId = string.Empty;
    private uint callUid;

    /// <summary>
    /// ChatPageController calls this only when a voice call starts/gets accepted.
    /// Nothing auto-joins on scene load anymore.
    /// </summary>
    public void JoinVoiceChannel(string targetChannel, string token, uint uid, string serverAppId)
    {
        if (IsJoined || joinInProgress) return;
        if (string.IsNullOrWhiteSpace(targetChannel) || string.IsNullOrWhiteSpace(token) ||
            string.IsNullOrWhiteSpace(serverAppId) || uid == 0)
        {
            RaiseLocalError(-5, "Missing server-issued Agora channel/token/UID/App ID.");
            return;
        }
        requestedChannel = targetChannel.Trim();
        runtimeToken = token.Trim();
        runtimeAppId = serverAppId.Trim();
        callUid = uid;
        joinInProgress = true;
#if UNITY_ANDROID
        StartCoroutine(RequestMicrophoneThenJoin());
#else
        InitializeAndJoin();
#endif
    }

#if UNITY_ANDROID
    private IEnumerator RequestMicrophoneThenJoin()
    {
        if (!Permission.HasUserAuthorizedPermission(Permission.Microphone))
        {
            Permission.RequestUserPermission(Permission.Microphone);

            float timeout = 20f;
            while (!Permission.HasUserAuthorizedPermission(Permission.Microphone) &&
                   timeout > 0f)
            {
                timeout -= Time.unscaledDeltaTime;
                yield return null;
            }

            if (!Permission.HasUserAuthorizedPermission(Permission.Microphone))
            {
                joinInProgress = false;
                RaiseLocalError(-3, "Microphone permission was not granted.");
                yield break;
            }
        }

        if (joinInProgress && !string.IsNullOrWhiteSpace(requestedChannel))
            InitializeAndJoin();
    }
#endif

    private void InitializeAndJoin()
    {
        string token = CurrentToken;

        if (string.IsNullOrWhiteSpace(runtimeAppId))
        {
            joinInProgress = false;
            RaiseLocalError(-4, "Agora App ID is empty.");
            return;
        }

        if (string.IsNullOrWhiteSpace(token) || token.StartsWith("PASTE_TOKEN_"))
        {
            joinInProgress = false;
            RaiseLocalError(
                -5,
                $"Temporary Agora token for UID {CurrentUid} is not configured in the Inspector."
            );
            return;
        }

        if (!EnsureEngine())
        {
            joinInProgress = false;
            return;
        }

        IsMuted = false;
        IsSpeakerOn = false;
        // Audio capture and remote playback are separate from joining a channel.
        // Log each result so a successful signaling call cannot hide an RTC failure.
        int captureResult = rtcEngine.EnableLocalAudio(true);
        int publishResult = rtcEngine.MuteLocalAudioStream(false);
        int subscribeResult = rtcEngine.MuteAllRemoteAudioStreams(false);
        Debug.Log($"[AgoraAudio] PRE-JOIN capture={captureResult} publish={publishResult} subscribe={subscribeResult} micPermission={HasMicrophonePermission()}");

        ChannelMediaOptions options = new ChannelMediaOptions();
        options.publishMicrophoneTrack.SetValue(true);
        options.autoSubscribeAudio.SetValue(true);
        options.clientRoleType.SetValue(
            CLIENT_ROLE_TYPE.CLIENT_ROLE_BROADCASTER
        );

        int joinResult = rtcEngine.JoinChannel(
            token,
            requestedChannel,
            CurrentUid,
            options
        );

        Debug.Log(
            $"[AgoraTest] JoinChannel result={joinResult}, " +
            $"channel={requestedChannel}, uid={CurrentUid}"
        );

        if (joinResult != 0)
        {
            joinInProgress = false;
            DisposeEngine();
            RaiseLocalError(
                joinResult,
                "Agora JoinChannel returned a non-zero result."
            );
        }
    }

    private bool EnsureEngine()
    {
        if (rtcEngine != null)
            return true;

        Debug.Log("[AgoraTest] Creating Agora RTC engine...");

        rtcEngine = RtcEngine.CreateAgoraRtcEngine();

        if (rtcEngine == null)
        {
            RaiseLocalError(-6, "Failed to create Agora RTC engine.");
            return false;
        }

        RtcEngineContext context = new RtcEngineContext();
        context.appId = runtimeAppId;
        context.channelProfile =
            CHANNEL_PROFILE_TYPE.CHANNEL_PROFILE_COMMUNICATION;
        context.audioScenario =
            AUDIO_SCENARIO_TYPE.AUDIO_SCENARIO_DEFAULT;

        int initResult = rtcEngine.Initialize(context);
        Debug.Log("[AgoraTest] Initialize result: " + initResult);

        if (initResult != 0)
        {
            RaiseLocalError(
                initResult,
                "Agora RTC engine initialization failed."
            );
            DisposeEngine();
            return false;
        }

        eventHandler = new AgoraEventHandler();
        eventHandler.OnJoinedChannel += HandleJoinedChannel;
        eventHandler.OnUserJoinedChannel += HandleUserJoined;
        eventHandler.OnUserOfflineChannel += HandleUserOffline;
        eventHandler.OnErrorOccurred += HandleAgoraError;

        rtcEngine.InitEventHandler(eventHandler);

        int audioResult = rtcEngine.EnableAudio();
        Debug.Log("[AgoraAudio] EnableAudio result: " + audioResult);
        if (audioResult != 0)
        {
            RaiseLocalError(audioResult, "Agora audio module could not be enabled.");
            DisposeEngine();
            return false;
        }
        return true;
    }

    public void LeaveVoiceChannel()
    {
        StopAllCoroutines(); // Cancel pending Android microphone permission/join.
        bool wasJoining = joinInProgress;
        joinInProgress = false;
        requestedChannel = string.Empty;
        runtimeToken = string.Empty;
        callUid = 0u;

        if (rtcEngine != null && (IsJoined || wasJoining))
        {
            int result = rtcEngine.LeaveChannel();
            Debug.Log("[AgoraTest] LeaveChannel result: " + result);
        }

        IsJoined = false;
        IsMuted = false;
        IsSpeakerOn = false;
        // Leaving the channel releases the microphone. Dispose() can block the
        // Unity main thread on some Android devices, so defer native teardown.
        if (rtcEngine != null && isActiveAndEnabled)
            StartCoroutine(DisposeAfterLeave());
    }

    private IEnumerator DisposeAfterLeave()
    {
        yield return new WaitForSecondsRealtime(0.25f);
        // A new call may have started before the delayed cleanup.
        if (!joinInProgress && !IsJoined)
            DisposeEngine();
    }

    public void SetMuted(bool muted)
    {
        if (rtcEngine == null || !IsJoined)
            return;

        int result = rtcEngine.MuteLocalAudioStream(muted);

        if (result == 0)
        {
            IsMuted = muted;
            Debug.Log("[AgoraTest] Microphone muted=" + muted);
        }
        else
        {
            RaiseLocalError(result, "Could not change microphone mute state.");
        }
    }

    public void SetSpeakerphone(bool enabled)
    {
        if (rtcEngine == null || !IsJoined)
            return;

        int result = rtcEngine.SetEnableSpeakerphone(enabled);

        if (result == 0)
        {
            IsSpeakerOn = enabled;
            Debug.Log("[AgoraTest] Speakerphone enabled=" + enabled);
        }
        else
        {
            RaiseLocalError(result, "Could not change speakerphone route.");
        }
    }

    // Compatibility method expected by ChatPageController.
    public void SetSpeakerEnabled(bool enabled)
    {
        SetSpeakerphone(enabled);
    }

    private static bool HasMicrophonePermission()
    {
#if UNITY_ANDROID
        return Permission.HasUserAuthorizedPermission(Permission.Microphone);
#else
        return true;
#endif
    }

    private void HandleJoinedChannel(string channel, uint uid)
    {
        joinInProgress = false;
        IsJoined = true;

        Debug.Log(
            $"[AgoraTest] JOIN SUCCESS! Channel={channel}, UID={uid}"
        );

        // A joined RTC channel does not guarantee microphone capture or playback.
        int capture = rtcEngine.EnableLocalAudio(true);
        int publish = rtcEngine.MuteLocalAudioStream(false);
        int subscribe = rtcEngine.MuteAllRemoteAudioStreams(false);
        int speaker = rtcEngine.SetEnableSpeakerphone(true);
        IsMuted = publish != 0;
        IsSpeakerOn = speaker == 0;
        Debug.Log($"[AgoraAudio] JOINED channel={channel} uid={uid} capture={capture} publish={publish} subscribe={subscribe} speaker={speaker} micPermission={HasMicrophonePermission()}");
        LocalJoined?.Invoke(channel, uid);
        JoinedChannel?.Invoke(channel, uid);
    }

    private void HandleUserJoined(uint uid)
    {
        Debug.Log(
            $"[AgoraTest] Remote user joined. UID={uid}"
        );

        int unmuteRemote = rtcEngine != null ? rtcEngine.MuteRemoteAudioStream(uid, false) : -1;
        Debug.Log($"[AgoraAudio] REMOTE_JOIN uid={uid} unmuteRemote={unmuteRemote}");
        RemoteUserJoined?.Invoke(uid);
    }

    private void HandleUserOffline(uint uid)
    {
        Debug.Log(
            $"[AgoraTest] Remote user left. UID={uid}"
        );

        RemoteUserLeft?.Invoke(uid);
    }

    private void HandleAgoraError(int errorCode, string message)
    {
        joinInProgress = false;

        Debug.LogError(
            $"[AgoraTest] Agora error {errorCode}: {message}"
        );

        AgoraError?.Invoke(errorCode, message);
    }

    private void RaiseLocalError(int errorCode, string message)
    {
        Debug.LogError(
            $"[AgoraTest] {message} (code={errorCode})"
        );

        AgoraError?.Invoke(errorCode, message);
    }

    private void OnDestroy()
    {
        LeaveVoiceChannel();
        DisposeEngine();
    }

    private void DisposeEngine()
    {
        if (rtcEngine == null)
            return;

        if (eventHandler != null)
        {
            eventHandler.OnJoinedChannel -= HandleJoinedChannel;
            eventHandler.OnUserJoinedChannel -= HandleUserJoined;
            eventHandler.OnUserOfflineChannel -= HandleUserOffline;
            eventHandler.OnErrorOccurred -= HandleAgoraError;
        }

        rtcEngine.Dispose();
        rtcEngine = null;
        eventHandler = null;

        Debug.Log("[AgoraTest] Agora engine disposed.");
    }

    private class AgoraEventHandler : IRtcEngineEventHandler
    {
        public Action<string, uint> OnJoinedChannel;
        public Action<uint> OnUserJoinedChannel;
        public Action<uint> OnUserOfflineChannel;
        public Action<int, string> OnErrorOccurred;

        public override void OnJoinChannelSuccess(
            RtcConnection connection,
            int elapsed)
        {
            OnJoinedChannel?.Invoke(
                connection.channelId,
                connection.localUid
            );
        }

        public override void OnUserJoined(
            RtcConnection connection,
            uint remoteUid,
            int elapsed)
        {
            OnUserJoinedChannel?.Invoke(remoteUid);
        }

        public override void OnUserOffline(
            RtcConnection connection,
            uint remoteUid,
            USER_OFFLINE_REASON_TYPE reason)
        {
            OnUserOfflineChannel?.Invoke(remoteUid);
        }

        public override void OnError(
            int err,
            string msg)
        {
            OnErrorOccurred?.Invoke(err, msg);
        }
    }
}
