using BepInEx.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Steamworks;

namespace xGRCoop
{
    internal class GameSync : MonoBehaviour
    {
        private static float stof(string s)
        {
            float f1;
            try { f1 = float.Parse(s.Replace(",", ".")); } catch (Exception) { f1 = float.MaxValue; }
            float f2;
            try { f2 = float.Parse(s.Replace(".", ",")); } catch (Exception) { f2 = float.MaxValue; }

            if (Mathf.Abs(f1) < Mathf.Abs(f2)) return f1;
            else return f2;
        }

        #region Fields and Variables
        
        public ManualLogSource Logger;
        public ModConfig Config;

        // sprite sync - self
        private GameObject _hornetObject = null;
        private tk2dSprite _hornetSprite = null;
        private Rigidbody2D _hornetRigidbody = null;
        
        // sprite sync - others
        private Dictionary<string, GameObject> _playerObjects = new Dictionary<string, GameObject>();
        private Dictionary<string, tk2dSprite> _playerSprites = new Dictionary<string, tk2dSprite>();
        private Dictionary<string, SimpleInterpolator> _playerInterpolators = new Dictionary<string, SimpleInterpolator>();
        
        // Enhanced multiplayer sync - track previous states for smoother updates
        private Dictionary<string, int> _playerPreviousSpriteIds = new Dictionary<string, int>();
        private Dictionary<string, Vector3> _playerPreviousPositions = new Dictionary<string, Vector3>();
        private Dictionary<string, float> _playerLastUpdateTime = new Dictionary<string, float>();
        
        // Track player names for display
        private Dictionary<string, string> _playerNames = new Dictionary<string, string>();
        
        // Direct nickname positioning (no interpolation)
        private Dictionary<string, Vector3> _nicknamePositions = new Dictionary<string, Vector3>();
        
        // Последние известные позиции игроков (для никнеймов при смене сцен)
        private Dictionary<string, Vector3> _lastKnownPositions = new Dictionary<string, Vector3>();
        
        // Отслеживание активных игроков для очистки никнеймов
        private Dictionary<string, float> _activePlayersLastSeen = new Dictionary<string, float>();
        private const float PLAYER_TIMEOUT_SECONDS = 10.0f; 
        
        // Цвет для локального игрока (используется только для никнейма)
        private Color _localPlayerColor = Color.white;
        
        // player nickname system
        private Dictionary<string, PlayerNameUI> _playerNicknames = new Dictionary<string, PlayerNameUI>();
        private PlayerNameUI _localPlayerNickname = null;
        private float _lastNicknameUpdate = 0f;
        private const float NICKNAME_UPDATE_INTERVAL = 0.005f; // 200 FPS для максимальной плавности!
        private float _lastNicknameRefresh = 0f;
        private const float NICKNAME_REFRESH_INTERVAL = 3.0f; // Обновление каждые 3 секунды
        private Camera _mainCamera;
        
        // Scene change handling
        private bool _sceneHandlerSubscribed = false;

        // player count
        private GameObject _pauseMenu = null;
        private int _playerCount = 0;
        private List<GameObject> _countPins = new List<GameObject>();

        // map sync - self
        private GameObject _mainQuests = null;
        private GameObject _map = null;
        private GameObject _compass = null;

        // map sync - others
        private Dictionary<string, GameObject> _playerCompasses = new Dictionary<string, GameObject>();
        private Dictionary<string, tk2dSprite> _playerCompassSprites = new Dictionary<string, tk2dSprite>();

        private bool _setup = false;

        #endregion
        
        #region Core Game Methods

