using Kitchen;
using Kitchen.Modules;
using Kitchen.ShopBuilder;
using Kitchen.ShopBuilder.Filters;
using KitchenData;
using KitchenLib.DevUI;
using KitchenLib.References;
using KitchenLib.Utils;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;

namespace KitchenSeedScanner
{
    public abstract class BaseMenu : BaseUI
    {
        public static readonly string PERSISTENT_DATA_PATH = Application.persistentDataPath;

        protected GUIStyle LabelLeftStyle { get; private set; }
        protected GUIStyle LabelCentreStyle { get; private set; }
        protected GUIStyle LabelMiddleCentreStyle { get; private set; }
        protected GUIStyle ButtonLeftStyle { get; private set; }
        protected Texture2D Background { get; private set; }
        public sealed override void OnInit()
        {
            Background = new Texture2D(64, 64);
            Color grayWithAlpha = new Color(0.2f, 0.2f, 0.2f, 0.6f);
            for (int x = 0; x < 64; x++)
            {
                for (int y = 0; y < 64; y++)
                {
                    Background.SetPixel(x, y, grayWithAlpha);
                }
            }
            Background.Apply();
            OnInitialise();
        }

        public sealed override void Setup()
        {
            if (LabelLeftStyle == null)
            {
                LabelLeftStyle = new GUIStyle(GUI.skin.label);
                LabelLeftStyle.alignment = TextAnchor.MiddleLeft;
                LabelLeftStyle.padding.left = 10;
                LabelLeftStyle.stretchWidth = true;
            }


            if (LabelCentreStyle == null)
            {
                LabelCentreStyle = new GUIStyle(GUI.skin.label);
                LabelCentreStyle.alignment = TextAnchor.MiddleCenter;
                LabelCentreStyle.stretchWidth = true;
            }

            if (LabelMiddleCentreStyle == null)
            {
                LabelMiddleCentreStyle = new GUIStyle(GUI.skin.label);
                LabelMiddleCentreStyle.alignment = TextAnchor.MiddleCenter;
                LabelMiddleCentreStyle.stretchWidth = true;
                LabelMiddleCentreStyle.stretchHeight = true;
            }

            if (ButtonLeftStyle == null)
            {
                ButtonLeftStyle = new GUIStyle(GUI.skin.button);
                ButtonLeftStyle.alignment = TextAnchor.MiddleLeft;
                ButtonLeftStyle.padding.left = 10;
                ButtonLeftStyle.stretchWidth = true;
            }
            OnSetup();
        }

        public static string WriteToTextFile(string folder, string filename, string data)
        {
            if (!Directory.Exists($"{PERSISTENT_DATA_PATH}{folder}"))
                Directory.CreateDirectory($"{PERSISTENT_DATA_PATH}/{folder}");

            string filepath = $"{PERSISTENT_DATA_PATH}/{folder}/{filename}";
            File.WriteAllText(filepath, data);

            return filepath;
        }

        public static string WriteTextureToPNG(string folder, string filename, Texture2D texture)
        {
            if (!Directory.Exists($"{PERSISTENT_DATA_PATH}{folder}"))
                Directory.CreateDirectory($"{PERSISTENT_DATA_PATH}/{folder}");

            byte[] bytes = texture.EncodeToPNG();
            string filepath = $"{PERSISTENT_DATA_PATH}/{folder}/{filename}";
            File.WriteAllBytes(filepath, bytes);

            return filepath;
        }

        protected virtual void OnInitialise()
        {
        }

        protected virtual void OnSetup()
        {

        }
    }

    public class SeedScanMenu : BaseMenu
    {
        private string _statusText = String.Empty;

        private string _seedFieldText = string.Empty;
        private Seed _activeSeed = default;

        private UnlockChoice _topLevelUnlockChoice;

        private List<RestaurantSetting> _restaurantSettings;
        private int _selectedRestaurantSettingIndex = 0;

        private List<Dish> _dishes;
        private int _dishesSelectedIndex = 0;
        private Dish _activeDish = null;

        private RestaurantSetting _activeRestaurantSetting = null;
        private UnlockPack _activeUnlockPack => _activeRestaurantSetting?.UnlockPack;

        private static UnlockCardElement _cardPrefabComp;

        private enum UnlockDisplayMode
        {
            SelectionPath,
            Hierarchy
        }
        private UnlockDisplayMode _activeUnlockDisplayMode = UnlockDisplayMode.SelectionPath;
        private Vector2 _unlockChoiceScrollPosition = Vector2.zero;

        #region Shop Filter Fields
        private static List<CShopBuilderOption> _shopBuilderOptions;
        private enum ShopFilterGDOType
        {
            None,
            OwnedAppliances,
            RequiredProcesses,
            ActiveThemes
        }

        private static readonly HashSet<int> CustomerMultiplierUnlockExceptions = new HashSet<int>()
        {
            UnlockCardReferences.MorningRush,
            UnlockCardReferences.LunchRush,
            UnlockCardReferences.DinnerRush,
            UnlockCardReferences.ClosingTime
        };

