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
using UnityEngine.UI;
using static InventoryItemManager;
using static InventoryItemToolManager;
using static SteelSoulQuestSpot;


namespace ExtraToolColors
{
    [BepInPlugin("com.archdodo.ExtraToolColors", "Extra Tool Colors", "0.0.1")]
    public class ExtraToolColors : BaseUnityPlugin
    {
        static readonly ToolItemType Green = (ToolItemType)4, Purple = (ToolItemType)5, Orange = (ToolItemType)6, Pink = (ToolItemType)7;

        private static readonly Color[] toolTypeColors = { new Color(0.4f, 1.0f, 0.4f, 1.0f), new Color(0.8f, 0.4f, 1.0f, 1.0f), new Color(1.0f, 0.5f, 0.25f, 1.0f), new Color(0.95f, 0.54f, 0.68f, 1.0f) };

        // Defines edges on a graph of (int)ToolItemType nodes 
        // See the method ToolCompatability for an explanation of why this is the way it is
        public static List<int>[] ToolCompatabilityGragh { get; private set; } = new List<int>[] {
            new List<int> { 5, 6, 7 },
            new List<int> { 4, 5 },
            new List<int> { 4, 6 },
            new List<int> { 7 },
            new List<int> { 1, 2, 5, 6 },
            new List<int> { 0, 1, 4 },
            new List<int> { 0, 2, 4 },
            new List<int> { 0, 3 }
        };
        public static List<int> AdditionalAttackTypes { get; private set; } = new List<int> { 5, 6, 7 };

        public static List<int> AttackOnlyTypes { get; private set; } = new List<int> { 0, 3, 7 };

        private static AssetBundle lshBundle;

        internal static ManualLogSource Log;

        private static GameObject[] ExtraHeaders;

        readonly static Harmony harmony = new Harmony("com.archdodo.ExtraToolColors");
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
        public static List<InventoryItemTool> GetListItemsExtraColorsPatch(InventoryToolCrestSlot slot, InventoryItemGrid toolList)
        {
            return toolList.GetListItems((InventoryItemTool toolItem) => ToolCompatability(toolItem.ToolType, slot.Type));
        }
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
        public static ToolItemType GetNewToolItemType(ToolItem tool)
        {
            if (tool is ToolItemSkill) return ToolItemType.Skill;
            if (tool.Type == Pink) return ToolItemType.Red;
            return OriginalTypes.ContainsKey(tool.name) ? OriginalTypes[tool.name] : tool.Type;
        }

        readonly static Expression<Func<ToolItemType, ToolItemType, bool>> m_ToolCompatability = (type1, type2) => ToolCompatability(type1, type2);
        readonly static Expression<Func<InventoryToolCrestSlot, InventoryItemGrid, List<InventoryItemTool>>> m_GetListItemsPatch = (slot, toolList) => GetListItemsExtraColorsPatch(slot, toolList);
        readonly static Expression<Func<ToolItem, ToolItemType>> m_GetNewToolItemType = (tool) => GetNewToolItemType(tool);
        private void Awake()
        {
            HarmonyFileLog.Enabled = true;
            harmony.PatchAll(typeof(ExtraToolColors));
            Logger.LogInfo("Extra Tool Colors loaded and initialized");
            Log = Logger;
            LoadHeadersFromAssetBundle();
        }