        private void Update()
        {
            if (!_sceneHandlerSubscribed)
            {
                SceneManager.activeSceneChanged += OnSceneChanged;
                _sceneHandlerSubscribed = true;
            }
            
            if (!_hornetObject) _hornetObject = GameObject.Find("Hero_Hornet");
            if (!_hornetObject) _hornetObject = GameObject.Find("Hero_Hornet(Clone)");
            if (!_hornetObject) { _setup = false; return; }

            if (!_hornetSprite) _hornetSprite = _hornetObject.GetComponent<tk2dSprite>();
            if (!_hornetSprite) { _setup = false; return; }

            if (!_hornetRigidbody) _hornetRigidbody = _hornetObject.GetComponent<Rigidbody2D>();
            if (!_hornetRigidbody) { _setup = false; return; }

            if (!_map) _map = GameObject.Find("Game_Map_Hornet");
            if (!_map) _map = GameObject.Find("Game_Map_Hornet(Clone)");
            if (!_map) { _setup = false; return; }

            if (!_compass) _compass = _map.transform.Find("Compass Icon")?.gameObject;
            if (!_compass) { _setup = false; return; }

            if (!_pauseMenu) _pauseMenu = Resources.FindObjectsOfTypeAll<GameObject>().FirstOrDefault(g => g.name == "NewPauseMenuScreen");
            if (!_pauseMenu) { _setup = false; return; }

            if (!_mainQuests) _mainQuests = _map.transform.Find("Main Quest Pins")?.gameObject;
            if (!_mainQuests) { _setup = false; return; }

            if (Config.SyncCompasses)
            {
                foreach (GameObject g in _playerCompasses.Values)
                    if (g != null) g.SetActive(_mainQuests.activeSelf);
            }

            foreach (GameObject g in _countPins)
                if (g != null) g.SetActive(_mainQuests.activeSelf);

            if (!_setup)
            {
                _setup = true;
            }
            
            // 🔧 ИСПРАВЛЕНИЕ: Постоянная проверка на создание локального никнейма
            if (_setup && Config.ShowPlayerNicknames && Config.ShowLocalPlayerNickname)
            {
                // Проверить нужно ли создать/восстановить локальный никнейм
                bool needLocalNickname = !_playerNicknames.ContainsKey("local") || 
                                        _playerNicknames["local"].Container == null;
                
                if (needLocalNickname && _hornetObject != null)
                {
                    // Настроить никнейм локального игрока
                    SetupLocalPlayerNickname();
                }
            }
            
            // Обновить систему никнеймов с оптимизированной частотой
            if (Time.time - _lastNicknameUpdate >= NICKNAME_UPDATE_INTERVAL)
            {
                UpdatePlayerNicknamesUI();
                _lastNicknameUpdate = Time.time;
            }
            
            // Принудительное обновление никнеймов каждые 3 секунды
            if (Time.time - _lastNicknameRefresh >= NICKNAME_REFRESH_INTERVAL)
            {
                RefreshAllNicknames();
                _lastNicknameRefresh = Time.time;
            }
            
            // Проверка тайм-аута игроков каждые 3 секунды
            if (Time.time - _lastNicknameRefresh >= NICKNAME_REFRESH_INTERVAL)
            {
                CheckPlayerTimeouts();
            }
            
            // Обработка клавиши переключения никнеймов F8
            if (Input.GetKeyDown(Config.NicknameToggleKey) || Input.GetKeyDown(KeyCode.F8))
            {
                Config.ShowPlayerNicknames = !Config.ShowPlayerNicknames;
                
                // Если никнеймы отключены - скрыть все
                if (!Config.ShowPlayerNicknames)
                {
                    foreach (var nickname in _playerNicknames.Values)
                    {
                        if (nickname.Container != null)
                        {
                            nickname.Container.SetActive(false);
                        }
                    }
                }
                // Если включены - показать согласно настройкам
                else
                {
                    foreach (var kvp in _playerNicknames)
                    {
                        var nickname = kvp.Value;
                        if (nickname.Container != null)
                        {
                            bool shouldShow = (nickname.IsLocal && Config.ShowLocalPlayerNickname) ||
                                            (!nickname.IsLocal && Config.ShowRemotePlayerNicknames);
                            nickname.Container.SetActive(shouldShow);
                        }
                    }
                }
            }
        }

        public string GetUpdateContent()
        {
            if (!_setup) return null;

            string scene = SceneManager.GetActiveScene().name;
            float posX = _hornetObject.transform.position.x;
            float posY = _hornetObject.transform.position.y;
            float posZ = _hornetObject.transform.position.z;
            int spriteId = _hornetSprite.spriteId;
            float scaleX = _hornetObject.transform.localScale.x;
            float vX = _hornetRigidbody.linearVelocity.x;
            float vY = _hornetRigidbody.linearVelocity.y;

            int compassActive = 0;
            float compassX = 0;
            float compassY = 0;

            if (Config.SyncCompasses)
            {
                compassActive = _compass.activeSelf ? 1 : 0;
                compassX = _compass.transform.localPosition.x;
                compassY = _compass.transform.localPosition.y;
            }

            string baseData = $"{scene}:{posX}:{posY}:{posZ}:{spriteId}:{scaleX}:{vX}:{vY}";
            string compassData = Config.SyncCompasses ? $":{compassActive}:{compassX}:{compassY}" : "";
            
            string data = $"{baseData}{compassData}";
            return data;
        }