        private static Dictionary<Type, (ShopFilterGDOType, Func<SystemReference, CShopBuilderOption, List<int>, CShopBuilderOption>)> _replacementFilters = 
            new Dictionary<Type, (ShopFilterGDOType, Func<SystemReference, CShopBuilderOption, List<int>, CShopBuilderOption>)>()
        {
            { typeof(FilterByRequiredAppliances), (ShopFilterGDOType.OwnedAppliances, ReplacementFilterByRequiredAppliances) },
            { typeof(FilterByRequiredProcess), (ShopFilterGDOType.RequiredProcesses, ReplacementFilterByRequiredProcess) },
            { typeof(FilterByRequirements), (ShopFilterGDOType.OwnedAppliances, ReplacementFilterByRequirements) },
            { typeof(FilterByTheme), (ShopFilterGDOType.ActiveThemes, ReplacementFilterByTheme) },
            { typeof(TagStapleWhenMissing), (ShopFilterGDOType.OwnedAppliances, ReplacementTagStapleWhenMissing) },
            { typeof(TagStaples), (ShopFilterGDOType.None, ReplacementTagStaples) }
        };
        private static CShopBuilderOption ReplacementFilterByRequiredAppliances(SystemReference sysRef, CShopBuilderOption option, List<int> ownedAppliances)
        {
            if (!option.IsRemoved && option.Staple != ShopStapleType.BonusStaple && option.Staple != ShopStapleType.WhenMissing && GameData.Main.TryGet<Appliance>(option.Appliance, out var output))
            {
                option.IsRemoved |= !HasRequirements(output);
                option.FilteredBy = sysRef;
            }
            return option;

            bool HasRequirements(Appliance app)
            {
                if (app.SellOnlyAsDuplicate && !HasCopy(app))
                {
                    return false;
                }
                if (app.SellOnlyAsUnique && HasCopy(app))
                {
                    return false;
                }
                if (app.RequiresForShop.IsNullOrEmpty())
                {
                    return true;
                }
                foreach (Appliance item in app.RequiresForShop)
                {
                    if (item == null || !HasCopy(item))
                    {
                        continue;
                    }
                    return true;
                }
                return false;
            }

            bool HasCopy(Appliance app)
            {
                foreach (int allOwnedApplianceID in ownedAppliances)
                {
                    if (allOwnedApplianceID == app.ID)
                    {
                        return true;
                    }
                }
                return false;
            }
        }
        private static CShopBuilderOption ReplacementFilterByRequiredProcess(SystemReference sysRef, CShopBuilderOption option, List<int> requiredProcessIDs)
        {
            if (!option.IsRemoved && option.Staple != ShopStapleType.BonusStaple && option.Staple != ShopStapleType.WhenMissing && GameData.Main.TryGet<Appliance>(option.Appliance, out var output))
            {
                option.IsRemoved |= !IsRequired(output);
                option.FilteredBy = sysRef;
            }
            return option;

            bool IsRequired(Appliance app)
            {
                if (app.RequiresProcessForShop.IsNullOrEmpty())
                {
                    return true;
                }
                foreach (Process item in app.RequiresProcessForShop)
                {
                    if (item == null || !requiredProcessIDs.Contains(item.ID))
                    {
                        continue;
                    }
                    return true;
                }
                return false;
            }
        }
        private static CShopBuilderOption ReplacementFilterByRequirements(SystemReference sysRef, CShopBuilderOption option, List<int> ownedAppliances)
        {
            bool hasRefreshableProvider = HasRefreshableProvider();
            if (!option.IsRemoved && option.Staple != ShopStapleType.BonusStaple)
            {
                ShopRequirementFilter filter = option.Filter;
                ShopRequirementFilter shopRequirementFilter = filter;
                if (shopRequirementFilter != 0 && shopRequirementFilter == ShopRequirementFilter.RefreshableProvider)
                {
                    option.IsRemoved |= !hasRefreshableProvider;
                    option.FilteredBy = sysRef;
                }
            }
            return option;

            bool HasRefreshableProvider()
            {
                foreach (int applianceID in ownedAppliances)
                {
                    if (!GameData.Main.TryGet(applianceID, out Appliance appliance))
                        continue;
                    if (appliance.Properties.Where(x => x.GetType() == typeof(CItemProvider) && ((CItemProvider)x).AllowRefreshes).Count() > 0)
                        return true;
                }
                return false;
            }
        }
        private static CShopBuilderOption ReplacementFilterByTheme(SystemReference sysRef, CShopBuilderOption option, List<int> themeValues)
        {
            DecorationType activeThemes = ConvertToDecorationType();
            if (!option.IsRemoved && option.DecorationRequired != 0 && !activeThemes.Contains(option.DecorationRequired))
            {
                option.IsRemoved = true;
                option.FilteredBy = sysRef;
            }
            return option;

            DecorationType ConvertToDecorationType()
            {
                DecorationType decoType = DecorationType.Null;
                foreach (int themeVal in themeValues)
                {
                    decoType |= (DecorationType)themeVal;
                }
                return decoType;
            }
        }
        private static CShopBuilderOption ReplacementTagStapleWhenMissing(SystemReference sysRef, CShopBuilderOption option, List<int> ownedAppliances)
        {
            if (option.Staple == ShopStapleType.NonStaple && option.StapleWhenMissing && !HasAlready(option.Appliance))
            {
                option.Staple = ShopStapleType.WhenMissing;
            }
            return option;

            bool HasAlready(int app)
            {
                foreach (CAppliance allOwnedAppliance in ownedAppliances)
                {
                    if (app == allOwnedAppliance.ID)
                    {
                        return true;
                    }
                }
                return false;
            }
        }
        private static CShopBuilderOption ReplacementTagStaples(SystemReference sysRef, CShopBuilderOption option, List<int> ownedAppliances)
        {
            if (option.Staple == 0)
            {
                //foreach (CShopStaple staple in Staples)
                //{
                //    if (option.Appliance == staple.Appliance)
                //    {
                //        option.Staple = ShopStapleType.BonusStaple;
                //        return;
                //    }
                //}
                if ((option.Tags & ShoppingTags.Basic) != 0)
                {
                    option.Staple = ShopStapleType.FixedStaple;
                }
                else
                {
                    option.Staple = ShopStapleType.NonStaple;
                }
            }
            return option;
        }
        #endregion

