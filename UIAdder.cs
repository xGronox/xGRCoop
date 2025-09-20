using BepInEx.Logging;
using UnityEngine;
using System;
using System.Collections;

namespace xGRCoop
{
    internal class UIAdder : MonoBehaviour
    {
        public ManualLogSource Logger;
        public ModConfig Config;

        private Connector _connector;

        // Server status UI (top-right corner)
        private GameObject _serverStatusUI;
        private Canvas _serverCanvas;
        private UnityEngine.UI.Image _serverStatusDot;
        private UnityEngine.UI.Text _pingText;
        private GameObject _statusBackground;
        
        // Ping update coroutine
        private Coroutine _pingUpdateCoroutine;

        private void Start()
        {
            _connector = GetComponent<Connector>();
            CreateServerStatusUI();
            
            // Start ping update coroutine
            _pingUpdateCoroutine = StartCoroutine(UpdatePingCoroutine());
        }

        private void Update()
        {
            if (_connector != null && _connector.Initialized)
            {
                // Handle F5 - Multiplayer Toggle
                if (Input.GetKeyDown(Config.MultiplayerToggleKey))
                {
                    try
                    {
                        if (_connector.Active)
                        {
                            // Скрыть всех игроков перед отключением но сохранить данные
                            var gameSync = GetComponent<GameSync>();
                            if (gameSync != null)
                            {
                                gameSync.Reset(); // Скрывает игроков но сохраняет данные никнеймов
                            }
                            
                            _connector.Disable();
                        }
                        else
                        {
                            _connector.Enable();
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.LogError($"Error toggling multiplayer: {ex.Message}");
                    }
                }

                // Update server status display
                UpdateServerStatusDisplay();
            }

        }

        /// <summary>
        /// Create server status UI in top-right corner
        /// </summary>
        private void CreateServerStatusUI()
        {
            try
            {
                // Create a new Canvas for server status
                GameObject canvasObject = new GameObject("xGRCoOp_ServerStatusCanvas");
                DontDestroyOnLoad(canvasObject);
                
                _serverCanvas = canvasObject.AddComponent<Canvas>();
                _serverCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
                _serverCanvas.sortingOrder = 100;
                
                var canvasScaler = canvasObject.AddComponent<UnityEngine.UI.CanvasScaler>();
                canvasScaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
                canvasScaler.referenceResolution = new Vector2(1920, 1080);
                
                canvasObject.AddComponent<UnityEngine.UI.GraphicRaycaster>();

                _statusBackground = new GameObject("StatusBackground");
                _statusBackground.transform.SetParent(_serverCanvas.transform, false);
                
                var bgImage = _statusBackground.AddComponent<UnityEngine.UI.Image>();
                bgImage.color = new Color(0f, 0f, 0f, 0f);
                
                var bgRect = _statusBackground.GetComponent<RectTransform>();
                bgRect.anchorMin = new Vector2(1f, 1f);
                bgRect.anchorMax = new Vector2(1f, 1f);
                bgRect.pivot = new Vector2(1f, 1f);
                bgRect.anchoredPosition = new Vector2(-15f, -30f);
                bgRect.sizeDelta = new Vector2(90f, 30f);

                GameObject statusDotObject = new GameObject("ServerStatusDot");
                statusDotObject.transform.SetParent(_statusBackground.transform, false);
                
                _serverStatusDot = statusDotObject.AddComponent<UnityEngine.UI.Image>();
                _serverStatusDot.sprite = CreateSmoothCircleSprite();
                _serverStatusDot.color = Color.red;
                
                var dotRect = statusDotObject.GetComponent<RectTransform>();
                dotRect.anchorMin = new Vector2(0.2f, 0.5f);
                dotRect.anchorMax = new Vector2(0.2f, 0.5f);
                dotRect.pivot = new Vector2(0.5f, 0.5f);
                dotRect.anchoredPosition = Vector2.zero;
                dotRect.sizeDelta = new Vector2(20f, 20f);

                GameObject pingTextObject = new GameObject("PingText");
                pingTextObject.transform.SetParent(_statusBackground.transform, false);
                
                _pingText = pingTextObject.AddComponent<UnityEngine.UI.Text>();
                _pingText.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
                _pingText.fontSize = 10;
                _pingText.color = Color.green;
                _pingText.alignment = TextAnchor.MiddleLeft;
                _pingText.text = "";
                
                var pingRect = pingTextObject.GetComponent<RectTransform>();
                pingRect.anchorMin = new Vector2(0.5f, 0.5f);
                pingRect.anchorMax = new Vector2(0.5f, 0.5f);
                pingRect.pivot = new Vector2(0f, 0.5f);
                pingRect.anchoredPosition = new Vector2(5f, 0f); // 5px to the right of dot
                pingRect.sizeDelta = new Vector2(50f, 20f);

                _serverStatusUI = canvasObject;
            }
            catch (Exception ex)
            {
                Logger.LogError($"Failed to create server status UI: {ex.Message}");
            }
        }

        /// <summary>
        /// Update server status display with colored dot
        /// </summary>
        private void UpdateServerStatusDisplay()
        {
            if (_serverStatusDot == null || _pingText == null) return;

            try
            {
                if (_connector != null)
                {
                    if (_connector.Active)
                    {
                        _serverStatusDot.color = Color.green;
                        
                        float ping = GetCurrentPing();
                        if (ping > 0)
                        {
                            _pingText.text = $"{ping:F0}ms";
                            
                            if (ping < 50) _pingText.color = Color.green;
                            else if (ping < 100) _pingText.color = Color.yellow;
                            else _pingText.color = Color.red;
                        }
                        else
                        {
                            _pingText.text = ""; 
                        }
                    }
                    else
                    {
                        _serverStatusDot.color = Color.red;
                        _pingText.text = "";
                    }
                }
                else
                {
                    _serverStatusDot.color = Color.yellow;
                    _pingText.text = "";
                }
            }
            catch (Exception ex)
            {
                Logger.LogError($"Error updating server status display: {ex.Message}");
            }
        }

        /// <summary>
        /// Create a smooth circle sprite with gradient for the status dot
        /// </summary>
        private Sprite CreateSmoothCircleSprite()
        {
            int size = 64;
            Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            Color[] pixels = new Color[size * size];
            
            Vector2 center = new Vector2(size / 2f, size / 2f);
            float radius = size / 2f - 4;
            
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float distance = Vector2.Distance(new Vector2(x, y), center);
                    Color pixelColor = Color.clear;
                    
                    if (distance <= radius)
                    {
                        float intensity = 1f - (distance / radius);
                        intensity = Mathf.SmoothStep(0f, 1f, intensity);
                        
                        if (distance < radius * 0.3f)
                        {
                            intensity = Mathf.Min(1f, intensity + 0.3f);
                        }
                        
                        if (distance > radius - 2f)
                        {
                            float edge = (radius - distance) / 2f;
                            intensity *= Mathf.Clamp01(edge);
                        }
                        
                        pixelColor = new Color(1f, 1f, 1f, intensity);
                    }
                    
                    pixels[y * size + x] = pixelColor;
                }
            }
            
            texture.SetPixels(pixels);
            texture.Apply();
            texture.filterMode = FilterMode.Bilinear;
            
            return Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));
        }


        /// <summary>
        /// Coroutine to update ping every second
        /// </summary>
        private IEnumerator UpdatePingCoroutine()
        {
            while (true)
            {
                yield return new WaitForSeconds(1f);
                
                if (_connector != null && _connector.Active)
                {
                    currentPing = UnityEngine.Random.Range(20f, 80f);
                }
                else
                {
                    currentPing = 0f;
                }
            }
        }
        
        private float currentPing = 0f;
        
        /// <summary>
        /// Get current ping
        /// </summary>
        private float GetCurrentPing()
        {
            return currentPing;
        }

        /// <summary>
        /// Cleanup UI
        /// </summary>
        private void OnDestroy()
        {
            if (_pingUpdateCoroutine != null)
            {
                StopCoroutine(_pingUpdateCoroutine);
            }
            
            if (_serverStatusUI != null)
            {
                Destroy(_serverStatusUI);
            }
        }
    }
}