        public void ApplyUpdate(string data)
        {
            try
            {
                if (!_setup) return;

                UpdateUI();

                string[] parts = data.Split("::");
                string id = parts[0];
                string[] metadataParts = parts[1].Split(":");
                string[] contentParts = parts[2].Split(":");

                _playerCount = int.Parse(metadataParts[0]);

                string scene = contentParts[0];
                float posX = stof(contentParts[1]);
                float posY = stof(contentParts[2]);
                float posZ = stof(contentParts[3]);
                int spriteId = int.Parse(contentParts[4]);
                float scaleX = stof(contentParts[5]);
                float vX = stof(contentParts[6]);
                float vY = stof(contentParts[7]);
                
                // Обновить время последней активности игрока
                _activePlayersLastSeen[id] = Time.time;

                bool compassActive = false;
                float compassX = 0;
                float compassY = 0;

                if (Config.SyncCompasses && contentParts.Length > 8)
                {
                    compassActive = contentParts[8] == "1";
                    compassX = stof(contentParts[9]);
                    compassY = stof(contentParts[10]);
                }

                bool sameScene = scene == SceneManager.GetActiveScene().name;

                if (!_playerObjects.ContainsKey(id))
                {
                    _playerObjects.Add(id, null);
                    _playerSprites.Add(id, null);
                    _playerInterpolators.Add(id, null);
                }

                if (!_playerCompasses.ContainsKey(id))
                {
                    _playerCompasses.Add(id, null);
                    if (!_playerCompassSprites.ContainsKey(id)) _playerCompassSprites.Add(id, null);
                }

                if (!sameScene)
                {
                    // скрыть объект игрока но сохранить никнейм
                    if (_playerObjects.ContainsKey(id))
                        if (_playerObjects[id] != null)
                        {
                            _playerObjects[id].SetActive(false);
                        }
                    
                    // Временно скрыть никнейм игрока в другой сцене но не удалять данные
                    if (_playerNicknames.ContainsKey(id) && _playerNicknames[id].Container != null)
                    {
                        _playerNicknames[id].Container.SetActive(false);
                    }  
                } else {
                    if (_playerObjects[id] != null)
                    {
                        // показать объект игрока если он был скрыт
                        if (!_playerObjects[id].activeSelf)
                        {
                            _playerObjects[id].SetActive(true);
                        }
                        
                        // Показать никнейм игрока обратно
                        if (_playerNicknames.ContainsKey(id) && _playerNicknames[id].Container != null)
                        {
                            if (Config.ShowPlayerNicknames && Config.ShowRemotePlayerNicknames)
                            {
                                _playerNicknames[id].Container.SetActive(true);
                            }
                        }
                        // Enhanced update with change detection
                        Vector3 newPosition = new Vector3(posX, posY, posZ + 0.001f);
                        Vector3 previousPosition = _playerPreviousPositions.ContainsKey(id) ? _playerPreviousPositions[id] : newPosition;
                        int previousSpriteId = _playerPreviousSpriteIds.ContainsKey(id) ? _playerPreviousSpriteIds[id] : -1;
                        float currentTime = Time.time;
                        
                        // Update position (always)
                        _playerObjects[id].transform.position = newPosition;
                        _playerObjects[id].transform.localScale = new Vector3(scaleX, 1, 1);
                        _playerInterpolators[id].velocity = new Vector3(vX, vY, 0);
                        
                        // Сохранить последнюю позицию для никнейма
                        _lastKnownPositions[id] = newPosition;
                        
                        // Enhanced sprite animation update with change detection
                        if (previousSpriteId != spriteId || _playerSprites[id].spriteId != spriteId)
                        {
                            try
                            {
                                _playerSprites[id].spriteId = spriteId;
                                _playerSprites[id].ForceBuild();
                                
                            }
                            catch (System.Exception)
                            {
                                try
                                {
                                    _playerSprites[id].spriteId = 0;
                                    _playerSprites[id].ForceBuild();
                                }
                                catch (System.Exception)
                                {

                                }
                            }
                        }
                        
                        if (Config.ShowPlayerNicknames && Config.ShowRemotePlayerNicknames && _playerNicknames.ContainsKey(id))
                        {
                            Vector3 remotePlayerPos = _playerObjects[id].transform.position;
                            Vector3 nicknamePos = remotePlayerPos + new Vector3(0f, 2.4f, 0f);
                            
                            // Мгновенное обновление позиции никнейма
                            var nameUI = _playerNicknames[id];
                            if (nameUI.Container != null)
                            {
                                nameUI.Container.transform.position = nicknamePos;
                                nameUI.Container.transform.rotation = Quaternion.identity;
                            }
                        }
                        
                        if (!_playerPreviousPositions.ContainsKey(id)) _playerPreviousPositions.Add(id, newPosition);
                        else _playerPreviousPositions[id] = newPosition;
                        
                        if (!_playerPreviousSpriteIds.ContainsKey(id)) _playerPreviousSpriteIds.Add(id, spriteId);
                        else _playerPreviousSpriteIds[id] = spriteId;
                        
                        if (!_playerLastUpdateTime.ContainsKey(id)) _playerLastUpdateTime.Add(id, currentTime);
                        else _playerLastUpdateTime[id] = currentTime;
                    }
                    else
                    {
                        // create player
                        string playerName = GetSteamName(id);
                        _playerNames[id] = playerName;
                        

                        GameObject newObject = new GameObject();
                        newObject.name = $"xGRCooperator_{playerName}";
                        newObject.transform.position = new Vector3(posX, posY, posZ + 0.001f);
                        newObject.transform.localScale = new Vector3(scaleX, 1, 1);

                        tk2dSprite newSprite = tk2dSprite.AddComponent(newObject, _hornetSprite.Collection, 0);
                        
                        newSprite.color = new Color(1, 1, 1, Config.PlayerOpacity);

                        SimpleInterpolator newInterpolator = newObject.AddComponent<SimpleInterpolator>();
                        newInterpolator.velocity = new Vector3(vX, vY, 0);

                        _playerObjects[id] = newObject;
                        _playerSprites[id] = newSprite;
                        _playerInterpolators[id] = newInterpolator;

                        _playerPreviousPositions[id] = new Vector3(posX, posY, posZ + 0.001f);
                        _playerPreviousSpriteIds[id] = spriteId;
                        _playerLastUpdateTime[id] = Time.time;
                        
                        // Сохранить начальную позицию для никнейма
                        _lastKnownPositions[id] = new Vector3(posX, posY, posZ + 0.001f);
                        
                        
                        if (Config.ShowPlayerNicknames && Config.ShowRemotePlayerNicknames)
                        {
                            string remotePlayerName = GetSteamName(id);
                            Vector3 playerPosition = newObject.transform.position;
                            Vector3 nicknameInitialPos = playerPosition + new Vector3(0f, 2.4f, 0f);
                            Color remoteColor = GetColorFromName(Config.RemotePlayerNicknameColorName);
                            
                            CreatePlayerNickname(id, remotePlayerName, remoteColor, playerPosition, false);
                        }

                    }
                }
                
                if (Config.SyncCompasses)
                {
                    if (compassActive)
                    {
                        if (_playerCompasses[id] != null)
                        {
                            // update compass
                            _playerCompasses[id].transform.localPosition = new Vector3(compassX, compassY, _compass.transform.localPosition.z + 0.001f);
                            _playerCompassSprites[id].color = new Color(1, 1, 1, Config.ActiveCompassOpacity);
                        }
                        else
                        {
                            // create compass

                            GameObject newObject = Instantiate(_compass, _map.transform);
                            newObject.SetName("xGRCoopCompass");
                            newObject.transform.localPosition = new Vector3(compassX, compassY, _compass.transform.localPosition.z + 0.001f);
                            tk2dSprite newSprite = newObject.GetComponent<tk2dSprite>();
                            newSprite.color = new Color(1, 1, 1, Config.ActiveCompassOpacity);

                            _playerCompasses[id] = newObject;
                            _playerCompassSprites[id] = newSprite;

                        }
                    }
                    else
                    {
                        if (_playerCompasses[id] != null)
                            _playerCompassSprites[id].color = new Color(1, 1, 1, Config.InactiveCompassOpacity);
                    }
                }
            } catch (Exception e)
            {
                Logger.LogError($"Error while applying update: {e}");
            }
        }
        