        private static int UNLOCKS_CATEGORY_SEED = 848292;
        private static int SHOP_CATEGORY_SEED = 823828;
        private const string SEED_ALLOWED_CHARS = "abcdefghijklmnopqrstuvwxyz123456789";
        private const string DAYS_ALLOWED_CHARS = "123456789";

        public SeedScanMenu()
        {
            ButtonName = "SeedScan";
        }

        private class ShopSimulation
        {
            public class ShopRequest
            {
                public bool IsDecorShop;
                public ShoppingTags Tags;

                public int ApplianceID;
                public int DecorID;

                public ShopRequest()
                {
                }

                public ShopRequest(ShoppingTags tags)
                {
                    Tags = tags;
                    IsDecorShop = false;
                }

                public static ShopRequest CreateDecorShop()
                {
                    return new ShopRequest()
                    {
                        IsDecorShop = true
                    };
                }
            }

            public List<ShopRequest> ShopRequests;

            public static ShopSimulation Create(int day)
            {
                ShopSimulation simulation = new ShopSimulation();
                simulation.ShopRequests = new List<ShopRequest>();
                if (day > 0 && day % 5 == 0)
                {
                    for (int i = 0; i < 8; i++)
                    {
                        simulation.AddShop(ShoppingTags.Decoration);
                    }
                    for (int j = 0; j < 2; j++)
                    {
                        simulation.AddShop(ShoppingTags.SpecialEvent);
                    }
                    for (int k = 0; k < 6; k++)
                    {
                        simulation.AddDecorShop();
                    }
                    return simulation;
                }
                ShoppingTags defaultShoppingTag = ShoppingTagsExtensions.DefaultShoppingTag;
                int totalBlueprints = Mathf.Max(1, DifficultyHelpers.TotalShopCount(day));
                int stapleCount = Mathf.Max(0, Mathf.Min(DifficultyHelpers.StapleCount(day), totalBlueprints));
                int nonStapleCount = Mathf.Max(0, totalBlueprints - stapleCount);
                for (int l = 0; l < stapleCount; l++)
                {
                    simulation.AddShop(ShoppingTags.Basic);
                }
                for (int m = 0; m < nonStapleCount; m++)
                {
                    simulation.AddShop(defaultShoppingTag);
                }
                return simulation;
            }

            private void AddShop(ShoppingTags tags)
            {
                if (ShopRequests == null)
                {
                    ShopRequests = new List<ShopRequest>();
                }
                ShopRequests.Add(new ShopRequest(tags));
            }

            private void AddDecorShop()
            {
                if (ShopRequests == null)
                {
                    ShopRequests = new List<ShopRequest>();
                }
                ShopRequests.Add(ShopRequest.CreateDecorShop());
            }

            public void Run(FixedSeedContext fixedSeedContext, List<CShopBuilderOption> shopBuilderOptions)
            {

            }
        }

        private class ShopState
        {
            public int Day;
            public int RerollIndex;
            public bool IsLettersInside;

            public List<int> OwnedAppliances;
            public List<int> RequiredProcesses;
            public List<int> ActiveThemes;

            public ShopSimulation Simulation;

            public void Simulate(FixedSeedContext fixedSeedContext)
            {
                Simulation = ShopSimulation.Create(Day);
                List<CShopBuilderOption> shopBuilderOptions = !_shopBuilderOptions.IsNullOrEmpty() ? new List<CShopBuilderOption>(_shopBuilderOptions) : new List<CShopBuilderOption>();

                if (Main.ShopBuilderFilters.IsNullOrEmpty())
                {
                    Main.LogWarning("Main.ShopBuilderFilters is null or empty");
                }
                else
                {
                    foreach (ComponentSystemBase shopBuilderFilter in Main.ShopBuilderFilters)
                    {
                        Type filterType = shopBuilderFilter.GetType();
                        if (!_replacementFilters.TryGetValue(filterType, out var filterValues))
                        {
                            Main.LogError($"Could not find replacement filter function for {filterType}");
                            continue;
                        }

                        List<int> parameter;
                        switch (filterValues.Item1)
                        {
                            case ShopFilterGDOType.OwnedAppliances:
                                parameter = OwnedAppliances;
                                break;
                            case ShopFilterGDOType.RequiredProcesses:
                                parameter = RequiredProcesses;
                                break;
                            case ShopFilterGDOType.ActiveThemes:
                                parameter = ActiveThemes;
                                break;
                            default:
                                parameter = new List<int>();
                                break;
                        }
                        var filterFunc = filterValues.Item2;
                        for (int i = 0; i < shopBuilderOptions.Count; i++)
                        {
                            shopBuilderOptions[i] = filterFunc(shopBuilderFilter, shopBuilderOptions[i], parameter);
                        }
                    }
                }

                Simulation.Run(fixedSeedContext, shopBuilderOptions);
            }
        }

        private class UnlockChoice
        {
            private Seed Seed;
            private RestaurantSetting Setting;
            private UnlockPack UnlockPack;
            private int Tier;
            private int MaxDayInterval;
            private Dish StartingDish;

            public int ID;
            public string Name;

