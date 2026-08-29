namespace PhasmaStrap.Integrations.GameChat
{
    public static class GameChatStrings
    {
        public const string AboutText = "About PhasmaStrap Chat:\nAn in-game chat overlay built into PhasmaStrap.\nMessages are relayed through an optional chat server that you (or your server admin) configure in Settings > Game Chat.\nChat '/help' for a list of commands.";
        public const string ChatInputBoxText = "Press / key | Ctrl+Shift+C to hide";
        public const string ConnectedSuccessfully = "Connected successfully!";
        public const string ConnectingToServer = "Connecting to server {0}...";
        public const string ConnectionError = "Connection error: {0}";
        public const string ConnectionFailed = "Connection failed: {0}";
        public const string CurrentChannelID = "Current channel ID";
        public const string EchoResponse = "{0} (Only you can see this message.)";
        public const string UnknownError = "Unknown error.";
        public const string FailedToSendMessage = "Failed to send message: {0}";
        public const string FilterPreferenceCurrent = "Current message filter: {0}";
        public const string FilterPreferenceSet = "Message filter set to: {0}";
        public const string MessageRejectedApiError = "Your message could not be processed due to a server error. Please try again.";
        public const string MessageRejectedModeration = "Your message was not sent as it violates the server's guidelines.";
        public const string MessageRejectedQueueFull = "Your message was rejected because the server queue is full. Please try again shortly.";
        public const string MessageRejectedUnknown = "Your message was not sent due to unknown reasons.";
        public const string MessageHiddenDueToFilterSettings = "[Message hidden due to your filter settings.]";
        public const string MutedSpeaker = "Speaker '{0}' has been muted.";
        public const string ResetToDefault = "Chat window reset to its default position and size.";
        public const string RequestTimedOut = "Request timed out.";
        public const string SpeakerNotMuted = "Speaker '{0}' was not muted.";
        public const string UsageBug = "Usage: /bug <describe the problem>";
        public const string StartupText = "Welcome to PhasmaStrap Chat.\nChat '/?' or '/help' for a list of chat commands.";
        public const string System = "System";
        public const string UnknownCommand = "Unknown command '{0}'. Use '/?' or '/help' for a list of commands.";
        public const string UnmutedSpeaker = "Speaker '{0}' has been unmuted.";
        public const string UsageFilter = "Usage: /filter <strict|default|relaxed>";
        public const string UsageMute = "Usage: /mute <speaker>";
        public const string UsageUnmute = "Usage: /unmute <speaker>";
        public const string UsageWhisper = "Usage: /w <speaker> message";
        public const string WhisperFrom = "From {0}";
        public const string WhisperTo = "To {0}";
        public const string NotConnected = "Not connected to a chat server. Configure one in Settings > Game Chat, then use '/reconnect'.";
        public const string NoServerConfigured = "No chat server is configured. Set one in Settings > Game Chat to enable cross-player chat.";
        public const string ReceiveError = "Receive error: {0}";
        public const string SendError = "Send error: {0}";
        public const string SendTimedOut = "Send timed out.";
        public const string UserNotFoundInChannel = "User {0} not found in this channel.";
        public const string DebugConsoleTitle = "PhasmaStrap Chat Debugger";
        public const string DebugConsoleInitialized = "DEBUG CONSOLE INITIALIZED AT {0}";
        public const string DebugConsoleUseClose = "Use '/console' or '/debug' again to close";
        public const string HelpHeader = "PhasmaStrap Chat commands";
        public const string ViewProfileTooltip = "Click or right click to view {0}'s profile";
        public const string CopiedUserId = "Copied user id {0}.";
        public const string CtxCopyMessage = "Copy Message";
        public const string CtxCopyUserId = "Copy User ID";
        public const string CtxCopyUsername = "Copy Username";
        public const string CtxViewProfile = "View Profile";
        public const string CtxMuteUser = "Mute User";
        public const string CtxUnmuteUser = "Unmute User";
        public const string CopiedMessage = "Copied message.";
        public const string BugSending = "Sending your bug report...";
        public const string BugSent = "Thanks! Your bug report was sent.";
        public const string BugFailed = "Could not send your bug report. Please try again later.";
        public const string BugCooldown = "Please wait a minute before sending another bug report.";
        public const string BugTooShort = "Please describe the bug in a bit more detail.";

        public static readonly (string Token, string Description)[] CommandTokens = new (string, string)[]
        {
            ("/help", "show the list of commands"),
            ("/about", "about PhasmaStrap Chat"),
            ("/reconnect", "reconnect to the chat server"),
            ("/clear", "clear the chat box"),
            ("/id", "show the current channel id"),
            ("/w", "whisper privately to a speaker"),
            ("/mute", "hide messages from a speaker"),
            ("/unmute", "show a muted speaker again"),
            ("/filter", "set the local message filter"),
            ("/echo", "echo a message back to only you"),
            ("/console", "open or close the debug console"),
            ("/bug", "send a bug report"),
        };

        public static readonly (string Command, string Description)[] HelpEntries = new (string, string)[]
        {
            ("/help", "show this list of commands"),
            ("/about", "about PhasmaStrap Chat"),
            ("/reconnect", "reconnect to the chat server"),
            ("/clear", "clear the chat box"),
            ("/id", "show the current channel id"),
            ("/w <speaker> <message>", "whisper privately to a speaker"),
            ("/mute <speaker>", "hide messages from a speaker"),
            ("/unmute <speaker>", "show a muted speaker again"),
            ("/filter <strict|default|relaxed>", "set the local message filter"),
            ("/echo <text>", "echo a message back to only you"),
            ("/console", "open or close the debug console"),
            ("/bug <description>", "send a bug report"),
        };
    }
}
