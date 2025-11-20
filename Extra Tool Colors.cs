using BepInEx;
using BepInEx.Logging;
using GlobalSettings;
using HarmonyLib;
using HarmonyLib.Tools;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Reflection.Emit;
using TeamCherry.NestedFadeGroup;
using UnityEngine;
using UnityEngine.U2D;
using UnityEngine.UI;
using static InventoryItemManager;
using static InventoryItemToolManager;
using static SteelSoulQuestSpot;
using static System.Net.Mime.MediaTypeNames;
using static ToolCrestsData;


namespace ExtraToolColors
{
    public struct ChangedSlot
    {
        public ToolItemType OriginalType;
        public int Occurance;
        public ToolItemType NewType;
        public ChangedSlot(ToolItemType origninalType, int occurance, ToolItemType newType)
        {
            OriginalType = origninalType;
            Occurance = occurance;
            NewType = newType;
        }
    }
    public struct ChangedSlotList
    {
        public List<ChangedSlot> slots;

        public ToolItemType Match(ToolItemType originalType, int occurances)
        {
            IEnumerable<ChangedSlot> filtered = slots.Where(slot => slot.OriginalType == originalType && slot.Occurance == occurances);
            return filtered.Count() > 0 ? filtered.First().NewType : originalType;
        }
        public ToolItemType MatchAndPop(ToolItemType originalType, int occurances)
        {
            IEnumerable<ChangedSlot> filtered = slots.Where(slot => slot.OriginalType == originalType && slot.Occurance == occurances);
            if (filtered.Count() > 0)
            {
                ChangedSlot slot = filtered.First();
                slots.Remove(slot);
                return slot.NewType;
            }
            return originalType;
        }
        public ChangedSlotList(ToolItemType[] originalItemTypes, int[] occurances, ToolItemType[] newItemTypes)
        {
            slots = new List<ChangedSlot>();
            int max = Math.Min(Math.Min(originalItemTypes.Length, occurances.Length), newItemTypes.Length);
            for (int i = 0; i < max; i++)
            {
                slots.Add(new ChangedSlot(originalItemTypes[i], occurances[i], newItemTypes[i]));
            }
        }
        public void ReplaceOrAdd(ToolItemType originalType, int occurance, ToolItemType newType)
        {
            int idx = slots.FindIndex(slot => slot.OriginalType == originalType && slot.Occurance == occurance);
            if (idx >= 0) slots.RemoveAt(idx);
            slots.Add(new ChangedSlot(originalType, occurance, newType));
        }
    }
    public struct SlotIconChanger
    {
        private readonly InventoryToolCrestSlot slot;
        private NestedFadeGroupSpriteRenderer slotIcon;
        private readonly AttackToolBinding attackBinding;
        public ToolItemType Type => slot.Type;
        public bool SetSprite(Dictionary<AttackToolBinding, Sprite> sprite)
        {
            if (slot == null) return false;
            if (!slot.isActiveAndEnabled || slot.EquippedItem || slot.IsLocked) return true;
            if (!(bool)slotIcon) slotIcon = Traverse.Create(slot).Field("slotTypeIcon").GetValue<NestedFadeGroupSpriteRenderer>();
            slotIcon.Sprite = sprite[attackBinding];
            return true;
        }
        public SlotIconChanger(InventoryToolCrestSlot Slot, AttackToolBinding? AttackBinding)
        {
            slot = Slot;
            slotIcon = Traverse.Create(slot).Field("slotTypeIcon").GetValue<NestedFadeGroupSpriteRenderer>();
            attackBinding = AttackBinding == null ? AttackToolBinding.Neutral : (AttackToolBinding)AttackBinding;
        }
        public int InList(List<SlotIconChanger> list)
        {
            var s = slot;
            return list.FindIndex(changer => changer.slot == s);
        }
    }

    [BepInPlugin("com.archdodo.ExtraToolColors", "Extra Tool Colors", "0.0.1")]
    public class ExtraToolColors : BaseUnityPlugin
    {
        public static readonly ToolItemType Green = (ToolItemType)4, Purple = (ToolItemType)5, Orange = (ToolItemType)6, Pink = (ToolItemType)7;
        public static readonly ToolItemType[] extraTypes = { Green, Purple, Orange, Pink };
        public static readonly string[] toolItemTypeNames = {"Attack", "Defend", "Explore", "Skill", "Defend/Explore", "Attack/Defend", "Attack/Explore", "Attack/Skill" };

        private static readonly Color[] toolTypeColors = { new Color(0.3f, 1.0f, 0.3f, 1.0f), new Color(0.8f, 0.4f, 1.0f, 1.0f), new Color(1.0f, 0.5f, 0.1f, 1.0f), new Color(0.95f, 0.54f, 0.68f, 1.0f) };

        // Defines edges on a graph of (int)ToolItemType nodes 
        // See the method ToolCompatability for an explanation of why this is the way it is
        public static List<int>[] ToolCompatabilityGragh { get; private set; } = new List<int>[] {
            new List<int> { 5, 6, 7 },
            new List<int> { 4, 5 },
            new List<int> { 4, 6 },
            new List<int> { 7 },
            new List<int> { 1, 2, 5, 6 },
            new List<int> { 0, 1, 4, 6, 7 },
            new List<int> { 0, 2, 4, 5, 7 },
            new List<int> { 0, 3, 5, 6 }
        };
        public static List<int> AdditionalAttackTypes { get; private set; } = new List<int> { 5, 6, 7 };

        public static List<ToolItemType> AttackOnlyTypes { get; private set; } = new List<ToolItemType> { ToolItemType.Red, ToolItemType.Skill, Pink };

        private static AssetBundle spriteBundle;

        internal static ManualLogSource Log;

