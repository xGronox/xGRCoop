using BepInEx;
using UnityEngine;
using HarmonyLib;

namespace xGRCoop;

[BepInPlugin("xGRCoop", "xGRCoop", "2.6")]
public class Plugin : BaseUnityPlugin
{
    private void Awake()
    {
        // bind configs
        ModConfig config = new ModConfig();
        config.MultiplayerToggleKey = Config.Bind<KeyCode>("General", "Toggle Key", KeyCode.F5, "Key used to toggle multiplayer.").Value;
        config.ConnectionType = Config.Bind<ConnectionType>("General", "Connection Type", ConnectionType.STEAM_P2P, "Choose echoserver for standalone or steam_p2p for Steam.").Value;
        config.TickRate = Config.Bind<int>("General", "Tick Rate", 35, "Messages per second sent to the server (higher = smoother but more network usage).").Value;
        config.SyncCompasses = Config.Bind<bool>("General", "Sync Compasses", true, "Enables seeing other players compasses on your map.").Value;

        config.PrintDebugOutput = Config.Bind<bool>("General", "Print Debug Output", false, "Enables advanced logging to help find bugs.").Value;

        config.EchoServerIP = Config.Bind<string>("Standalone", "Server IP Address", "127.0.0.1", "IP Address of the standalone server.").Value;
        config.EchoServerPort = Config.Bind<int>("Standalone", "Server Port", 45565, "Port of the standalone server.").Value;

        config.PlayerOpacity = Config.Bind<float>("Visuals", "Player Opacity", 0.7f, "Opacity of other players (0.0f = invisible, 1.0f = as opaque as yourself).").Value;
        config.ActiveCompassOpacity = Config.Bind<float>("Visuals", "Active Compass Opacity", 0.7f, "Opacity of other players' compasses while they have their map open.").Value;
        config.InactiveCompassOpacity = Config.Bind<float>("Visuals", "Inactive Compass Opacity", 0.35f, "Opacity of other players' compasses while they have their map closed.").Value;
        
        // Настройки никнеймов
        config.ShowPlayerNicknames = Config.Bind<bool>("Nicknames", "Show Player Nicknames", true, "Shows Steam nicknames above players instead of colored dots.").Value;
        config.ShowLocalPlayerNickname = Config.Bind<bool>("Nicknames", "Show Local Player Nickname", true, "Shows your own Steam nickname above your character.").Value;
        config.ShowRemotePlayerNicknames = Config.Bind<bool>("Nicknames", "Show Remote Player Nicknames", true, "Shows Steam nicknames above other players.").Value;
        config.NicknameFontSize = Config.Bind<int>("Nicknames", "Nickname Font Size", 30, "Font size for player nicknames (5-100).").Value;
        config.NicknameOpacity = Config.Bind<float>("Nicknames", "Nickname Opacity", 0.9f, "Opacity of player nicknames (0.0 = invisible, 1.0 = fully opaque).").Value;
        config.RemotePlayerNicknameColorName = Config.Bind<string>("Nicknames", "Remote Player Nickname Color", "Red", "Color name for remote player nicknames. Available colors: White, Red, Blue, Green, Yellow, Cyan, Magenta, Orange, Pink, Purple, Brown, Gray, Black").Value;
        config.NicknameToggleKey = Config.Bind<KeyCode>("General", "Nickname Toggle Key", KeyCode.F8, "Key used to toggle player nicknames on/off.").Value;

        GameObject persistentObject = new GameObject("xGRCoop");
        DontDestroyOnLoad(persistentObject);

        GameSync sync = persistentObject.AddComponent<GameSync>();
        sync.Logger = Logger;
        sync.Config = config;

        UIAdder ua = persistentObject.AddComponent<UIAdder>();
        ua.Logger = Logger;
        ua.Config = config;

        Connector c = null;
        if (config.ConnectionType == ConnectionType.ECHOSERVER) c = persistentObject.AddComponent<StandaloneConnector>();
        if (config.ConnectionType == ConnectionType.STEAM_P2P) c = persistentObject.AddComponent<SteamConnector>();
        c.Logger = Logger;
        c.Config = config;

        if (!c.Init())
        {
            Logger.LogError($"{c.GetName()} has failed to initialize!");
            return;
        }
    }
}