        private void OnSceneChanged(Scene prevScene, Scene newScene)
        {
            // Проверка активных игроков и удаление неактивных никнеймов
            var inactivePlayerIds = new List<string>();
            
            foreach (var playerId in _playerNicknames.Keys.ToList())
            {
                // Пропустить локального игрока
                if (playerId == "local") continue;
                
                // Если игрока нет в списке активных или он давно не обновлялся
                if (!_activePlayersLastSeen.ContainsKey(playerId) || 
                    !_playerNames.ContainsKey(playerId))
                {
                    inactivePlayerIds.Add(playerId);
                }
            }
            
            // Удалить неактивных игроков
            foreach (var playerId in inactivePlayerIds)
            {
                RemovePlayerNickname(playerId);
            }
            
            foreach (var kvp in _playerNames)
            {
                string playerId = kvp.Key;
                string playerName = kvp.Value;
                
                // Если никнейм существует но скрыт - показать его снова
                if (_playerNicknames.ContainsKey(playerId) && _playerNicknames[playerId].Container != null)
                {
                    if (Config.ShowPlayerNicknames && Config.ShowRemotePlayerNicknames)
                    {
                        _playerNicknames[playerId].Container.SetActive(true);
                        
                        // Обновить позицию на основе последней известной
                        if (_lastKnownPositions.ContainsKey(playerId))
                        {
                            Vector3 lastPos = _lastKnownPositions[playerId];
                            Vector3 nicknamePos = lastPos + new Vector3(0f, 2.4f, 0f);
                            _playerNicknames[playerId].Container.transform.position = nicknamePos;
                        }
                    }
                }
                // Если никнейм не существует - создать новый
                else if (!_playerNicknames.ContainsKey(playerId))
                {
                    Vector3 pos = _lastKnownPositions.ContainsKey(playerId) ? _lastKnownPositions[playerId] : Vector3.zero;
                    Color nicknameColor = GetColorFromName(Config.RemotePlayerNicknameColorName);
                    CreatePlayerNickname(playerId, playerName, nicknameColor, pos, false);
                }
            }
        }
        
        private void OnDestroy()
        {
            if (_sceneHandlerSubscribed)
            {
                SceneManager.activeSceneChanged -= OnSceneChanged;
                _sceneHandlerSubscribed = false;
            }
        }

        #endregion
        
        #region Color Utils
        
        /// <summary>
        /// Конвертировать строковое название цвета в Unity Color
        /// </summary>
        private Color GetColorFromName(string colorName)
        {
            switch (colorName.ToLower())
            {
                case "white": return Color.white;
                case "red": return Color.red;
                case "blue": return Color.blue;
                case "green": return Color.green;
                case "yellow": return Color.yellow;
                case "cyan": return Color.cyan;
                case "magenta": return Color.magenta;
                case "orange": return new Color(1f, 0.5f, 0f); // Orange
                case "pink": return new Color(1f, 0.75f, 0.8f); // Pink
                case "purple": return new Color(0.5f, 0f, 0.5f); // Purple
                case "brown": return new Color(0.65f, 0.16f, 0.16f); // Brown
                case "gray": case "grey": return Color.gray;
                case "black": return Color.black;
                default:
                    return Color.red;
            }
        }
        
        #endregion
        
        #region Utility Methods

        private void UpdateUI()
        {
            try
            {
                while (_countPins.Count < _playerCount)
                {

                    GameObject newPin = Instantiate(_compass, _map.transform);
                    newPin.SetName("xGRCoopPlayerCountPin");
                    _countPins.Add(newPin);

                }

                while (_countPins.Count > _playerCount)
                {

                    Destroy(_countPins[_countPins.Count - 1]);
                    _countPins.RemoveAt(_countPins.Count - 1);

                }

                for (int i = 0; i < _countPins.Count; i++)
                    _countPins[i].transform.position = new Vector3(-14.8f + i * 0.9f, -8.2f, -5f);
            }
            catch (Exception e)
            {
                Logger.LogError($"Error while updating ui: {e}");
            }
        }