        private static Sprite[] ExtraHeadersSprites;

        readonly static Harmony harmony = new Harmony("com.archdodo.ExtraToolColors");

        public static Dictionary<string, ToolItemType> ChangedTools;
        public static Dictionary<string, ToolItemType> OriginalTypes { get; private set; } = new Dictionary<string, ToolItemType> { };

        public static List<SlotIconChanger> SlotIcons { get; private set; } = new List<SlotIconChanger>();

        

        public static bool ToolCompatability(ToolItemType type1, ToolItemType type2)
        {
            int t1 = (int)type1;
            int t2 = (int)type2;

            // We model tool compatability as a graph where tool types are nodes and compatible types have bidirectional edges connecting them. Moving along any connected edge once results in a compatible type.
            // Self loop is of course valid since each tool type is compatible with any slot of the same color
            if (t1 == t2) return true;

            // Get all possible tool types within a single step from the start and check if the second type is in the list
            return ToolCompatabilityGragh[t1].Contains(t2);
        }
        /*
        public static List<InventoryItemTool> GetListItemsExtraColorsPatch(InventoryToolCrestSlot slot, InventoryItemGrid toolList)
        {
            Log.LogInfo("Getting List Items: " + toolList.name);
            return toolList.GetListItems((InventoryItemTool toolItem) => ToolCompatability(toolItem.ToolType, slot.Type));
        }*/
        public static int ExtraColorsGetAvailableSlotCount(IEnumerable<InventoryToolCrestSlot> slots, ToolItemType toolType, bool checkEmpty)
        {
            return slots.Count((slot) => !slot.IsLocked && ToolCompatability(slot.Type, toolType) && (!checkEmpty || slot.EquippedItem == null));
        }
        public static IEnumerable<InventoryToolCrestSlot> ExtraColorsGetAvailableSlots(IEnumerable<InventoryToolCrestSlot> slots, ToolItemType toolType)
        {
            slots = slots.Where((slot) => !slot.IsLocked && ToolCompatability(slot.Type, toolType));
            return slots.Any((slot) => !slot.EquippedItem) ? slots.Where((slot) => !slot.EquippedItem) : slots;
        }
        public static InventoryToolCrestSlot ExtraColorsGetAvailableSlot(IEnumerable<InventoryToolCrestSlot> slots, ToolItemType toolType)
        {
            return ExtraColorsGetAvailableSlots(slots, toolType).First();
        }
        public static int GetAvailableSlotCount(IEnumerable<InventoryToolCrestSlot> slots, ToolItemType toolType, bool checkEmpty)
        {
            return slots.Count((slot) => !slot.IsLocked && slot.Type == toolType && (!checkEmpty || slot.EquippedItem == null));
        }
        public static ToolItemType GetOldToolItemType(ToolItem tool)
        {
            if (tool is ToolItemSkill) return ToolItemType.Skill;
            if (tool.Type == Pink) return ToolItemType.Red;
            return OriginalTypes.ContainsKey(tool.name) ? OriginalTypes[tool.name] : tool.Type;
        }

        public static Dictionary<string, ChangedSlotList> ChangedSlots = new Dictionary<string, ChangedSlotList>() {
            { "Hunter", new ChangedSlotList(new ToolItemType[]{ ToolItemType.Blue, ToolItemType.Yellow}, new int[]{ 2, 2 }, new ToolItemType[] { Green, Green }) },
            { "Reaper", new ChangedSlotList(new ToolItemType[] { ToolItemType.Blue, ToolItemType.Yellow, ToolItemType.Red, ToolItemType.Red, ToolItemType.Skill}, new int[] {2, 2, 1, 2, 1 }, new ToolItemType[] { Green, Green, Purple, Orange, Pink}) },
            { "Wanderer", new ChangedSlotList(new ToolItemType[] { }, new int[] { }, new ToolItemType[] {}) },
            { "Warrior", new ChangedSlotList(new ToolItemType[] { }, new int[] { }, new ToolItemType[] {}) },
            { "Witch", new ChangedSlotList(new ToolItemType[] { }, new int[] { }, new ToolItemType[] {}) },
            { "Toolmaster", new ChangedSlotList(new ToolItemType[]{ ToolItemType.Red, ToolItemType.Red}, new int[]{ 2, 3 }, new ToolItemType[] { Purple, Orange }) },
            { "Spell", new ChangedSlotList(new ToolItemType[] { }, new int[] { }, new ToolItemType[] {}) }
        };
        public static Dictionary<string, ToolItemType[]> OriginalSlots { get; private set; } = new Dictionary<string, ToolItemType[]>();

        public static bool ChangeHunterUpgradesWithBase = true;

        public static Dictionary<ToolItemType, Dictionary<AttackToolBinding, Sprite>> ExtraColorsSlotSprites { get; private set; }

        readonly static Expression<Func<ToolItemType, ToolItemType, bool>> m_ToolCompatability = (type1, type2) => ToolCompatability(type1, type2);
        //readonly static Expression<Func<InventoryToolCrestSlot, InventoryItemGrid, List<InventoryItemTool>>> m_GetListItemsPatch = (slot, toolList) => GetListItemsExtraColorsPatch(slot, toolList);
        readonly static Expression<Func<ToolItem, ToolItemType>> m_GetOldToolItemType = (tool) => GetOldToolItemType(tool);

