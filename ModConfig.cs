using UnityEngine;

namespace xGRCoop
{
    public enum ConnectionType
    {
        ECHOSERVER,
        STEAM_P2P
    };

    internal class ModConfig
    {
        public KeyCode MultiplayerToggleKey;
        public KeyCode NicknameToggleKey;
        public ConnectionType ConnectionType;
        public int TickRate;
        public bool SyncCompasses;
        public bool PrintDebugOutput;

        public string EchoServerIP;
        public int EchoServerPort;

        public float PlayerOpacity;
        public float ActiveCompassOpacity;
        public float InactiveCompassOpacity;
        
        // Настройки отображения никнеймов
        public bool ShowPlayerNicknames;
        public bool ShowLocalPlayerNickname; 
        public bool ShowRemotePlayerNicknames;
        public int NicknameFontSize;
        public float NicknameOpacity;
        public string RemotePlayerNicknameColorName;
    };
}