        public void Reset()
        {
            foreach (GameObject g in _playerObjects.Values)
                if (g != null) Destroy(g);
            _playerObjects.Clear();
            _playerSprites.Clear();
            _playerInterpolators.Clear();

            foreach (GameObject g in _countPins)
                if (g != null) Destroy(g);
            _countPins.Clear();

            foreach (GameObject g in _playerCompasses.Values)
                if (g != null) Destroy(g);
            _playerCompasses.Clear();
            
            // Скрыть никнеймы но сохранить данные для возможного восстановления
            HideAllRemoteNicknames();
            
            // Clear player object data но сохранить имена и позиции
            _playerNames.Clear();
            
            // Clear enhanced sync tracking data
            _playerPreviousPositions.Clear();
            _playerPreviousSpriteIds.Clear();
            _playerLastUpdateTime.Clear();
            
            // Clear player activity tracking
            _activePlayersLastSeen.Clear();
            
            // Clear nickname position data только при полном отключении
            _nicknamePositions.Clear();
        }
        
        /// <summary>
        /// Скрыть все удаленные никнеймы но сохранить данные
        /// </summary>
        private void HideAllRemoteNicknames()
        {
            foreach (var kvp in _playerNicknames)
            {
                if (!kvp.Value.IsLocal && kvp.Value.Container != null)
                {
                    kvp.Value.Container.SetActive(false);
                }
            }
        }
        
        /// <summary>
        /// Полное удаление всех данных никнеймов (только при выходе из игры)
        /// </summary>
        public void FullReset()
        {
            Reset();
            
            // Теперь очистить все данные никнеймов
            ClearRemotePlayerNicknames();
            _lastKnownPositions.Clear();
            _activePlayersLastSeen.Clear();
        }
        
        /// <summary>
        /// Get Steam display name for player ID
        /// </summary>
        private string GetSteamName(string playerId)
        {
            try
            {
                // Если это локальный игрок
                if (playerId == "local")
                {
                    return SteamFriends.GetPersonaName();
                }
                
                // Для удаленных игроков - получить Steam имя через SteamID
                if (ulong.TryParse(playerId, out ulong steamIdUlong))
                {
                    CSteamID steamId = new CSteamID(steamIdUlong);
                    string friendName = SteamFriends.GetFriendPersonaName(steamId);
                    
                    // Если имя получено и не пустое
                    if (!string.IsNullOrEmpty(friendName) && friendName != "[unknown]")
                    {
                        return friendName;
                    }
                }
                
                // Fallback - последние 4 цифры SteamID
                return $"Player_{playerId.Substring(Math.Max(0, playerId.Length - 4))}";
            }
            catch (Exception)
            {
                return $"Player_{playerId.Substring(Math.Max(0, playerId.Length - 4))}";
            }
        }


        #endregion
        
        #region Player Nickname System
        
        /// <summary>
        /// Принудительное обновление всех никнеймов (каждые 3 секунды)
        /// </summary>
        private void RefreshAllNicknames()
        {
            if (!Config.ShowPlayerNicknames) return;
            
            // Обновить локальный никнейм
            if (Config.ShowLocalPlayerNickname && _hornetObject != null)
            {
                if (!_playerNicknames.ContainsKey("local") || _playerNicknames["local"].Container == null)
                {
                    SetupLocalPlayerNickname();
                }
            }
            
            // Обновить никнеймы удаленных игроков
            if (Config.ShowRemotePlayerNicknames)
            {
                foreach (var kvp in _playerObjects)
                {
                    string playerId = kvp.Key;
                    GameObject playerObject = kvp.Value;
                    
                    if (playerObject != null && !_playerNicknames.ContainsKey(playerId))
                    {
                        // Создать никнейм если его нет
                        string playerName = GetSteamName(playerId);
                        Vector3 playerPos = playerObject.transform.position;
                        Color nicknameColor = GetColorFromName(Config.RemotePlayerNicknameColorName);
                        CreatePlayerNickname(playerId, playerName, nicknameColor, playerPos, false);
                    }
                    else if (playerObject != null && _playerNicknames.ContainsKey(playerId))
                    {
                        // Убедиться что никнейм активен и правильно позиционирован
                        var nameUI = _playerNicknames[playerId];
                        if (nameUI.Container != null)
                        {
                            nameUI.Container.SetActive(true);
                            Vector3 playerPos = playerObject.transform.position;
                            Vector3 nicknamePos = playerPos + new Vector3(0f, 2.4f, 0f);
                            nameUI.Container.transform.position = nicknamePos;
                            nameUI.Container.transform.rotation = Quaternion.identity;
                        }
                    }
                }
            }
        }
        