        internal static ConfigManager configManager;
        private void Awake()
        {
            
            configManager = new ConfigManager(Config);
            configManager.Init();
            Logger.LogInfo("Blue and Yellow: " + ConfigManager.ConvertStringToToolType("Blue and Yellow"));
            ChangedTools = configManager.GetAllEntries();

            if (ChangeHunterUpgradesWithBase)
            {
                ChangedSlots.Add("Hunter_v2", ChangedSlots["Hunter"]);
                ChangedSlots.Add("Hunter_v3", ChangedSlots["Hunter"]);
            }

            Logger.LogInfo("Extra Tool Colors loaded and initialized");
            Log = Logger;
            LoadSpritesFromAssetBundle();
            HarmonyFileLog.Enabled = true;
            harmony.PatchAll(typeof(ExtraToolColors));
        }

        private void LateUpdate()
        {
            SlotIcons.Do(changer => changer.SetSprite(ExtraColorsSlotSprites[changer.Type]));
        }

        private static void LoadSpritesFromAssetBundle()
        {
            if ((UnityEngine.Object)(object)spriteBundle == null)
            {
                string text = Path.Combine(Path.Combine(Paths.PluginPath, "Extra Tool Colors"), "extra_tool_colors_sprites");
                //Log.LogInfo("Loading AssetBundle from: " + text);
                spriteBundle = AssetBundle.LoadFromFile(text);
                if ((UnityEngine.Object)(object)spriteBundle == null)
                {
                    Log.LogError("Could not find AssetBundle at: " + text);
                    return;
                }
                //Log.LogInfo("Loaded AssetBundle : " + text);
            }
            SpriteAtlas atlas = spriteBundle.LoadAsset<SpriteAtlas>("Extra Colors Sprites");
            Sprite LoadSprite(string name)
            {
                return atlas.GetSprite(name);
            }

            ExtraHeadersSprites = new Sprite[4]{ LoadSprite("GreenListHeader"), LoadSprite("PurpleListHeader"), LoadSprite("OrangeListHeader"), LoadSprite("PinkListHeader") };
            //Log.LogInfo("HeaderSprites: " + ExtraHeadersSprites[0].ToString() + ", " + ExtraHeadersSprites[1].ToString() + ", " + ExtraHeadersSprites[2].ToString() + ", " + ExtraHeadersSprites[3].ToString());

            ExtraColorsSlotSprites = new Dictionary<ToolItemType, Dictionary<AttackToolBinding, Sprite>>() { 
                {Green, new Dictionary<AttackToolBinding, Sprite>() { { AttackToolBinding.Neutral, LoadSprite("Green Slot") }, { AttackToolBinding.Up, LoadSprite("Green Slot") } , { AttackToolBinding.Down, LoadSprite("Green Slot") } } },
                {Purple, new Dictionary<AttackToolBinding, Sprite>() { { AttackToolBinding.Neutral, LoadSprite("Purple Slot") }, { AttackToolBinding.Up, LoadSprite("Purple Slot Up") }, { AttackToolBinding.Down, LoadSprite("Purple Slot Down") } } }, 
                {Orange, new Dictionary<AttackToolBinding, Sprite>() { { AttackToolBinding.Neutral, LoadSprite("Orange Slot") }, { AttackToolBinding.Up, LoadSprite("Orange Slot Up") }, { AttackToolBinding.Down, LoadSprite("Orange Slot Down") } } }, 
                {Pink, new Dictionary<AttackToolBinding, Sprite>() { { AttackToolBinding.Neutral, LoadSprite("Pink Slot") }, { AttackToolBinding.Up, LoadSprite("Pink Slot Up") }, { AttackToolBinding.Down, LoadSprite("Pink Slot Down") } } }
            };
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(UI), "GetToolTypeColor")]
        public static bool GetToolTypeColorPrefix(ToolItemType type, ref Color __result)
        {
            var t = (int)type;
            if (t > 3) { __result = toolTypeColors[t - 4]; return false; }
            return true;
        }
        [HarmonyPrefix]
        [HarmonyPatch(typeof(InventoryItemTool), "SetData")]
        public static void SetDataPrefix(ref ToolItem newItemData, InventoryItemTool __instance, RuntimeAnimatorController[] ___slotAnimatorControllers)
        {
            if (ChangedTools.ContainsKey(newItemData.name))
            {
                if (!OriginalTypes.ContainsKey(newItemData.name))
                {
                    OriginalTypes.Add(newItemData.name, newItemData.Type);
                }
                ToolItemType newType = ChangedTools[newItemData.name];
                if (AttackOnlyTypes.Contains(newItemData.Type) && !AttackOnlyTypes.Contains(newType)) { return; }

                if (___slotAnimatorControllers.Length < 5)
                {
                    RuntimeAnimatorController[] newSlotAnimatorControllers = new RuntimeAnimatorController[8];
                    ___slotAnimatorControllers.CopyTo(newSlotAnimatorControllers, 0);
                    for (int i = 0; i < 4; i++)
                    {
                        newSlotAnimatorControllers[i + 4] = Instantiate(newSlotAnimatorControllers[i]);
                    }
                    Traverse.Create(__instance).Field("slotAnimatorControllers").SetValue(newSlotAnimatorControllers);
                }
                Traverse.Create(newItemData).Field("type").SetValue((ToolItemType)newType);
            }
        }
        [HarmonyPrefix]
        [HarmonyPatch(typeof(InventoryItemToolManager), "GetGridSections")]
        public static void GetGridSectionsPrefix(ref NestedFadeGroupSpriteRenderer[] ___listSectionHeaders)
        {
            if (___listSectionHeaders.Length < 5)
            {
                NestedFadeGroupSpriteRenderer GreenSection = Instantiate(___listSectionHeaders[3], ___listSectionHeaders[0].transform.parent),
                    OrangeSection = Instantiate(___listSectionHeaders[2], ___listSectionHeaders[0].transform.parent),
                    PurpleSection = Instantiate(___listSectionHeaders[3], ___listSectionHeaders[0].transform.parent),
                    PinkSection = Instantiate(___listSectionHeaders[3], ___listSectionHeaders[0].transform.parent);
                NestedFadeGroupSpriteRenderer[] newListSectionHeaders = { null, null, null, null, GreenSection, PurpleSection, OrangeSection, PinkSection };
                ___listSectionHeaders.CopyTo(newListSectionHeaders, 0);
                //Log.LogInfo("Header info scale: " + ___listSectionHeaders[0].Sprite.spriteAtlasTextureScale);

                for (int i = 0; i < 4; i++)
                {
                    newListSectionHeaders[i + 4].transform.SetScaleMatching(1.5f);
                    newListSectionHeaders[i + 4].Sprite = ExtraHeadersSprites[i];
                    //Log.LogInfo("New List Section Header: " + newListSectionHeaders[i + 4].name);
                }
                ___listSectionHeaders = newListSectionHeaders;
            }
        }
        [HarmonyPostfix]
        [HarmonyPatch(typeof(InventoryItemToolManager), "GetGridSections")]
        public static void GetGridSectionsPostfix(InventoryItemGrid ___toolList, NestedFadeGroupSpriteRenderer[] ___listSectionHeaders, List<InventoryItemTool> selectableItems, ref List<InventoryItemGrid.GridSection> __result)
        {
            List<InventoryItemGrid.GridSection> newSections = new List<InventoryItemGrid.GridSection>();
            for (int i = 4; i < 8; i++)
            {

                newSections.Add(new InventoryItemGrid.GridSection
                {
                    Header = ___listSectionHeaders[i].transform,
                    Items = selectableItems.Where((item) => item.ToolType == (ToolItemType)i).Cast<InventoryItemSelectableDirectional>().ToList()
                });
            }
            ___toolList.Setup(newSections);
            __result.AddRange(newSections);
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(InventoryToolCrest), "Setup")]
        public static void CrestSetupPrefix(InventoryToolCrest __instance, ToolCrest newCrestData, ref InventoryToolCrestSlot[] ___templateSlots)
        {
            
            // Ensure TOOL_TYPES array contains new types
            var t = Traverse.CreateWithType("InventoryToolCrest").Field("TOOL_TYPES");
            ToolItemType[] T() { return t.GetValue() as ToolItemType[]; }
            if (T().Length < 5)
            {
                ToolItemType[] t2 = new ToolItemType[8];
                T().CopyTo(t2, 0);
                extraTypes.CopyTo(t2, 4);
                t.SetValue(t2);
            }

            // Ensure templateSlots array contains new template slots corresponding to the new types
            var t_TemplateSlots = Traverse.Create(__instance).Field("templateSlots");
            if ((t_TemplateSlots.GetValue() as InventoryToolCrestSlot[]).Length < 5)
            {
                InventoryToolCrestSlot[] tSlots = new InventoryToolCrestSlot[8];
                ___templateSlots.CopyTo(tSlots, 0);
                for (int i = 4; i < 8; i++)
                {
                    int j = i == 7 ? 3 : (i == 4 ? 1 : 0);
                    tSlots[i] = Instantiate(tSlots[j], tSlots[j].transform.parent);
                    tSlots[i].name = $"{toolItemTypeNames[i]} Slot";
                    ToolCrest.SlotInfo si = tSlots[i].SlotInfo;
                    si.Type = extraTypes[i - 4];
                    tSlots[i].SlotInfo = si;
                    Traverse.Create(tSlots[i]).Field("slotTypeSprite").SetValue(ExtraColorsSlotSprites[(ToolItemType)i][AttackToolBinding.Neutral]);
                }
                ___templateSlots = tSlots;
            }


            // Change original slots to be new ones
            int[] slotCounts = new int[8];
            if (ChangedSlots.ContainsKey(newCrestData.name))
            {
                ToolItemType[] slotTypes(ToolCrest.SlotInfo[] Slots)
                {
                    ToolItemType[] types = new ToolItemType[Slots.Length];
                    for (int i = 0; i < types.Length; i++)
                    {
                        types[i] = Slots[i].Type;
                    }
                    return types;
                }
                ToolItemType[] slots = slotTypes(newCrestData.Slots);
                if (!OriginalSlots.ContainsKey(newCrestData.name))
                    OriginalSlots.Add(newCrestData.name, slotTypes(newCrestData.Slots));
                else
                    slots = OriginalSlots[newCrestData.name];

                for (int i = 0; i < slots.Length; i++)
                {
                    slotCounts[(int)slots[i]]++;
                    newCrestData.Slots[i].Type = ChangedSlots[newCrestData.name].Match(slots[i], slotCounts[(int)slots[i]]);
                }
            }
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(InventoryToolCrest), "Setup")]
        public static void AddSlotsToChangerList(List<InventoryToolCrestSlot> ___activeSlots, List<ToolCrest.SlotInfo> ___activeSlotsData)
        {
            // Add new slots of new types to list that changes their sprites to the new ones in LateUpdate, after any aniators run
            for (int i = 0; i < ___activeSlots.Count; i++)
            {
                if ((int)___activeSlots[i].Type > 3)
                {
                    var iconChanger = new SlotIconChanger(___activeSlots[i], ___activeSlotsData[i].AttackBinding);
                    var index = iconChanger.InList(SlotIcons);
                    if (index >= 0) SlotIcons.RemoveAt(index);
                    SlotIcons.Add(iconChanger);
                }
            }
        }
        
        [HarmonyPrefix]
        [HarmonyPatch(typeof(ToolItemList), "SortByType")]
        public static bool SortByTypePrefix(ToolItemList __instance)
        {
            ToolItemType[] enumerable = { ToolItemType.Red, ToolItemType.Blue, ToolItemType.Yellow, ToolItemType.Skill, Green, Purple, Orange, Pink };
            Dictionary<ToolItemType, List<ToolItem>> dictionary = new Dictionary<ToolItemType, List<ToolItem>>(enumerable.Count());
            foreach (ToolItemType item in enumerable)
            {
                dictionary[item] = new List<ToolItem>();
            }
            List<ToolItem> list = Traverse.Create(__instance).Field("List").GetValue() as List<ToolItem>;
            foreach (ToolItem item2 in list)
            {
                if (!(item2 == null))
                {
                    dictionary[item2.Type].Add(item2);
                }
            }

            list.Clear();
            foreach (ToolItemType item3 in enumerable)
            {
                list.AddRange(dictionary[item3]);
            }
            return false;
        }
        [HarmonyPostfix]
        [HarmonyPatch(typeof(ToolItemTypeExtensions), "IsAttackType")]
        public static void IsAttackTypePostfix(ToolItemType type, ref bool __result)
        {
            __result = __result || AdditionalAttackTypes.Contains((int)type);
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(ToolItemManager), nameof(ToolItemManager.GetBoundAttackTool), typeof(AttackToolBinding), typeof(ToolEquippedReadSource))]
        public static void GetBoundAttackToolPostfix(ref ToolItem __result)
        {
            if (__result != null && !AttackOnlyTypes.Contains(__result.Type)) __result = null;
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(RadialHudIcon), "UpdateDisplay")]
        public static void SetToolHudIconColor(RadialHudIcon __instance, ref UnityEngine.UI.Image ___radialImage)
        {
            if (__instance is ToolHudIcon && (bool)(__instance as ToolHudIcon).CurrentTool)
            {
                var crestList = FindFirstObjectByType<InventoryToolCrestList>(FindObjectsInactive.Include);
                if ((bool)crestList)
                {
                    if (crestList.GetSlots().Any(slot => slot.EquippedItem == (__instance as ToolHudIcon).CurrentTool))
                    {
                        ___radialImage.color = UI.GetToolTypeColor(crestList.GetSlots().First(slot => slot.EquippedItem == (__instance as ToolHudIcon).CurrentTool).Type);
                    }
                }
            }
        }


        [HarmonyPrefix]
        [HarmonyPatch(typeof(InventoryItemToolManager), "TryPickupOrPlaceTool")]
        public static bool TryPickupOrPlaceToolPrefix(ToolItem tool, InventoryItemToolManager __instance, InventoryToolCrestList ___crestList, InventoryFloatingToolSlots ___extraSlots, ref InventoryItemSelectable ___selectedBeforePickup, ref bool __result)
        {
            Traverse t_PickedUpTool = Traverse.Create(__instance).Property("PickedUpTool");
            Traverse t_EquipState = Traverse.Create(__instance).Property("EquipState");

            t_PickedUpTool.SetValue(tool);
            if (!tool)
            {
                __result = false;
                return false;
            }
            IEnumerable<InventoryToolCrestSlot> enumerable = null, enumerable2 = null, enumerable3 = null;
            Action[] prioritySelections = {
                // Priority is determined by exact type match (high) vs compatible types (low), crest (high) vs vesticrest (low), then empty slots (high) vs not (low) 
                // Normally this would create 8 permutations, but if there are no empty slots, we don't care about the exact type match vs compatible type distinction anymore, so there are only 6
                // This exists solely to determine if the crest or vesticrest contains any available slots
                () => {enumerable2 = ___crestList.GetSlots(); if (GetAvailableSlotCount(enumerable2, tool.Type, true) > 0) enumerable = enumerable2; },
                () => {enumerable3 = ___extraSlots.GetSlots(); if (GetAvailableSlotCount(enumerable3, tool.Type, true) > 0) enumerable = enumerable3; },
                () => {if (ExtraColorsGetAvailableSlotCount(enumerable2, tool.Type, true) > 0) enumerable = enumerable2; },
                () => {if (ExtraColorsGetAvailableSlotCount(enumerable3, tool.Type, true) > 0) enumerable = enumerable3; },
                () => {if (ExtraColorsGetAvailableSlotCount(enumerable2, tool.Type, false) > 0) enumerable = enumerable2;
                        else if (ExtraColorsGetAvailableSlotCount(enumerable3, tool.Type, false) > 0) enumerable =  enumerable3; }
            };
            foreach (Action selection in prioritySelections)
            {
                selection.Invoke();
                if (enumerable != null) break;
            }
            if (enumerable != null)
            {
                InventoryToolCrestSlot availableSlot = __instance.GetAvailableSlot(enumerable, tool.Type);
                if ((bool)availableSlot)
                {
                    t_EquipState.SetValue(EquipStates.PlaceTool);
                    ___selectedBeforePickup = __instance.CurrentSelected;

                    if (availableSlot.Type.IsAttackType())
                    {
                        if (GetAvailableSlotCount(enumerable, availableSlot.Type, false) == 1)
                        {
                            // Slot is attack type and perfect tool type match for exactly one empty slot: immediate placement
                            __instance.PlaceTool(availableSlot, isManual: true);
                        }
                        else
                        {
                            // Slot is attack type and either more than one valid empty slot or all available slots are filled: allow slot choice
                            __instance.PlayMoveSound();
                            __instance.SetSelected(availableSlot, null);
                        }
                    }
                    else if (GetAvailableSlotCount(enumerable, availableSlot.Type, true) > 0)
                    {
                        // Slot is not attack type and perfect tool match for 1 or more empty slots: immediate placement
                        __instance.PlaceTool(availableSlot, isManual: true);
                    }
                    else
                    {
                        // Slot is not attack type and all available slots are filled
                        int availableSlotCount = GetAvailableSlotCount(enumerable2, availableSlot.Type, false);
                        int availableSlotCount2 = GetAvailableSlotCount(enumerable3, availableSlot.Type, false);
                        if (availableSlotCount + availableSlotCount2 == 1)
                        {
                            // There is only 1 filled slot available: immediate placement
                            __instance.PlaceTool(availableSlot, isManual: true);
                        }
                        else
                        {
                            // More than one filled slot available: allow slot choice
                            __instance.PlayMoveSound();
                            __instance.SetSelected(availableSlot, null);
                        }
                    }
                    __instance.RefreshTools();
                    __result = true;
                    return false;
                }
                else
                {
                    availableSlot = ExtraColorsGetAvailableSlot(enumerable, tool.Type);
                    if ((bool)availableSlot)
                    {
                        t_EquipState.SetValue(EquipStates.PlaceTool);
                        ___selectedBeforePickup = __instance.CurrentSelected;
                        IEnumerable<InventoryToolCrestSlot> availableSlots = ExtraColorsGetAvailableSlots(enumerable2.Concat(enumerable3).Where((slot) => !slot.EquippedItem), tool.Type);
                        if (availableSlots.Count() == 0) availableSlots = ExtraColorsGetAvailableSlots(enumerable, tool.Type);

                        if (availableSlots.Any((slot) => slot.Type.IsAttackType()))
                        {
                            if (ExtraColorsGetAvailableSlotCount(enumerable, tool.Type, false) == 1)
                            {
                                // Exactly one compatable typed slot is an attack type slot: immediate placement
                                __instance.PlaceTool(availableSlot, isManual: true);
                            }
                            else
                            {
                                // Multiple compatable slots available, at least one of which is an attack slot: allow slot choice
                                __instance.PlayMoveSound();
                                __instance.SetSelected(availableSlot, null);
                            }
                        }
                        else if (ExtraColorsGetAvailableSlotCount(enumerable, tool.Type, true) > 0)
                        {
                            // No slots are attack types and compatable tool match for 1 or more empty slots
                            if (availableSlots.All(slot => slot.Type == availableSlots.First().Type))
                                // All empty slots share the same type: immediate placement
                                __instance.PlaceTool(availableSlot, isManual: true);
                            else
                            {
                                // Multiple types exist amongst empoty slots: allow slot choice
                                __instance.PlayMoveSound();
                                __instance.SetSelected(availableSlot, null);
                            }
                        }
                        else
                        {
                            // Slot is not attack type and all available slots are filled
                            int availableSlotCount = ExtraColorsGetAvailableSlotCount(enumerable2, tool.Type, false);
                            int availableSlotCount2 = ExtraColorsGetAvailableSlotCount(enumerable3, tool.Type, false);
                            if (availableSlotCount + availableSlotCount2 == 1)
                            {
                                // There is only 1 filled slot available: immediate placement
                                __instance.PlaceTool(availableSlot, isManual: true);
                            }
                            else
                            {
                                // More than one filled slot available: allow slot choice
                                __instance.PlayMoveSound();
                                __instance.SetSelected(availableSlot, null);
                            }
                        }
                        __instance.RefreshTools();
                        __result = true;
                        return false;
                    }
                }
            }
            t_PickedUpTool.SetValue(null);
            __result = false;
            return false;
        }/**/
        [HarmonyPrefix]
        [HarmonyPatch(typeof(InventoryItemToolManager), "StartSelection")]
        public static bool StartSelectionPrefix(InventoryToolCrestSlot slot, InventoryItemToolManager __instance, InventoryItemGrid ___toolList)
        {
            if (!(___toolList == null))
            {
                List<InventoryItemTool> listItems = ___toolList.GetListItems((InventoryItemTool toolItem) => ToolCompatability(toolItem.ToolType, slot.Type));
                List<InventoryItemTool> list = listItems.Where((toolItem) => !IsToolEquipped(toolItem.ItemData)).ToList();
                InventoryItemTool inventoryItemTool = null;
                if (list.Count > 0)
                {
                    inventoryItemTool = list[0];
                }
                else if (listItems.Count > 0)
                {
                    inventoryItemTool = listItems[0];
                }

                if (!(inventoryItemTool == null))
                {
                    Traverse.Create(__instance).Property("SelectedSlot").SetValue(slot);
                    Traverse.Create(__instance).Property("EquipState").SetValue(EquipStates.SelectTool);
                    __instance.PlayMoveSound();
                    __instance.SetSelected(inventoryItemTool, null);
                    __instance.RefreshTools();
                }
            }
            return false;
        }
        [HarmonyPostfix]
        [HarmonyPatch(typeof(ToolItem), "HasLimitedUses")]
        public static void HasLimitedUsesPostfix(ToolItem __instance, ref bool __result)
        {
            // If a modder were to want to make a silk skill that is pink by default, they would need to make it with the ToolItemSkill class
            __result = __result && !(__instance is ToolItemSkill);
        }

        [HarmonyTranspiler]
        [HarmonyPatch(typeof(InventoryToolCrestSlot), "UpdateSlotDisplay")]
        [HarmonyPatch(typeof(InventoryItemTool), "GetNextSelectable")]
        [HarmonyPatch(typeof(InventoryItemToolManager), "PlaceTool")]
        [HarmonyPatch(typeof(InventoryItemToolManager), "RefreshTools", typeof(bool), typeof(bool))]
        [HarmonyPatch(typeof(InventoryItemToolManager), "EndSelection")]
        [HarmonyPatch(typeof(InventoryToolCrestSlot), "GetNextSelectable")]
        public static IEnumerable<CodeInstruction> SlotLoadTypeTranspiler(IEnumerable<CodeInstruction> instructions)
        {
            var c = CodeInstruction.Call("InventoryToolCrestSlot:get_Type");
            var cv = c.Clone();
            cv.opcode = OpCodes.Callvirt;
            var codeMatcher = new CodeMatcher(instructions);
            while (codeMatcher.MatchStartForward(new CodeMatch(c), new CodeMatch(OpCodes.Beq)).IsValid)
                codeMatcher.Advance(1)
                    .InsertAndAdvance(CodeInstruction.Call(m_ToolCompatability))
                    .SetOpcodeAndAdvance(OpCodes.Brtrue);
            codeMatcher.Start();
            while (codeMatcher.MatchStartForward(new CodeMatch(cv), new CodeMatch(OpCodes.Beq)).IsValid)
                codeMatcher.Advance(1)
                    .InsertAndAdvance(CodeInstruction.Call(m_ToolCompatability))
                    .SetOpcodeAndAdvance(OpCodes.Brtrue);
            codeMatcher.Start();
            while (codeMatcher.MatchStartForward(new CodeMatch(c), new CodeMatch(OpCodes.Bne_Un)).IsValid)
                codeMatcher.Advance(1)
                    .InsertAndAdvance(CodeInstruction.Call(m_ToolCompatability))
                    .SetOpcodeAndAdvance(OpCodes.Brfalse);
            codeMatcher.Start();
            while (codeMatcher.MatchStartForward(new CodeMatch(cv), new CodeMatch(OpCodes.Bne_Un)).IsValid)
                codeMatcher.Advance(1)
                    .InsertAndAdvance(CodeInstruction.Call(m_ToolCompatability))
                    .SetOpcodeAndAdvance(OpCodes.Brfalse);
            //codeMatcher.Instructions().Do(instr => Console.WriteLine("tpc  | " + instr.ToString()));
            return codeMatcher.InstructionEnumeration();
        }
        [HarmonyTranspiler]
        [HarmonyPatch(typeof(InventoryItemToolManager), "EndSelection")]
        public static IEnumerable<CodeInstruction> InventoryToolItemLoadTypeTranspiler(IEnumerable<CodeInstruction> instructions)
        {
            var c = CodeInstruction.Call("InventoryItemTool:get_ToolType");
            var cv = c.Clone();
            cv.opcode = OpCodes.Callvirt;
            var codeMatcher = new CodeMatcher(instructions);
            while (codeMatcher.MatchStartForward(new CodeMatch(c), new CodeMatch(OpCodes.Beq)).IsValid)
                codeMatcher.Advance(1)
                    .InsertAndAdvance(CodeInstruction.Call(m_ToolCompatability))
                    .SetOpcodeAndAdvance(OpCodes.Brtrue);
            codeMatcher.Start();
            while (codeMatcher.MatchStartForward(new CodeMatch(cv), new CodeMatch(OpCodes.Beq)).IsValid)
                codeMatcher.Advance(1)
                    .InsertAndAdvance(CodeInstruction.Call(m_ToolCompatability))
                    .SetOpcodeAndAdvance(OpCodes.Brtrue);
            codeMatcher.Start();
            while (codeMatcher.MatchStartForward(new CodeMatch(c), new CodeMatch(OpCodes.Bne_Un)).IsValid)
                codeMatcher.Advance(1)
                    .InsertAndAdvance(CodeInstruction.Call(m_ToolCompatability))
                    .SetOpcodeAndAdvance(OpCodes.Brfalse);
            codeMatcher.Start();
            while (codeMatcher.MatchStartForward(new CodeMatch(cv), new CodeMatch(OpCodes.Bne_Un)).IsValid)
                codeMatcher.Advance(1)
                    .InsertAndAdvance(CodeInstruction.Call(m_ToolCompatability))
                    .SetOpcodeAndAdvance(OpCodes.Brfalse);
            //codeMatcher.Instructions().Do(instr => Console.WriteLine("tpc  | " + instr.ToString()));
            return codeMatcher.InstructionEnumeration();
        }

        [HarmonyTranspiler]
        [HarmonyPatch(typeof(InventoryItemTool), "UpdateEquippedDisplay", typeof(bool))]
        public static IEnumerable<CodeInstruction> ToolItemLoadTypeTranspiler(IEnumerable<CodeInstruction> instructions)
        {
            var c = CodeInstruction.Call("ToolItem:get_Type");
            var cv = c.Clone();
            cv.opcode = OpCodes.Callvirt;
            var codeMatcher = new CodeMatcher(instructions);
            while (codeMatcher.MatchStartForward(new CodeMatch(c), new CodeMatch(OpCodes.Beq)).IsValid)
                codeMatcher.Advance(1)
                    .InsertAndAdvance(CodeInstruction.Call(m_ToolCompatability))
                    .SetOpcodeAndAdvance(OpCodes.Brtrue);
            codeMatcher.Start();
            while (codeMatcher.MatchStartForward(new CodeMatch(cv), new CodeMatch(OpCodes.Beq)).IsValid)
                codeMatcher.Advance(1)
                    .InsertAndAdvance(CodeInstruction.Call(m_ToolCompatability))
                    .SetOpcodeAndAdvance(OpCodes.Brtrue);
            codeMatcher.Start();
            while (codeMatcher.MatchStartForward(new CodeMatch(c), new CodeMatch(OpCodes.Bne_Un)).IsValid)
                codeMatcher.Advance(1)
                    .InsertAndAdvance(CodeInstruction.Call(m_ToolCompatability))
                    .SetOpcodeAndAdvance(OpCodes.Brfalse);
            codeMatcher.Start();
            while (codeMatcher.MatchStartForward(new CodeMatch(cv), new CodeMatch(OpCodes.Bne_Un)).IsValid)
                codeMatcher.Advance(1)
                    .InsertAndAdvance(CodeInstruction.Call(m_ToolCompatability))
                    .SetOpcodeAndAdvance(OpCodes.Brfalse);
            //codeMatcher.Instructions().Do(instr => Console.WriteLine("tpc  | " + instr.ToString()));
            return codeMatcher.InstructionEnumeration();
        }
        private static InventoryToolCrestSlot GetNextSlotOfCompatibleType(InventoryAutoNavGroup autoNavGroup, InventoryToolCrestSlot source, SelectionDirection direction, ToolItemType type)
        {
            return autoNavGroup.GetNextSelectable<InventoryToolCrestSlot>(source, direction, (slot) => ToolCompatability(slot.Type, type)) as InventoryToolCrestSlot;
        }


        [HarmonyPostfix]
        [HarmonyPatch(typeof(InventoryItemTool), "GetNextSelectable")]
        public static void ToolGetNextSelectablePostfix(SelectionDirection direction, ref InventoryItemSelectable __result, ref InventoryItemTool __instance)
        {
            if (__result is InventoryItemTool)
            {
                int d = (int)direction;
                var tool = __result as InventoryItemTool;
                var manager = Traverse.Create(__instance).Field("manager").GetValue() as InventoryItemToolManager;
                if (tool == __instance && manager.EquipState == EquipStates.SelectTool)
                {
                    if (d > 1) { return; }
                    var slot = manager.SelectedSlot;
                    do
                    {
                        if (tool.Selectables[d] == null) return;
                        tool = tool.Selectables[d] as InventoryItemTool;
                    } while (!ToolCompatability(tool.ToolType, slot.Type));
                    //Log.LogInfo("Found search selctable: " + tool.ToString());
                    __result = tool;
                }
            }
        }
        [HarmonyPostfix]
        [HarmonyPatch(typeof(InventoryToolCrestSlot), "GetNextSelectable")]
        public static void SlotGetNextSelectablePostfix(SelectionDirection direction, ref InventoryItemSelectable __result, InventoryToolCrestSlot __instance)
        {
            var manager = Traverse.Create(__instance).Field("manager").GetValue() as InventoryItemToolManager;
            if (manager.EquipState == EquipStates.PlaceTool && !(__instance.Selectables[(int)direction] == __result && __instance.FallbackSelectables[(int)direction].Selectables.Any((fallback) => fallback != null && fallback.gameObject.activeInHierarchy)))
            {
                var autoNav = Traverse.Create(__instance).Field("autoNavGroup").GetValue() as InventoryAutoNavGroup;
                var selected = Traverse.Create(manager).Field("selectedBeforePickup").GetValue() as InventoryItemTool;
                var nextSlot = GetNextSlotOfCompatibleType(autoNav, __instance, direction, selected.ToolType);
                if (nextSlot)
                {
                    __result = nextSlot;
                }
            }
        }

        [HarmonyTranspiler]
        [HarmonyPatch(typeof(InventoryItemToolManager), "ToolListHasType")]
        [HarmonyPatch(typeof(InventoryToolCrestSlot), "IsSlotInvalid")]
        public static IEnumerable<CodeInstruction> FirstArgumentToolItemTypeTranspiler(IEnumerable<CodeInstruction> instructions)
        {
            var codeMatcher = new CodeMatcher(instructions);
            while (codeMatcher.MatchStartForward(new CodeMatch(OpCodes.Ldarg_1), new CodeMatch(OpCodes.Beq)).IsValid)
            {
                codeMatcher.Advance(1)
                    .InsertAndAdvance(CodeInstruction.Call(m_ToolCompatability))
                    .SetOpcodeAndAdvance(OpCodes.Brtrue);
            }
            codeMatcher.Start();
            while (codeMatcher.MatchStartForward(new CodeMatch(OpCodes.Ldarg_1), new CodeMatch(OpCodes.Bne_Un)).IsValid)
            {
                codeMatcher.Advance(1)
                    .InsertAndAdvance(CodeInstruction.Call(m_ToolCompatability))
                    .SetOpcodeAndAdvance(OpCodes.Brfalse);
            }
            //codeMatcher.Instructions().Do(instr => Console.WriteLine("tpc  | " + instr.ToString()));
            return codeMatcher.InstructionEnumeration();
        }

        [HarmonyTranspiler]
        [HarmonyPatch(typeof(HeroController), "ThrowTool")]
        [HarmonyPatch(typeof(HeroController), "CanThrowTool", typeof(ToolItem), typeof(AttackToolBinding), typeof(bool))]
        [HarmonyPatch(typeof(ToolHudIcon), nameof(ToolHudIcon.GetIsEmpty))]
        [HarmonyPatch(typeof(ToolHudIcon), "GetAmounts")]
        [HarmonyPatch(typeof(ToolHudIcon), "OnSilkSpoolRefreshed")]
        public static IEnumerable<CodeInstruction> ToolGetTypeTranspiler(IEnumerable<CodeInstruction> instructions)
        {
            var c = CodeInstruction.Call("ToolItem:get_Type");
            var codeMatcher = new CodeMatcher(instructions);
            while (codeMatcher.MatchStartForward(new CodeMatch(c)).IsValid)
            {
                codeMatcher.RemoveInstruction()
                    .InsertAndAdvance(CodeInstruction.Call(m_GetOldToolItemType));
            }
            codeMatcher.Start();
            c.opcode = OpCodes.Callvirt;
            while (codeMatcher.MatchStartForward(new CodeMatch(c)).IsValid)
            {
                codeMatcher.RemoveInstruction()
                    .InsertAndAdvance(CodeInstruction.Call(m_GetOldToolItemType));
            }
            //codeMatcher.Instructions().Do(instr => Console.WriteLine("tpc  | " + instr.ToString()));
            return codeMatcher.InstructionEnumeration();
        }
    }
}