        public static Dictionary<string, int> ChangedTools = new Dictionary<string, int>() { { "Barbed Wire", 6 }, { "Sprintmaster", 4 }, { "WebShot Forge", 7 }, { "WebShot Weaver", 7 }, { "Webshot Architect", 7 }, { "Thief Claw", 5 }, { "Silk Bomb", 7 } };
        public static Dictionary<string, ToolItemType> OriginalTypes = new Dictionary<string, ToolItemType> { };
        private static void LoadHeadersFromAssetBundle()
        {
            if ((UnityEngine.Object)(object)lshBundle == null)
            {
                string text = Path.Combine(Path.Combine(Paths.PluginPath, "Extra Tool Colors"), "list_section_header_sprites");
                Log.LogInfo("Trying to load AssetBundle from: " + text);
                lshBundle = AssetBundle.LoadFromFile(text);
                if ((UnityEngine.Object)(object)lshBundle == null)
                {
                    Log.LogError("Could not find assetbundle at: " + text);
                    return;
                }
            }

            GameObject GreenPrefab = lshBundle.LoadAsset<GameObject>("GreenListHeaderPrefab"),
                PurplePrefab = lshBundle.LoadAsset<GameObject>("PurpleListHeaderPrefab"),
                OrangePrefab = lshBundle.LoadAsset<GameObject>("OrangeListHeaderPrefab"),
                PinkPrefab = lshBundle.LoadAsset<GameObject>("PinkListHeaderPrefab");
            DontDestroyOnLoad(GreenPrefab);
            DontDestroyOnLoad(PurplePrefab);
            DontDestroyOnLoad(OrangePrefab);
            DontDestroyOnLoad(PinkPrefab);
            if (GreenPrefab == null || PurplePrefab == null || OrangePrefab == null || PinkPrefab == null)
            {
                Log.LogError("Could not load all sprites");
                return;
            }
            ExtraHeaders = new GameObject[] { GreenPrefab, PurplePrefab, OrangePrefab, PinkPrefab };
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
                int newType = ChangedTools[newItemData.name];
                if (AttackOnlyTypes.Contains((int)newItemData.Type) && !AttackOnlyTypes.Contains(newType)) { return; }

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
                Log.LogInfo("Loaded " + newItemData.name + " as type " + newItemData.Type);
            }
        }
        [HarmonyPrefix]
        [HarmonyPatch(typeof(InventoryItemToolManager), "GetGridSections")]
        public static void GetGridSectionsPrefix(InventoryItemToolManager __instance, NestedFadeGroupSpriteRenderer[] ___listSectionHeaders)
        {
            if (___listSectionHeaders.Length < 5)
            {
                NestedFadeGroup Parent = ___listSectionHeaders[0].ParentGroup;
                NestedFadeGroupSpriteRenderer GreenSection = Instantiate(___listSectionHeaders[3], ___listSectionHeaders[0].transform.parent),
                    OrangeSection = Instantiate(___listSectionHeaders[2], ___listSectionHeaders[0].transform.parent),
                    PurpleSection = Instantiate(___listSectionHeaders[3], ___listSectionHeaders[0].transform.parent),
                    PinkSection = Instantiate(___listSectionHeaders[3], ___listSectionHeaders[0].transform.parent);
                NestedFadeGroupSpriteRenderer[] newListSectionHeaders = { ___listSectionHeaders[0], ___listSectionHeaders[1], ___listSectionHeaders[2], ___listSectionHeaders[3], GreenSection, PurpleSection, OrangeSection, PinkSection };
                Log.LogInfo("Header info scale: " + ___listSectionHeaders[0].Sprite.spriteAtlasTextureScale);

                for (int i = 0; i < 4; i++)
                {
                    Traverse t = Traverse.Create(newListSectionHeaders[i + 4]);
                    SpriteRenderer newRenderer = Instantiate(ExtraHeaders[i].GetComponent<SpriteRenderer>(), newListSectionHeaders[i + 4].transform) as SpriteRenderer;
                    newListSectionHeaders[i + 4].transform.SetScaleMatching(1.6f);
                    newListSectionHeaders[i + 4].Sprite = newRenderer.sprite;

                    t.Field("spriteRenderer").SetValue(newRenderer);

                    newListSectionHeaders[i + 4].BaseColor = toolTypeColors[i];

                    newListSectionHeaders[i + 4].SetParent(Parent);
                }
                Traverse.Create(__instance).Field("listSectionHeaders").SetValue(newListSectionHeaders);
            }
        }
        [HarmonyPostfix]
        [HarmonyPatch(typeof(InventoryItemToolManager), "GetGridSections")]
        public static void GetGridSectionsPostfix(InventoryItemGrid ___toolList, NestedFadeGroupSpriteRenderer[] ___listSectionHeaders, List<InventoryItemTool> selectableItems, ref List<InventoryItemGrid.GridSection> __result)
        {
            List<InventoryItemGrid.GridSection> newList = new List<InventoryItemGrid.GridSection>(4);
            for (int i = 4; i < 8; i++)
            {
                newList.Add(new InventoryItemGrid.GridSection
                {
                    Header = ___listSectionHeaders[i].transform,
                    Items = selectableItems.Where((InventoryItemTool item) => item.ToolType == (ToolItemType)i).Cast<InventoryItemSelectableDirectional>().ToList()
                });
            }
            __result.AddRange(newList);
            ___toolList.Setup(__result);
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
                List<InventoryItemTool> list = listItems.Where((InventoryItemTool toolItem) => !IsToolEquipped(toolItem.ItemData)).ToList();
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
        public static IEnumerable<CodeInstruction> ToolItemLoadTypeTranspiler(IEnumerable<CodeInstruction> instructions)
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
            if (manager.EquipState == EquipStates.PlaceTool)
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
        public static IEnumerable<CodeInstruction> ToolGetTypeTranspiler(IEnumerable<CodeInstruction> instructions)
        {
            var c = CodeInstruction.Call("ToolItem:get_Type");
            var codeMatcher = new CodeMatcher(instructions);
            while (codeMatcher.MatchStartForward(new CodeMatch(c)).IsValid)
            {
                codeMatcher.RemoveInstruction()
                    .InsertAndAdvance(CodeInstruction.Call(m_GetNewToolItemType));
            }
            codeMatcher.Start();
            c.opcode = OpCodes.Callvirt;
            while (codeMatcher.MatchStartForward(new CodeMatch(c)).IsValid)
            {
                codeMatcher.RemoveInstruction()
                    .InsertAndAdvance(CodeInstruction.Call(m_GetNewToolItemType));
            }
            //codeMatcher.Instructions().Do(instr => Console.WriteLine("tpc  | " + instr.ToString()));
            return codeMatcher.InstructionEnumeration();
        }
    }
}