        /// <summary>
        /// Создать или обновить никнейм игрока
        /// </summary>
        public void CreatePlayerNickname(string playerId, string displayName, Color nameColor, Vector3 worldPosition, bool isLocal = false)
        {
            if (_playerNicknames.ContainsKey(playerId))
            {
                UpdatePlayerNickname(playerId, displayName, nameColor, worldPosition);
                return;
            }
            
            try
            {
                
                GameObject nicknameCanvas = new GameObject($"PlayerNickname_{playerId}");
                DontDestroyOnLoad(nicknameCanvas);
                
                Canvas worldCanvas = nicknameCanvas.AddComponent<Canvas>();
                worldCanvas.renderMode = RenderMode.WorldSpace;
                worldCanvas.sortingOrder = 100;
                worldCanvas.pixelPerfect = true; // Включить pixelPerfect для четкости
                
                nicknameCanvas.AddComponent<UnityEngine.UI.GraphicRaycaster>();
                
                var scaler = nicknameCanvas.AddComponent<UnityEngine.UI.CanvasScaler>();
                scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ConstantPixelSize;
                scaler.scaleFactor = 1.0f;
                
                var canvasRect = nicknameCanvas.GetComponent<RectTransform>();
                Vector2 nicknameSize = new Vector2(3000f, 600f);
                canvasRect.sizeDelta = nicknameSize;
                canvasRect.localScale = Vector3.one * 0.018f;
                
                Vector3 nicknameWorldPos = worldPosition + new Vector3(0f, 2.4f, 0f);
                nicknameCanvas.transform.position = nicknameWorldPos;
                nicknameCanvas.transform.rotation = Quaternion.identity;
                
                GameObject textObj = new GameObject("NicknameText");
                textObj.transform.SetParent(nicknameCanvas.transform, false);
                textObj.AddComponent<CanvasRenderer>();
                
                var nicknameText = textObj.AddComponent<UnityEngine.UI.Text>();
                nicknameText.text = displayName;

                nicknameText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                nicknameText.fontSize = Mathf.Max(20, Config.NicknameFontSize - 10);
                nicknameText.color = Color.white;
                nicknameText.alignment = TextAnchor.MiddleCenter;
                nicknameText.fontStyle = FontStyle.Bold;
                
                int actualFontSize = Mathf.Max(20, Config.NicknameFontSize - 10);
                nicknameText.resizeTextForBestFit = false;
                nicknameText.resizeTextMinSize = actualFontSize;
                nicknameText.resizeTextMaxSize = actualFontSize;
                
                if (nicknameText.material != null && nicknameText.material.mainTexture != null)
                {
                    nicknameText.material.mainTexture.filterMode = FilterMode.Point;
                }
                
                if (nicknameText.font != null && nicknameText.font.material != null && nicknameText.font.material.mainTexture != null)
                {
                    nicknameText.font.material.mainTexture.filterMode = FilterMode.Point;
                }
                
                var textRect = textObj.GetComponent<RectTransform>();
                textRect.anchorMin = Vector2.zero;
                textRect.anchorMax = Vector2.one;
                textRect.offsetMin = Vector2.zero;
                textRect.offsetMax = Vector2.zero;
                
                worldCanvas.enabled = true;
                nicknameCanvas.SetActive(true);
                textObj.SetActive(true);
                
                nicknameText.color = Color.white;
                Camera mainCamera = Camera.main;
                if (mainCamera != null)
                {
                    int layerMask = mainCamera.cullingMask;
                    bool canSeeDefaultLayer = (layerMask & (1 << 0)) != 0;
                }
                
                var playerNameUI = new PlayerNameUI
                {
                    PlayerId = playerId,
                    DisplayName = displayName,
                    NameColor = nameColor,
                    Container = nicknameCanvas,
                    TextComponent = nicknameText,
                    Canvas = worldCanvas,
                    IsLocal = isLocal,
                    LastUpdateTime = Time.time
                };
                
                _playerNicknames[playerId] = playerNameUI;
            }
            catch (Exception)
            {
                return; 
            }
        }
        
        /// <summary>
        /// Обновить никнейм существующего игрока (поддерживает TextMesh и Canvas)
        /// </summary>
        public void UpdatePlayerNickname(string playerId, string displayName, Color nameColor, Vector3 worldPosition)
        {
            if (!_playerNicknames.ContainsKey(playerId)) return;
            
            var nameUI = _playerNicknames[playerId];
            if (nameUI.Container == null) return;
            
            var textMesh = nameUI.Container.GetComponent<TextMesh>();
            bool textMeshUpdated = false;
            
            if (textMesh != null)
            {
                if (!nameUI.IsLocal)
                {
                    nameUI.Container.transform.position = new Vector3(worldPosition.x, worldPosition.y + 1.2f, worldPosition.z);
                }
                
                if (nameUI.DisplayName != displayName)
                {
                    nameUI.DisplayName = displayName;
                    textMesh.text = displayName;
                }
                
                if (nameUI.NameColor != nameColor)
                {
                    nameUI.NameColor = nameColor;
                    textMesh.color = new Color(nameColor.r, nameColor.g, nameColor.b, Config.NicknameOpacity);
                }
                
                textMeshUpdated = true;
            }
            
            if (!textMeshUpdated && nameUI.TextComponent != null)
            {
                if (!nameUI.IsLocal)
                {
                    nameUI.Container.transform.position = new Vector3(worldPosition.x, worldPosition.y + 1.2f, worldPosition.z);
                }
                
                if (nameUI.DisplayName != displayName)
                {
                    nameUI.DisplayName = displayName;
                    nameUI.TextComponent.text = displayName;
                }
                
                if (nameUI.NameColor != nameColor)
                {
                    nameUI.NameColor = nameColor;
                    nameUI.TextComponent.color = new Color(nameColor.r, nameColor.g, nameColor.b, Config.NicknameOpacity);
                }
            }
            
            nameUI.LastUpdateTime = Time.time;
        }
        
        /// <summary>
        /// Setup local player nickname
        /// </summary>
        private void SetupLocalPlayerNickname()
        {
            if (!Config.ShowPlayerNicknames || !Config.ShowLocalPlayerNickname) 
            {
                return;
            }
            
            if (_hornetObject == null) 
            {
                return;
            }
            
            string localPlayerName = "Player";
            
            try
            {
                localPlayerName = SteamFriends.GetPersonaName();
                if (string.IsNullOrEmpty(localPlayerName))
                {
                    localPlayerName = "Player";
                }
            }
            catch (Exception)
            {
                localPlayerName = "Player";
            }
            
            Vector3 playerPos = _hornetObject.transform.position;
            Vector3 nicknamePos = new Vector3(playerPos.x, playerPos.y + 1.8f, playerPos.z - 0.1f);
            
            if (_playerNicknames.ContainsKey("local"))
            {
                var localNickname = _playerNicknames["local"];
                if (localNickname.Container != null)
                {
                    Destroy(localNickname.Container);
                }
                _playerNicknames.Remove("local");
                _localPlayerNickname = null;
            }
            
            CreatePlayerNickname("local", localPlayerName, _localPlayerColor, nicknamePos, true);
                        
            if (_playerNicknames.ContainsKey("local"))
            {
                _localPlayerNickname = _playerNicknames["local"];
            }
        }
        