            public int Day;
            public int CustomersPerHourReductionExponent;   // Card Customer % Adjustment
            public float CustomersPerHourChange;    // ParameterEffect
            public Factor BaseCustomerMultiplier;    // CustomerSpawnEffect
            public Factor PerDayCustomerMultiplier;  // CustomerSpawnEffect
            public int Courses;
            public float CourseCustomerDivisor;

            public List<string> ExcludedCustomerCountUnlockNames;

            public bool IsInit = false;
            public bool IsExpanded = false;

            public HashSet<int> SelectedOptions = new HashSet<int>();
            public UnlockChoice Child1;
            public UnlockChoice Child2;
            public int SelectedChild = -1;

            private int _totalRerolls = 0;

            public int TotalRerolls
            {
                get
                {
                    return _totalRerolls;
                }
                set
                {
                    _totalRerolls = value;
                    // PopulateChildren(); Need to cache seed, unlockPack, tier and maxDayInterval
                }
            }
            Dictionary<ShopState, List<ShopSimulation>> Blueprints;

            public UnlockChoice(Seed seed, RestaurantSetting setting, UnlockPack unlockPack, Dish startingDish, int tier, int maxDayInterval = 10)
            {
                Seed = seed;
                Setting = setting;
                UnlockPack = unlockPack ?? GameData.Main.Get<UnlockPack>(AssetReference.DefaultUnlockPack);
                Tier = tier;
                MaxDayInterval = maxDayInterval;
                StartingDish = startingDish;
            }

            public FixedSeedContext GetFixedSeedContext(Seed seed, int category_seed, int instance)
            {
                return new FixedSeedContext(seed, category_seed * 1231231 + instance);
            }

            public bool HasChildren => !IsInit || Child1 != default || Child2 != default;

            public void PopulateChildren()
            {
                int customerMultiplierExponent = 0;
                float customersPerHourChange = 0f;
                Factor baseCustomerMultiplier = default;
                Factor perDayCustomerMultiplier = default;
                HashSet<DishType> presentDishTypes = new HashSet<DishType>();
                List<string> excludedCustomerCountUnlockNames = new List<string>();
                foreach (int unlockID in SelectedOptions)
                {
                    bool isExcluded = false;
                    string unlockName = unlockID.ToString();
                    if (CustomerMultiplierUnlockExceptions.Contains(unlockID))
                    {
                        isExcluded = true;
                    }

                    if (GameData.Main.TryGet(unlockID, out Unlock unlock))
                    {
                        unlockName = unlock.Name.IsNullOrEmpty()? unlockName : unlock.Name;
                        if (unlock.CustomerMultiplier != DishCustomerChange.FranchiseTier)
                        {
                            customerMultiplierExponent += unlock.CustomerMultiplier.Value();
                        }

                        if (unlock is UnlockCard unlockCard)
                        {
                            foreach (UnlockEffect unlockEffect in unlockCard.Effects)
                            {
                                if (!(unlockEffect is ParameterEffect parameterEffect))
                                {
                                    if (!(unlockEffect is CustomerSpawnEffect customerSpawnEffect))
                                    {
                                    }
                                    else
                                    {
                                        baseCustomerMultiplier += customerSpawnEffect.Base;
                                        if (Day >= 0)
                                        {
                                            perDayCustomerMultiplier += customerSpawnEffect.PerDay.Repeat(Day);
                                        }
                                    }
                                }
                                else
                                {
                                    customersPerHourChange += parameterEffect.Parameters.CustomersPerHour;
                                }
                            }
                        }

                        if (unlock is Dish dish)
                        {
                            switch (dish.Type)
                            {
                                case DishType.Starter:
                                    presentDishTypes.Add(DishType.Starter);
                                    break;
                                case DishType.Dessert:
                                    presentDishTypes.Add(DishType.Dessert);
                                    break;
                                case DishType.Main:
                                case DishType.Base:
                                    presentDishTypes.Add(DishType.Main);
                                    break;
                                default:
                                    break;
                            }
                        }
                    }

                    if (isExcluded)
                    {
                        excludedCustomerCountUnlockNames.Add(unlockName);
                    }
                }

                Courses = presentDishTypes.Count >= 1 ? presentDishTypes.Count : 1;
                CourseCustomerDivisor = 1 + (Courses - 1) * 0.25f;
                CustomersPerHourReductionExponent = customerMultiplierExponent;
                CustomersPerHourChange = customersPerHourChange;
                BaseCustomerMultiplier = baseCustomerMultiplier;
                PerDayCustomerMultiplier = perDayCustomerMultiplier;
                ExcludedCustomerCountUnlockNames = excludedCustomerCountUnlockNames;

                int nextDay;
                for (int i = 1; i <= MaxDayInterval; i++)
                {
                    nextDay = Day + i;
                    using FixedSeedContext fixedSeedContext = GetFixedSeedContext(Seed, UNLOCKS_CATEGORY_SEED, nextDay);
                    using (fixedSeedContext.UseSubcontext(1))
                    {
                        UnlockOptions options = UnlockPack.GetOptions(SelectedOptions, new UnlockRequest(nextDay, Tier));
                        if (options.Unlock1 == default && options.Unlock2 == default)
                        {
                            continue;
                        }

                        if (options.Unlock1 != default)
                        {
                            Child1 = CreateChildUnlockChoice(options.Unlock1);
                        }
                        if (options.Unlock2 != default)
                        {
                            Child2 = CreateChildUnlockChoice(options.Unlock2);
                        }

                        UnlockChoice CreateChildUnlockChoice(Unlock childUnlock)
                        {
                            HashSet<int> unlockSelectedOptions = new HashSet<int>(SelectedOptions);
                            unlockSelectedOptions.Add(childUnlock.ID);

                            return new UnlockChoice(Seed, Setting, UnlockPack, StartingDish, Tier, MaxDayInterval)
                            {
                                ID = childUnlock.ID,
                                Name = childUnlock.Name,
                                Day = nextDay,
                                SelectedOptions = unlockSelectedOptions
                            };
                        }
                    }
                    break;
                }

                //Blueprints = new Dictionary<ShopState, List<ShopSimulation>>();
                //for (int i = Day; i < nextDay; i++)
                //{
                //    foreach (bool lettersInside in new bool[] { true, false })
                //    {
                //        FixedSeedContext fixedSeedContext = GetFixedSeedContext(seed, SHOP_CATEGORY_SEED, Day + i);
                //        for (int rerollIndex = 0; rerollIndex < _totalRerolls + 1; rerollIndex++)
                //        {
                //            ShopState shopState = new ShopState()
                //            {
                //                Day = Day + i,
                //                IsLettersInside = lettersInside,
                //                RerollIndex = rerollIndex
                //            };
                //        }
                //    }
                //}
                IsInit = true;
            }

