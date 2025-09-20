using Steamworks;
using System;
using System.Collections.Generic;
using System.Text;

namespace xGRCoop
{
    internal class SteamConnector : Connector
    {
        private enum ELobbyRole { DEFAULT, SERVER, CLIENT }

        // callbacks
        private Callback<GameRichPresenceJoinRequested_t> _gameRichPresenceJoinRequested;
        private Callback<P2PSessionRequest_t> _p2pSessionRequest;
        private Callback<P2PSessionConnectFail_t> _p2pSessionConnectFail;

        // state
        private ELobbyRole _role;
        private CSteamID _ownId;
        private CSteamID _hostId;
        private HashSet<CSteamID> _connected;

        public override string GetName() { return "Steam connector"; }

        public override bool Init()
        {

            if (!SteamAPI.Init())
            {
                Logger.LogError("SteamAPI failed to intialize!");
                return false;
            }


            return base.Init();
        }

        public override void Enable()
        {
            try
            {
                _gameRichPresenceJoinRequested = Callback<GameRichPresenceJoinRequested_t>.Create(OnGameRichPresenceJoinRequested);
                _p2pSessionRequest = Callback<P2PSessionRequest_t>.Create(OnP2PSessionRequest);
                _p2pSessionConnectFail = Callback<P2PSessionConnectFail_t>.Create(OnP2PSessionConnectFail);

                _role = ELobbyRole.DEFAULT;
                _ownId = SteamUser.GetSteamID();
                _hostId = CSteamID.Nil;
                _connected = new HashSet<CSteamID>();

                SteamFriends.SetRichPresence("connect", _ownId.ToString());

                base.Enable();
            } catch (Exception e)
            {
                Logger.LogError($"Error while enabling steam connector: {e}");

                Disable();
            }
        }

        public override void Disable()
        {
            if (!Active) return;

            try
            {
                _gameRichPresenceJoinRequested.Unregister();
                _gameRichPresenceJoinRequested = null;
                _p2pSessionRequest.Unregister();
                _p2pSessionRequest = null;
                _p2pSessionConnectFail.Unregister();
                _p2pSessionConnectFail = null;

                foreach (CSteamID id in _connected)
                    SteamNetworking.CloseP2PSessionWithUser(id);

                SteamFriends.ClearRichPresence();
                
                // Скрыть всех игроков при отключении но сохранить данные
                var gameSync = GetComponent<GameSync>();
                if (gameSync != null)
                {
                    gameSync.Reset(); // Теперь только скрывает, не удаляет данные никнеймов
                }

                base.Disable();
            }
            catch (Exception e)
            {
                Logger.LogError($"Error while disabling steam connector: {e}");
            }
        }

        protected override void Update()
        {
            if (Initialized && Active) SteamAPI.RunCallbacks();

            base.Update();
        }

        private void OnGameRichPresenceJoinRequested(GameRichPresenceJoinRequested_t request)
        {
            // called on the client

            if (_role == ELobbyRole.SERVER)
            {
                return;
            }

            if (!SteamNetworking.AcceptP2PSessionWithUser(request.m_steamIDFriend))
            {
                return;
            }

            _hostId = request.m_steamIDFriend;
            _connected.Add(request.m_steamIDFriend);

            SteamFriends.SetRichPresence("connect", request.m_steamIDFriend.ToString());

            if (_role != ELobbyRole.CLIENT)
            {
                _role = ELobbyRole.CLIENT;
            }

        }

        private void OnP2PSessionRequest(P2PSessionRequest_t request)
        {
            // called on the server

            if (_role == ELobbyRole.CLIENT)
            {
                return;
            }

            if (!SteamNetworking.AcceptP2PSessionWithUser(request.m_steamIDRemote))
            {
                return;
            }

            _hostId = CSteamID.Nil;
            _connected.Add(request.m_steamIDRemote);

            if (_role != ELobbyRole.SERVER)
            {
                _role = ELobbyRole.SERVER;
            }

        }

        private void OnP2PSessionConnectFail(P2PSessionConnectFail_t fail)
        {

            if (!SteamNetworking.CloseP2PSessionWithUser(fail.m_steamIDRemote))
            {
                return;
            }

            _hostId = CSteamID.Nil;
            _connected.Remove(fail.m_steamIDRemote);

            if (_connected.Count == 0)
            {
                _role = ELobbyRole.DEFAULT;
                SteamFriends.SetRichPresence("connect", _ownId.ToString());
                
                // Скрыть всех игроков при потере соединения но сохранить данные
                var gameSync = GetComponent<GameSync>();
                if (gameSync != null)
                {
                    gameSync.Reset(); // Теперь только скрывает, не удаляет данные никнеймов
                }
            }

        }

        protected override void Tick()
        {
            try
            {
                // send
                string updateData = _sync.GetUpdateContent();
                if (updateData != null)
                {
                    byte[] updateMsg = Encoding.UTF8.GetBytes($"{_ownId}::1::{updateData}");
                    foreach (CSteamID id in _connected) SteamNetworking.SendP2PPacket(id, updateMsg, (uint) updateMsg.Length, EP2PSend.k_EP2PSendReliable);
                }

                // receive
                while (SteamNetworking.IsP2PPacketAvailable(out uint msgSize))
                {
                    byte[] buffer = new byte[msgSize];

                    if (SteamNetworking.ReadP2PPacket(buffer, msgSize, out _, out CSteamID sender))
                    {
                        string data = Encoding.UTF8.GetString(buffer);

                        string[] parts = data.Split("::");

                        string metadata = $"{_connected.Count}";

                        data = $"{parts[0]}::{metadata}::{parts[2]}";

                        _sync.ApplyUpdate(data);

                        if (_role == ELobbyRole.SERVER)
                        {
                            foreach (CSteamID id in _connected)
                                if (id != sender)
                                    SteamNetworking.SendP2PPacket(id, buffer, (uint)buffer.Length, EP2PSend.k_EP2PSendReliable);
                        }
                    }
                }
            }

            catch (Exception e)
            
            {
                Logger.LogError($"Error during tick: {e}");
                Disable();
            }
        }
    }
}