        /// <summary>
        /// Обновить видимость и позиции всех никнеймов
        /// </summary>
        private void UpdatePlayerNicknamesUI()
        {
            if (!Config.ShowPlayerNicknames) return;
            
            // Найти активную камеру для расчета расстояний
            if (_mainCamera == null) 
            {
                _mainCamera = Camera.main;
                // В крайнем случае взять первую доступную камеру
                if (_mainCamera == null)
                {
                    Camera[] cameras = Camera.allCameras;
                    if (cameras.Length > 0)
                    {
                        _mainCamera = cameras[0];
                    }
                }
            }
            if (_mainCamera == null) return;
            
            foreach (var nameUI in _playerNicknames.Values)
            {
                if (nameUI.Container == null) continue;
                
                var textMesh = nameUI.Container.GetComponent<TextMesh>();
                bool hasTextMesh = textMesh != null;
                bool hasCanvas = nameUI.TextComponent != null && nameUI.Canvas != null;
                
                if (!hasTextMesh && !hasCanvas) continue;

                if (nameUI.IsLocal && _hornetObject != null)
                {
                    // Для Canvas - обновляем мировую позицию (не локальную!)
                    if (hasCanvas)
                    {
                        // Позиция как в EnemyHealthBar - мировая позиция + смещение выше
                        Vector3 worldPos = _hornetObject.transform.position + new Vector3(0f, 2.4f, 0f);
                        nameUI.Container.transform.position = worldPos;
                        nameUI.Container.transform.rotation = Quaternion.identity;
                    }
                    else if (hasTextMesh)
                    {
                        // Для TextMesh - старая логика (но нам она не нужна)
                        nameUI.Container.transform.localPosition = new Vector3(0f, 2.4f, -0.1f);
                        Vector3 scale = Vector3.one * 1.0f;

                        bool characterFacingRight = _hornetObject.transform.localScale.x < 0f;
                        if (characterFacingRight)
                        {
                            scale.x = -Mathf.Abs(scale.x);
                        }
                        else
                        {
                            scale.x = Mathf.Abs(scale.x);
                        }
                        nameUI.Container.transform.localScale = scale;
                    }
                }
                else
                {
                    // Найти соответствующий объект удаленного игрока
                    string remotePlayerId = null;
                    foreach (var kvp in _playerNicknames)
                    {
                        if (kvp.Value == nameUI)
                        {
                            remotePlayerId = kvp.Key;
                            break;
                        }
                    }

                    if (remotePlayerId != null && remotePlayerId != "local")
                    {
                        Vector3 nicknamePos = Vector3.zero;
                        bool hasPosition = false;
                        
                        // Попытка получить позицию от активного объекта игрока
                        if (_playerObjects.ContainsKey(remotePlayerId) && _playerObjects[remotePlayerId] != null && _playerObjects[remotePlayerId].activeSelf)
                        {
                            GameObject remotePlayerObject = _playerObjects[remotePlayerId];
                            Vector3 remotePlayerPos = remotePlayerObject.transform.position;
                            nicknamePos = remotePlayerPos + new Vector3(0f, 2.4f, 0f);
                            hasPosition = true;
                        }
                        // Использовать последнюю известную позицию если объект недоступен
                        else if (_lastKnownPositions.ContainsKey(remotePlayerId))
                        {
                            Vector3 lastPos = _lastKnownPositions[remotePlayerId];
                            nicknamePos = lastPos + new Vector3(0f, 2.4f, 0f);
                            hasPosition = true;
                        }
                        
                        // Обновить позицию никнейма если позиция найдена
                        if (hasPosition)
                        {
                            nameUI.Container.transform.position = nicknamePos;
                            nameUI.Container.transform.rotation = Quaternion.identity;
                        }
                    }
                }
                
                // никнейм всегда видим (убрали проверки расстояния)
                bool shouldBeVisible = true;
                
                // Показать/скрыть локальный никнейм в зависимости от настроек
                if (nameUI.IsLocal && !Config.ShowLocalPlayerNickname)
                {
                    shouldBeVisible = false;
                }
                
                // Показать/скрыть удаленные никнеймы в зависимости от настроек
                if (!nameUI.IsLocal && !Config.ShowRemotePlayerNicknames)
                {
                    shouldBeVisible = false;
                }
                
                // Показать/скрыть объект целиком (или Canvas если используется Canvas подход)
                if (hasCanvas && nameUI.Canvas != null)
                {
                    // ПРИНУДИТЕЛЬНО включить Canvas - не отключать
                    if (!nameUI.Canvas.enabled)
                    {
                        nameUI.Canvas.enabled = true;
                    }
                }
                else
                {
                    if (nameUI.Container.activeSelf != shouldBeVisible)
                    {
                        nameUI.Container.SetActive(shouldBeVisible);
                    }
                }
                
                // Применить базовую прозрачность никнейма (без fade эффектов)
                if (shouldBeVisible)
                {
                    Color targetColor = nameUI.NameColor;
                    targetColor.a = Config.NicknameOpacity;
                    
                    // Применить цвет к правильному компоненту
                    if (hasTextMesh)
                    {
                        textMesh.color = targetColor;
                    }
                    else if (hasCanvas && nameUI.TextComponent != null)
                    {
                        nameUI.TextComponent.color = targetColor;
                    }
                }
            }
        }
        