            public float GetCumulativeCustomerMultiplier()
            {
                return Mathf.Pow(1f - DifficultyHelpers.CustomerChangePerPoint / 100f, CustomersPerHourReductionExponent) * (1f + CustomersPerHourChange) * BaseCustomerMultiplier * PerDayCustomerMultiplier / CourseCustomerDivisor;
            }

            public void RecurseCollapse()
            {
                if (Child1 != default)
                {
                    Child1.RecurseCollapse();
                }
                if (Child2 != default)
                {
                    Child2.RecurseCollapse();
                }

                SelectedChild = -1;
                IsExpanded = false;
            }

            public string ExportUpToDay(int day)
            {
                List<string> lines = GetCardPermutationsCSVLines(day, isFirst: true);
                string data = String.Join("\n", lines);
                string filepath = WriteToTextFile("SeedScan", $"{Seed.StrValue}_{(Setting.Name.IsNullOrEmpty() ? Setting.ID : Setting.Name)}_{(StartingDish.Name.IsNullOrEmpty() ? StartingDish.ID : StartingDish.Name)}_{day}.csv", data);
                return $"Exported to {filepath}";
            }

            private List<string> GetCardPermutationsCSVLines(int dayLimit, List<int> headerDays = null, bool isFirst = false)
            {
                if (headerDays.IsNullOrEmpty())
                {
                    headerDays = new List<int>();
                    foreach (var _ in SelectedOptions)
                    {
                        headerDays.Add(Day);
                    }
                }
                else
                {
                    if (!headerDays.Contains(Day))
                    {
                        headerDays.Add(Day);
                    }
                }

                List<string> lines = new List<string>();
                if (!IsInit)
                {
                    PopulateChildren();
                }

                if (!HasChildren || (Child1 != null && Child1.Day > dayLimit) || (Child2 != null && Child2.Day > dayLimit))
                {
                    return new List<string>() { String.Join(",", SelectedOptions.Select(x => GameData.Main.TryGet(x, out Unlock unlock) ? unlock.Name : $"Unknown ({x})")) + 
                        $",{Courses},{$"{GetCumulativeCustomerMultiplier() * 100f:0.##}%"}" };
                }

                if (Child1 != null)
                {
                    lines.AddRange(Child1.GetCardPermutationsCSVLines(dayLimit, headerDays));
                }
                if (Child2 != null)
                {
                    lines.AddRange(Child2.GetCardPermutationsCSVLines(dayLimit, headerDays));
                }

                if (isFirst)
                {
                    List<string> headerBlock = new List<string>()
                    {
                        $"Seed,{Seed.StrValue}",
                        $"Setting,{(Setting.Name.IsNullOrEmpty() ? Setting.ID : Setting.Name)}",
                        $"Starting Dish,{(StartingDish.Name.IsNullOrEmpty() ? StartingDish.ID : StartingDish.Name)}",
                        "",
                        "No.," + String.Join(",", headerDays.Select<int, string>(x => x < 0 ? "Start" : x.ToString())) + ",Courses,Customer Multiplier"
                    };

                    for (int i = 0; i < lines.Count; i++)
                    {
                        lines[i] = $"{i + 1},{lines[i]}";
                    }

                    headerBlock.AddRange(lines);
                    lines = headerBlock;
                }
                return lines;
            }

            public string ExportCardsUpToDay(int day)
            {
                List<Unlock> unlocks = GetCardImages(day, isFirst: true);

                string folderPath = $"SeedScan/{Seed.StrValue}_{(Setting.Name.IsNullOrEmpty() ? Setting.ID : Setting.Name)}_{(StartingDish.Name.IsNullOrEmpty() ? StartingDish.ID : StartingDish.Name)}";

                int fade = Shader.PropertyToID("_NightFade");
                float nightFade = Shader.GetGlobalFloat(fade);
                Shader.SetGlobalFloat(fade, 0f);

                foreach (Unlock unlock in unlocks)
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
                    string filename = $"{(unlock.Name.IsNullOrEmpty() ? unlock.ID.ToString() : unlock.Name)}.png";
                    WriteTextureToPNG(folderPath, filename, texture);
                }

                Shader.SetGlobalFloat(fade, nightFade);
                return $"Exported to {folderPath}";
            }

            private List<Unlock> GetCardImages(int dayLimit, bool isFirst = false)
            {
                List<Unlock> unlocks = new List<Unlock>();
                if (!IsInit)
                {
                    PopulateChildren();
                }

                if (!HasChildren || (Child1 != null && Child1.Day > dayLimit) || (Child2 != null && Child2.Day > dayLimit))
                {
                    return SelectedOptions.Distinct().Select(x => GameData.Main.TryGet(x, out Unlock unlock) ? unlock : null).Where(x => x != null).ToList();
                }

                if (Child1 != null)
                {
                    MergeIntoUnlocks(Child1.GetCardImages(dayLimit));
                }
                if (Child2 != null)
                {
                    MergeIntoUnlocks(Child2.GetCardImages(dayLimit));
                }

                void MergeIntoUnlocks(List<Unlock> unlocksToMerge)
                {
                    foreach (Unlock unlock in unlocksToMerge)
                    {
                        if (!unlocks.Contains(unlock))
                            unlocks.Add(unlock);
                    }
                }
                return unlocks;
            }
        }

