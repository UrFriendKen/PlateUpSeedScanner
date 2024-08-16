using Kitchen;
using Kitchen.ShopBuilder;
using KitchenData;
using KitchenLib;
using KitchenLib.Utils;
using KitchenMods;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Unity.Entities;
using UnityEngine;

// Namespace should have "Kitchen" in the beginning
namespace KitchenSeedScanner
{
    public class Main : BaseMod, IModSystem
    {
        // GUID must be unique and is recommended to be in reverse domain name notation
        // Mod Name is displayed to the player and listed in the mods menu
        // Mod Version must follow semver notation e.g. "1.2.3"
        public const string MOD_GUID = "IcedMilo.PlateUp.SeedScanner";
        public const string MOD_NAME = "Seed Scanner";
        public const string MOD_VERSION = "0.1.6";
        public const string MOD_AUTHOR = "IcedMilo";
        public const string MOD_GAMEVERSION = ">=1.1.5";
        // Game version this mod is designed for in semver
        // e.g. ">=1.1.3" current and all future
        // e.g. ">=1.1.3 <=1.2.3" for all from/until

        public Main() : base(MOD_GUID, MOD_NAME, MOD_AUTHOR, MOD_VERSION, MOD_GAMEVERSION, Assembly.GetExecutingAssembly()) { }

        public static Main _instance;

        static bool _firstUpdate = true;

        internal static List<ComponentSystemBase> ShopBuilderFilters;

        protected override void OnInitialise()
        {
            _instance = this;
        }

        protected override void OnUpdate()
        {
            if (_firstUpdate)
            {
                ShopBuilderFilters = new List<ComponentSystemBase>();
                Main.LogInfo("Extracting ShopBuilderFilters...");
                try
                {
                    ShopOptionGroup shopOptionsGroup = World.GetExistingSystem<ShopOptionGroup>();
                    shopOptionsGroup.SortSystems();
                    foreach (ComponentSystemBase componentSystem in shopOptionsGroup.Systems)
                    {
                        if (typeof(ShopBuilderFilter).IsAssignableFrom(componentSystem.GetType()))
                        {
                            ShopBuilderFilters.Add(componentSystem);
                            Main.LogInfo($"Found {componentSystem.GetType()}");
                        }
                    }
                }
                catch
                {

                }
                Main.LogInfo($"ShopBuilderFilters Count = {ShopBuilderFilters.Count}");
                _firstUpdate = false;
            }
        }

        protected override void OnPostActivate(KitchenMods.Mod mod)
        {
            LogWarning($"{MOD_GUID} v{MOD_VERSION} in use!");
            RegisterMenu<SeedScanMenu>();
        }

        protected override void OnPreInject()
        {
            base.OnPreInject();

            UnlockCardElement _cardPrefabComp = null;

            GameObject gO = Resources.FindObjectsOfTypeAll(typeof(GameObject)).Cast<GameObject>().Where(x => x.name == "Unlock Card Option").FirstOrDefault();
            if (gO != null && gO.HasComponent<UnlockCardElement>())
            {
                GameObject prefab = GameObject.Instantiate(gO);
                prefab.name = "Card Snapshot Prefab";
                GameObject container = new GameObject("Prefab Hider");
                container.SetActive(false);
                prefab.transform.SetParent(container.transform);
                _cardPrefabComp = prefab.GetComponent<UnlockCardElement>();
            }

            if (_cardPrefabComp == null)
                return;

            string folderPath = $"CardImages";

            int fade = Shader.PropertyToID("_NightFade");
            float nightFade = Shader.GetGlobalFloat(fade);
            Shader.SetGlobalFloat(fade, 0f);

            foreach (Unlock unlock in GameData.Main.Get<Unlock>())
            {
                GameObject instance = GameObject.Instantiate(_cardPrefabComp.gameObject);
                instance.transform.localPosition = Vector3.zero;
                //instance.transform.localScale = Vector3.one;
                instance.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                UnlockCardElement element = instance.GetComponent<UnlockCardElement>();
                element.SetUnlock(unlock);
                SnapshotTexture snapshotTexture = Snapshot.RenderToTexture(1024, 1024, element.gameObject, 1f, 1f, -10f, 10f, Vector3.back * 0.742f);
                Texture2D texture = snapshotTexture.Snapshot;
                GameObject.Destroy(instance);
                string filename = $"{unlock.CardType}_{unlock.UnlockGroup}_{(unlock.Name.IsNullOrEmpty() ? unlock.ID.ToString() : unlock.Name)}.png";
                WriteTextureToPNG(folderPath, filename, texture);
            }
            Shader.SetGlobalFloat(fade, nightFade);
        }

        public static string WriteTextureToPNG(string folder, string filename, Texture2D texture)
        {
            filename = GetSafeFilename(filename);

            if (!Directory.Exists($"{Application.persistentDataPath}/{folder}"))
                Directory.CreateDirectory($"{Application.persistentDataPath}/{folder}");

            byte[] bytes = texture.EncodeToPNG();
            string filepath = $"{Application.persistentDataPath}/{folder}/{filename}";
            File.WriteAllBytes(filepath, bytes);

            return filepath;
        }
        public static string GetSafeFilename(string path)
        {
            return string.Join("_", path.Split(Path.GetInvalidFileNameChars()));
        }

        #region Logging
        public static void LogInfo(string _log) { Debug.Log($"[{MOD_NAME}] " + _log); }
        public static void LogWarning(string _log) { Debug.LogWarning($"[{MOD_NAME}] " + _log); }
        public static void LogError(string _log) { Debug.LogError($"[{MOD_NAME}] " + _log); }
        public static void LogInfo(object _log) { LogInfo(_log.ToString()); }
        public static void LogWarning(object _log) { LogWarning(_log.ToString()); }
        public static void LogError(object _log) { LogError(_log.ToString()); }
        #endregion
    }
}