        /// <summary>
        /// Удалить никнейм игрока
        /// </summary>
        public void RemovePlayerNickname(string playerId)
        {
            if (!_playerNicknames.ContainsKey(playerId)) return;
            
            var nameUI = _playerNicknames[playerId];
            if (nameUI.Container != null)
            {
                Destroy(nameUI.Container);
            }
            
            _playerNicknames.Remove(playerId);
            
            // Очистить данные позиции никнейма
            if (_nicknamePositions.ContainsKey(playerId))
                _nicknamePositions.Remove(playerId);
            if (_lastKnownPositions.ContainsKey(playerId))
                _lastKnownPositions.Remove(playerId);
        }
        
        /// <summary>
        /// Очистить только никнеймы удаленных игроков (не локального)
        /// </summary>
        public void ClearRemotePlayerNicknames()
        {
            var toRemove = new List<string>();
            
            foreach (var kvp in _playerNicknames)
            {
                if (!kvp.Value.IsLocal)
                {
                    if (kvp.Value.Container != null)
                    {
                        Destroy(kvp.Value.Container);
                    }
                    toRemove.Add(kvp.Key);
                }
            }
            
            foreach (var key in toRemove)
            {
                _playerNicknames.Remove(key);
                
                // Очистить данные позиции никнейма для удаленных игроков
                if (_nicknamePositions.ContainsKey(key))
                    _nicknamePositions.Remove(key);
                if (_lastKnownPositions.ContainsKey(key))
                    _lastKnownPositions.Remove(key);
            }
        }
        
        /// <summary>
        /// Очистить все никнеймы
        /// </summary>
        public void ClearAllPlayerNicknames()
        {
            ClearRemotePlayerNicknames();
            
            // Очистить локальный никнейм тоже
            if (_localPlayerNickname != null && _localPlayerNickname.Container != null)
            {
                Destroy(_localPlayerNickname.Container);
            }
            
            _playerNicknames.Clear();
            _localPlayerNickname = null;
        }
        
        /// <summary>
        /// Обработка отключения игрока - удаление всех связанных данных
        /// </summary>
        public void HandlePlayerDisconnected(string playerId)
        {            
            // Удалить никнейм
            RemovePlayerNickname(playerId);
            
            // Удалить объект игрока если он существует
            if (_playerObjects.ContainsKey(playerId) && _playerObjects[playerId] != null)
            {
                Destroy(_playerObjects[playerId]);
                _playerObjects.Remove(playerId);
            }
            
            // Очистить все связанные данные
            if (_playerSprites.ContainsKey(playerId))
                _playerSprites.Remove(playerId);
            if (_playerInterpolators.ContainsKey(playerId))
                _playerInterpolators.Remove(playerId);
            if (_playerNames.ContainsKey(playerId))
                _playerNames.Remove(playerId);
            if (_playerPreviousPositions.ContainsKey(playerId))
                _playerPreviousPositions.Remove(playerId);
            if (_playerPreviousSpriteIds.ContainsKey(playerId))
                _playerPreviousSpriteIds.Remove(playerId);
            if (_playerLastUpdateTime.ContainsKey(playerId))
                _playerLastUpdateTime.Remove(playerId);
            if (_activePlayersLastSeen.ContainsKey(playerId))
                _activePlayersLastSeen.Remove(playerId);
            if (_lastKnownPositions.ContainsKey(playerId))
                _lastKnownPositions.Remove(playerId);
            if (_nicknamePositions.ContainsKey(playerId))
                _nicknamePositions.Remove(playerId);
                
            // Удалить компас если он есть
            if (_playerCompasses.ContainsKey(playerId) && _playerCompasses[playerId] != null)
            {
                Destroy(_playerCompasses[playerId]);
                _playerCompasses.Remove(playerId);
            }
            if (_playerCompassSprites.ContainsKey(playerId))
                _playerCompassSprites.Remove(playerId);
        }
        
        /// <summary>
        /// Проверка тайм-аута игроков и автоматическая очистка неактивных
        /// </summary>
        private void CheckPlayerTimeouts()
        {
            var playersToRemove = new List<string>();
            float currentTime = Time.time;
            
            foreach (var kvp in _activePlayersLastSeen)
            {
                string playerId = kvp.Key;
                float lastSeen = kvp.Value;
                
                // Если игрок не обновлялся больше установленного времени
                if (currentTime - lastSeen > PLAYER_TIMEOUT_SECONDS)
                {
                    playersToRemove.Add(playerId);
                }
            }
            
            // Удалить неактивных игроков
            foreach (string playerId in playersToRemove)
            {
                HandlePlayerDisconnected(playerId);
            }
        }
        
        #endregion
        
    }
    
    /// <summary>
    /// Структура данных для UI никнеймов игроков
    /// </summary>
    internal class PlayerNameUI
    {
        public string PlayerId;
        public string DisplayName;
        public Color NameColor;
        public GameObject Container;
        public Text TextComponent;
        public Canvas Canvas;
        public bool IsLocal;
        public float LastUpdateTime;
    }
    
}