        protected override void OnInitialise()
        {
            _restaurantSettings = GameData.Main.Get<RestaurantSetting>().ToList();
            _dishes = GameData.Main.Get<Dish>().Where(x => x.Type == DishType.Base && x.IsUnlockable).ToList();

            _shopBuilderOptions = new List<CShopBuilderOption>();
            foreach (Appliance appliance in GameData.Main.Get<Appliance>())
            {
                if (appliance.IsPurchasable || appliance.IsPurchasableAsUpgrade)
                {
                    _shopBuilderOptions.Add(new CShopBuilderOption(appliance)
                    {
                        IsRemoved = false,
                        Staple = ShopStapleType.NonStaple,
                    });
                }
            }

            Main.LogWarning($"Unlock Card Option GOs = {UnityEngine.Object.FindObjectsOfType<GameObject>(true).Where(x => x.name == "Unlock Card Option").Count()}");
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
        }

        protected override void OnSetup()
        {
            GUILayout.BeginArea(new Rect(10f, 10f, 770f, 1050f));
            GUI.DrawTexture(new Rect(0f, 0f, 770f, 1050f), Background);

            GUILayout.BeginVertical();
            GUILayout.BeginHorizontal();

            string echoInput;
            GUILayout.BeginVertical(GUILayout.Width(0.2f * 770f));
            GUILayout.Label("Seed", LabelCentreStyle);
            _seedFieldText = SeedTextPostProcess(GUILayout.TextField(_seedFieldText).ToLowerInvariant(), out echoInput) ? echoInput : _seedFieldText;
            GUILayout.EndVertical();
            bool SeedTextPostProcess(string input, out string echoInput)
            {
                echoInput = input;
                if (input.Length > 8)
                {
                    return false;
                }
                foreach (char c in input)
                {
                    if (!SEED_ALLOWED_CHARS.Contains(c))
                        return false;
                }
                return true;
            }

            GUILayout.BeginVertical(GUILayout.Width(0.4f * 770f));
            GUILayout.Label("Restaurant Setting", LabelCentreStyle);
            if (GUILayout.Button(_restaurantSettings[_selectedRestaurantSettingIndex].Name))
            {
                _selectedRestaurantSettingIndex = (_selectedRestaurantSettingIndex + 1) % _restaurantSettings.Count;
            }
            GUILayout.EndVertical();

            GUILayout.BeginVertical(GUILayout.Width(0.39f * 770f));
            GUILayout.Label("Starting Dish", LabelCentreStyle);
            string dishName = _dishes[_dishesSelectedIndex].Name.IsNullOrEmpty() ? _dishes[_dishesSelectedIndex].ToString() : _dishes[_dishesSelectedIndex].Name;
            if (GUILayout.Button(dishName))
            {
                _dishesSelectedIndex = (_dishesSelectedIndex + 1) % _dishes.Count;
            }
            GUILayout.EndVertical();

            GUILayout.EndHorizontal();
            if (GUILayout.Button($"Mode = {_activeUnlockDisplayMode}"))
            {
                _unlockChoiceScrollPosition = Vector2.zero;
                _activeUnlockDisplayMode = GetNext();

                UnlockDisplayMode GetNext()
                {
                    List<UnlockDisplayMode> unlockDisplayModes = ((UnlockDisplayMode[])Enum.GetValues(typeof(UnlockDisplayMode))).ToList();
                    int currDiplayModeIndex = unlockDisplayModes.IndexOf(_activeUnlockDisplayMode);
                    currDiplayModeIndex = (++currDiplayModeIndex) % unlockDisplayModes.Count;
                    return unlockDisplayModes[currDiplayModeIndex];
                }
            }
            if (GUILayout.Button("Search") && !_seedFieldText.IsNullOrEmpty())
            {
                _activeSeed = new Seed(_seedFieldText);
                _activeRestaurantSetting = _restaurantSettings[_selectedRestaurantSettingIndex];
                _activeDish = _dishes[_dishesSelectedIndex];

                HashSet<int> startingUnlocks = new HashSet<int>() { _activeDish.ID };
                if (_activeRestaurantSetting.StartingUnlock != null)
                    startingUnlocks.Add(_activeRestaurantSetting.StartingUnlock.ID);
                _topLevelUnlockChoice = new UnlockChoice(_activeSeed, _activeRestaurantSetting, _activeUnlockPack, _activeDish, 0)
                {
                    Name = _activeSeed.StrValue,
                    IsExpanded = true,
                    Day = -1,
                    SelectedOptions = startingUnlocks
                };
                _topLevelUnlockChoice.PopulateChildren();
                _unlockChoiceScrollPosition = Vector2.zero;
            }

            if (_topLevelUnlockChoice != null && GUILayout.Button($"Export {_activeSeed.StrValue} Day 15 CSV"))
            {
                _statusText = _topLevelUnlockChoice.ExportUpToDay(15);
            }

            if (_topLevelUnlockChoice != null && _cardPrefabComp != null && GUILayout.Button($"Export {_activeSeed.StrValue} Day 15 Card Images"))
            {
                _statusText = _topLevelUnlockChoice.ExportCardsUpToDay(15);
            }

            if (!_statusText.IsNullOrEmpty())
            {
                GUILayout.Label(_statusText);
            }    

            GUILayout.EndVertical();

            if (_topLevelUnlockChoice != null)
            {
                switch (_activeUnlockDisplayMode)
                {
                    case UnlockDisplayMode.SelectionPath:
                        _topLevelUnlockChoice = DrawUnlockChoiceSelectionPath(_topLevelUnlockChoice, ref _unlockChoiceScrollPosition);
                        break;
                    case UnlockDisplayMode.Hierarchy:
                        _topLevelUnlockChoice = DrawUnlockChoiceHierarchy(_topLevelUnlockChoice, ref _unlockChoiceScrollPosition);
                        break;
                }
            }

            GUILayout.EndArea();

            if (!GUI.tooltip.IsNullOrEmpty())
            {
                float tooltipWidth = 300f;
                Vector2 tooltipPosition = Event.current.mousePosition;
                tooltipPosition.x -= tooltipWidth / 2;
                tooltipPosition.y += 20f;

                float tooltipHeight = GUI.skin.label.CalcHeight(new GUIContent(GUI.tooltip), tooltipWidth);
                Vector2 tooltipSize = new Vector2(tooltipWidth, tooltipHeight);
                Rect tooltipRect = new Rect(tooltipPosition, tooltipSize);

                GUI.DrawTexture(tooltipRect, Background);
                GUI.Label(tooltipRect, GUI.tooltip);
            }
        }

        private UnlockChoice DrawUnlockChoiceHierarchy(UnlockChoice choice, ref Vector2 scrollPosition, float? width = null)
        {
            if (width.HasValue)
            {
                float widthValue = width.Value;
                scrollPosition = GUILayout.BeginScrollView(scrollPosition, false, false, GUI.skin.horizontalScrollbar, GUI.skin.verticalScrollbar, GUILayout.Width(widthValue));
            }
            else
            {
                scrollPosition = GUILayout.BeginScrollView(scrollPosition, false, false, GUI.skin.horizontalScrollbar, GUI.skin.verticalScrollbar);
            }

            choice = DrawUnlockChoice(choice);
            GUILayout.EndScrollView();

            return choice;

            UnlockChoice DrawUnlockChoice(UnlockChoice choice, int indentLevel = 0, int unitIndent = 20)
            {
                // Change indent to move label start position to the right
                string label = "";
                label += choice.HasChildren ? (choice.IsExpanded ? "▼ " : "▶ ") : "    ";
                label += $"{choice.Name}";
                if (choice.Day > -1)
                {
                    if (choice.Day > 15)
                    {
                        label += $" (Overtime Day {choice.Day - 15})";
                    }
                    else
                    {
                        label += $" (Day {choice.Day})";
                    }
                }
                else
                {
                    List<string> unlockNames = new List<string>();
                    foreach (int unlockID in choice.SelectedOptions)
                    {
                        if (!GameData.Main.TryGet(unlockID, out Unlock unlock))
                        {
                            unlockNames.Add(unlockID.ToString());
                            continue;
                        }
                        unlockNames.Add(unlock.Name.IsNullOrEmpty() ? unlock.ToString() : unlock.Name);
                    }
                    if (unlockNames.Count > 0)
                    {
                        label += $" ({string.Join(", ", unlockNames)})";
                    }
                }

                GUILayout.BeginHorizontal();
                GUILayout.Space(unitIndent * indentLevel);
                if (GUILayout.Button(label, LabelLeftStyle, GUILayout.MinWidth(600)))
                {
                    choice.IsExpanded = !choice.IsExpanded;
                    if (choice.IsExpanded && !choice.IsInit)
                    {
                        choice.PopulateChildren();
                    }
                }
                GUILayout.EndHorizontal();
                if (choice.IsExpanded)
                {
                    int nextIndentLevel = indentLevel + 1;

                    GUILayout.BeginHorizontal();
                    GUILayout.Space(unitIndent * nextIndentLevel);
                    GUILayout.Label($"Courses = {choice.Courses}");
                    GUILayout.EndHorizontal();

                    GUILayout.BeginHorizontal();
                    GUILayout.Space(unitIndent * nextIndentLevel);
                    Color defaultContentColor = GUI.contentColor;
                    string tooltip = null;
                    if (choice.ExcludedCustomerCountUnlockNames.Count > 0)
                    {
                        GUI.contentColor = new Color(100f, 0f, 0f);
                        tooltip = "Inaccurate customer multiplier.\nSome card effects are excluded from calculation:\n"
                                + String.Join("\n", choice.ExcludedCustomerCountUnlockNames);
                    }
                    GUIContent customerMultiplierContent = new GUIContent($"Customer Multiplier = {choice.GetCumulativeCustomerMultiplier() * 100f:0.##}%", tooltip);
                    GUILayout.Label(customerMultiplierContent);
                    GUI.contentColor = defaultContentColor;

                    GUILayout.EndHorizontal();

                    if (choice.HasChildren)
                    {
                        if (choice.Child1 != null)
                        {
                            DrawUnlockChoice(choice.Child1, nextIndentLevel);
                        }
                        if (choice.Child2 != null)
                        {
                            DrawUnlockChoice(choice.Child2, nextIndentLevel);
                        }
                    }
                }
                return choice;
            }
        }

        private UnlockChoice DrawUnlockChoiceSelectionPath(UnlockChoice choice, ref Vector2 scrollPosition, float? width = null)
        {
            string label = $"{choice.Name}";
            GUIStyle seedTitleStyle = new GUIStyle(LabelCentreStyle);
            seedTitleStyle.fontStyle = FontStyle.Bold;
            GUILayout.Label(label, seedTitleStyle);


            if (width.HasValue)
            {
                float widthValue = width.Value;
                scrollPosition = GUILayout.BeginScrollView(scrollPosition, false, false, GUI.skin.horizontalScrollbar, GUI.skin.verticalScrollbar, GUILayout.Width(widthValue));
            }
            else
            {
                scrollPosition = GUILayout.BeginScrollView(scrollPosition, false, false, GUI.skin.horizontalScrollbar, GUI.skin.verticalScrollbar);
            }

            choice = DrawUnlockChoice(choice, width);
            GUILayout.EndScrollView();

            return choice;

            UnlockChoice DrawUnlockChoice(UnlockChoice choice, float? width = null)
            {
                float scrollWidth = 15f;
                float drawWidth = width.HasValue ? width.Value : 770f;
                drawWidth -= scrollWidth;

                GUILayout.BeginHorizontal();
                GUILayout.BeginVertical();

                string label;
                if (choice.Day > -1)
                {
                    label = $"{choice.Name}";
                    if (choice.Day > 15)
                    {
                        label += $" (Overtime Day {choice.Day - 15})";
                    }
                    else
                    {
                        label += $" (Day {choice.Day})";
                    }
                }
                else
                {
                    label = "Starting Unlocks";
                    List<string> unlockNames = new List<string>();
                    foreach (int unlockID in choice.SelectedOptions)
                    {
                        if (!GameData.Main.TryGet(unlockID, out Unlock unlock))
                        {
                            unlockNames.Add(unlockID.ToString());
                            continue;
                        }
                        unlockNames.Add(unlock.Name.IsNullOrEmpty() ? unlock.ToString() : unlock.Name);
                    }
                    if (unlockNames.Count > 0)
                    {
                        label += $" ({string.Join(", ", unlockNames)})";
                    }
                }
                GUILayout.Label(label, LabelCentreStyle);


                GUILayout.BeginHorizontal(GUILayout.Width(drawWidth));
                GUILayout.Label($"Courses = {choice.Courses}", LabelCentreStyle);


                Color defaultContentColor = GUI.contentColor;
                string tooltip = null;
                if (choice.ExcludedCustomerCountUnlockNames.Count > 0)
                {
                    GUI.contentColor = new Color(100f, 0f, 0f);
                    tooltip = "Inaccurate customer multiplier.\nSome card effects are excluded from calculation:\n"
                        + String.Join("\n", choice.ExcludedCustomerCountUnlockNames);
                }
                GUIContent customerMultiplierContent = new GUIContent($"Customer Multiplier = {choice.GetCumulativeCustomerMultiplier() * 100f:0.##}%", tooltip);
                GUILayout.Label(customerMultiplierContent, LabelCentreStyle);
                GUI.contentColor = defaultContentColor;

                //GUILayout.Label($"Customer Multiplier = {choice.GetCumulativeCustomerMultiplier() * 100f:0.##}%", LabelCentreStyle);
                GUILayout.EndHorizontal();

                if (!choice.HasChildren)
                {
                    GUILayout.Label("No more unlock options");
                }
                else
                {
                    GUILayout.BeginHorizontal();

                    Color defaultBackColor = GUI.backgroundColor;
                    GUI.backgroundColor = choice.SelectedChild == 1 ? new Color(0f, 100f, 0f) : defaultBackColor;
                    if (choice.Child1 != null && GUILayout.Button($"{choice.Child1.Name}", GUILayout.Width(drawWidth / 2f)))
                    {
                        if (choice.SelectedChild != 1)
                        {
                            choice.RecurseCollapse();
                            choice.SelectedChild = 1;
                        }
                    }
                    GUI.backgroundColor = choice.SelectedChild == 2 ? new Color(0f, 100f, 0f) : defaultBackColor;
                    if (choice.Child2 != null && GUILayout.Button($"{choice.Child2.Name}", GUILayout.Width(drawWidth / 2f)))
                    {
                        if (choice.SelectedChild != 2)
                        {
                            choice.RecurseCollapse();
                            choice.SelectedChild = 2;
                        }
                    }
                    GUI.backgroundColor = defaultBackColor;
                    GUILayout.EndHorizontal();
                }

                GUILayout.EndVertical();
                GUILayout.Space(15f);
                GUILayout.EndHorizontal();
                GUILayout.Label("");

                UnlockChoice selectedChild = null;

                switch (choice.SelectedChild)
                {
                    case 1:
                        selectedChild = choice.Child1;
                        break;
                    case 2:
                        selectedChild = choice.Child2;
                        break;
                    default:
                        break;
                }

                if (selectedChild != null)
                {
                    if (!selectedChild.IsInit)
                    {
                        selectedChild.PopulateChildren();
                    }
                    DrawUnlockChoice(selectedChild);
                }

                return choice;
            }
        }
    }
}
