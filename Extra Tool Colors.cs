using BepInEx;
using BepInEx.Logging;
using GlobalSettings;
using HarmonyLib;
using HarmonyLib.Tools;
using HutongGames.PlayMaker.Actions;
using MonoMod.Utils;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Reflection.Emit;
using TeamCherry.NestedFadeGroup;
using UnityEngine;
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

        public static List<int> AttackOnlyTypes { get; private set; } = new List<int> { 0,3,7 };

        private static AssetBundle lshBundle;

        internal static ManualLogSource Log;

        private static GameObject[] ExtraHeaders;

        static readonly Harmony harmony = new Harmony("com.archdodo.ExtraToolColors");

        private void Awake()
        {
            HarmonyFileLog.Enabled = true;
            Harmony.CreateAndPatchAll(typeof(ExtraToolColors));
            Logger.LogInfo("Extra Tool Colors loaded and initialized");
            Log = Logger;
            LoadHeadersFromAssetBundle();
        }

        public static Dictionary<string, int> ChangedTools = new Dictionary<string, int>() { { "Barbed Wire", 6 }, { "Sprintmaster", 4 }, { "WebShot Forge", 7 }, { "WebShot Weaver", 7 }, { "Webshot Architect", 7 }, { "Thief Claw", 5 } };
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
        readonly static MethodInfo m_toolCompatability = SymbolExtensions.GetMethodInfo((Expression<Func<ToolItemType, ToolItemType, bool>>)((type1, type2) => ToolCompatability(type1, type2)));
        

        public static int ExtraColorsGetAvailableSlotCount(IEnumerable<InventoryToolCrestSlot> slots, ToolItemType toolType, bool checkEmpty)
        {
            int count = 0;
            foreach (InventoryToolCrestSlot slot in slots)
            {
                if (!slot.IsLocked && (ToolCompatability(slot.Type, toolType)) && (!checkEmpty || slot.EquippedItem == null))
                {
                    count++;
                }
            }
            return count;
        }

        public static InventoryToolCrestSlot ExtraColorsGetAvailableSlot(IEnumerable<InventoryToolCrestSlot> slots, ToolItemType toolType)
        {
            InventoryToolCrestSlot slot2 = null;
            foreach (InventoryToolCrestSlot slot in slots)
            {
                if (!slot.IsLocked && ToolCompatability(slot.Type, toolType))
                {
                    if (!slot2)
                    {
                        slot2 = slot;
                    }
                    if (!slot.EquippedItem)
                    {
                        return slot;
                    }
                }
            }
            return slot2;
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(UI), "GetToolTypeColor")]
        public static bool GetToolTypeColorPrefix(ToolItemType type, ref Color __result)
        {
            var t = (int)type;
            if (t > 3) { __result = toolTypeColors[t-4]; return false; }
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
                if (AttackOnlyTypes.Contains(newType) && !AttackOnlyTypes.Contains((int)newItemData.Type)) { return; }

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
                //Log.LogInfo("Header info scale: " + ___listSectionHeaders[0].Sprite.spriteAtlasTextureScale);

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
        [HarmonyPatch(typeof(InventoryItemToolManager), "StartSelection")]
        public static void StartSelectionPostfix(ref InventoryItemToolManager __instance, InventoryToolCrestSlot slot, InventoryItemGrid ___toolList)
        {
            if(!(___toolList == null))
            {
                List<InventoryItemTool> listItems = ___toolList.GetListItems((InventoryItemTool toolItem) => toolItem.ToolType != slot.Type && ToolCompatability(toolItem.ToolType, slot.Type));
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
                    Traverse.Create(__instance).Field("SelectedSlot").SetValue(slot);
                    Traverse.Create(__instance).Field("EquipState").SetValue(EquipStates.SelectTool);
                    __instance.PlayMoveSound();
                    __instance.SetSelected(inventoryItemTool, null);
                    __instance.RefreshTools();
                }
            }
        }
        [HarmonyPrefix]
        [HarmonyPatch(typeof(InventoryItemToolManager),"TryPickupOrPlaceTool")]
        public static bool TryPickupOrPlaceToolPrefix(ToolItem tool, InventoryItemToolManager __instance, ref bool __result)
        {
            Traverse t = Traverse.Create(__instance);
            Traverse t_PickedUpTool = t.Field("PickedUpTool");
            t_PickedUpTool.SetValue(tool);
            if (!tool)
            {
                __result = false;
                return false;
            }

            IEnumerable<InventoryToolCrestSlot> enumerable  = null;
            IEnumerable<InventoryToolCrestSlot> enumerable2 = null;
            IEnumerable<InventoryToolCrestSlot> enumerable3 = null;

            if((bool)(t.Field("crestList").GetValue() as InventoryToolCrestList))
            {
                enumerable2 = (t.Field("crestList").GetValue() as InventoryToolCrestList).GetSlots();
                if(ExtraColorsGetAvailableSlotCount(enumerable2, tool.Type, checkEmpty: true) > 0)
                {
                    enumerable = enumerable2;
                }
            }

            if(enumerable2 != null && (bool)(t.Field("extraSlots").GetValue() as InventoryFloatingToolSlots)) 
            {
                enumerable3 = (t.Field("extraSlots").GetValue() as InventoryFloatingToolSlots).GetSlots();
                if (ExtraColorsGetAvailableSlotCount(enumerable3, tool.Type, checkEmpty: true) > 0)
                {
                    enumerable = enumerable3;
                }
            }

            if(enumerable == null)
            {
                if (ExtraColorsGetAvailableSlotCount(enumerable2, tool.Type, checkEmpty: false) > 0)
                {
                    enumerable = enumerable2;
                }
                else if (ExtraColorsGetAvailableSlotCount(enumerable3, tool.Type, checkEmpty: false) > 0)
                {
                    enumerable = enumerable3;
                }
            }

            if (enumerable != null)
            {
                InventoryToolCrestSlot availableSlot = ExtraColorsGetAvailableSlot(enumerable, tool.Type);
                if((bool) availableSlot)
                {
                    t.Field("EquipState").SetValue(EquipStates.PlaceTool);
                    t.Field("selectedBeforePickup").SetValue(__instance.CurrentSelected);
                    if(availableSlot.Type.IsAttackType())// && (tool.Type.IsAttackType() || (OriginalTypes.ContainsKey(tool.name) && OriginalTypes[tool.name].IsAttackType())))
                    {
                        if (ExtraColorsGetAvailableSlotCount(enumerable, availableSlot.Type, checkEmpty: false) == 1)
                        {
                            __instance.PlaceTool(availableSlot, isManual: true);
                        }
                        else
                        {
                            __instance.PlayMoveSound();
                            __instance.SetSelected(availableSlot, null);
                        }
                    }
                    else if (ExtraColorsGetAvailableSlotCount(enumerable, availableSlot.Type, checkEmpty: true) > 0)
                    {
                        __instance.PlaceTool(availableSlot, isManual: true);
                    }
                    else
                    {
                        int availableSlotCount  = ExtraColorsGetAvailableSlotCount(enumerable2, availableSlot.Type, checkEmpty: false);
                        int availableSlotCount2 = ExtraColorsGetAvailableSlotCount(enumerable3, availableSlot.Type, checkEmpty: false);
                        if (availableSlotCount + availableSlotCount2 == 1)
                        {
                            __instance.PlaceTool(availableSlot, isManual: true);
                        }
                        else
                        {
                            __instance.PlayMoveSound();
                            __instance.SetSelected(availableSlot, null);
                        }
                    }

                    __instance.RefreshTools();
                    __result = true;
                    return false;
                }
            }

            t_PickedUpTool.SetValue(null);
            __result = false;
            return false;
        }
        [HarmonyPostfix]
        [HarmonyPatch(typeof(ToolItemTypeExtensions), "IsAttackType")]
        static void IsAttackTypePostfix(ToolItemType type, ref bool __result)
        {
            __result = __result || AdditionalAttackTypes.Contains((int)type);
        }

        [HarmonyTranspiler]
        [HarmonyPatch(typeof(InventoryToolCrestSlot), "UpdateSlotDisplay")]
        [HarmonyPatch(typeof(InventoryItemTool), "GetNextSelectable")]
        [HarmonyPatch(typeof(InventoryItemToolManager), "PlaceTool")]
        static IEnumerable<CodeInstruction> SlotLoadTypeTranspiler(IEnumerable<CodeInstruction> instructions)
        {
            var c = CodeInstruction.Call("InventoryToolCrestSlot:get_Type");
            var cv = c.Clone();
            cv.opcode = OpCodes.Callvirt;
            var codeMatcher = new CodeMatcher(instructions);
            while (codeMatcher.MatchStartForward(new CodeMatch(c), new CodeMatch(OpCodes.Beq)).IsValid)
            {
                codeMatcher.Advance(1)
                    .InsertAndAdvance(new CodeInstruction(OpCodes.Call, m_toolCompatability))
                    .SetOpcodeAndAdvance(OpCodes.Brtrue);
            }
            codeMatcher.Start();
            while (codeMatcher.MatchStartForward(new CodeMatch(cv), new CodeMatch(OpCodes.Beq)).IsValid)
            {
                codeMatcher.Advance(1)
                    .InsertAndAdvance(new CodeInstruction(OpCodes.Call, m_toolCompatability))
                    .SetOpcodeAndAdvance(OpCodes.Brtrue);
            }
            codeMatcher.Start();
            while (codeMatcher.MatchStartForward(new CodeMatch(c), new CodeMatch(OpCodes.Bne_Un)).IsValid)
            {
                codeMatcher.Advance(1)
                    .InsertAndAdvance(new CodeInstruction(OpCodes.Call, m_toolCompatability))
                    .SetOpcodeAndAdvance(OpCodes.Brfalse);
            }
            codeMatcher.Start();
            while (codeMatcher.MatchStartForward(new CodeMatch(cv), new CodeMatch(OpCodes.Bne_Un)).IsValid)
            {
                codeMatcher.Advance(1)
                    .InsertAndAdvance(new CodeInstruction(OpCodes.Call, m_toolCompatability))
                    .SetOpcodeAndAdvance(OpCodes.Brfalse);
            }
            //codeMatcher.Instructions().Do(instr => Console.WriteLine("tpc  | " + instr.ToString()));
            return codeMatcher.InstructionEnumeration();
        }
        [HarmonyPostfix]
        [HarmonyPatch(typeof(InventoryItemTool), "GetNextSelectable")]
        public static void GetNextSelectable(SelectionDirection direction, ref InventoryItemSelectable __result, ref InventoryItemTool __instance)
        {
            if (__result is InventoryItemTool)
            {
                int d = (int)direction;
                var tool = __result as InventoryItemTool;
                if (tool == __instance)
                {
                    if (d > 1) { return; }
                    int i = (int)tool.ToolType;
                    var a = d == 1 ? (Action)(() => i--) : (Action)(() => i++);
                    a();

                }
            }
        }
        /*
        [HarmonyTranspiler]
        [HarmonyPatch(typeof(InventoryItemToolManager), "ToolListHasType")]
        static IEnumerable<CodeInstruction> FirstArgumentToolItemTypeTranspiler(IEnumerable<CodeInstruction> instructions)
        {
            var codeMatcher = new CodeMatcher(instructions);
            while (codeMatcher.MatchStartForward(new CodeMatch(OpCodes.Ldarg_1), new CodeMatch(OpCodes.Beq)).IsValid)
            {
                codeMatcher.Advance(1)
                    .InsertAndAdvance(new CodeInstruction(OpCodes.Call, m_toolCompatability))
                    .SetOpcodeAndAdvance(OpCodes.Brtrue);
            }
            codeMatcher.Start();
            while (codeMatcher.MatchStartForward(new CodeMatch(OpCodes.Ldarg_1), new CodeMatch(OpCodes.Bne_Un)).IsValid)
            {
                codeMatcher.Advance(1)
                    .InsertAndAdvance(new CodeInstruction(OpCodes.Call, m_toolCompatability))
                    .SetOpcodeAndAdvance(OpCodes.Brfalse);
            }
            //codeMatcher.Instructions().Do(instr => Console.WriteLine("tpc  | " + instr.ToString()));
            return codeMatcher.InstructionEnumeration();
        }
        [HarmonyTranspiler]
        [HarmonyPatch(typeof(InventoryItemToolManager), "GetAvailableSlot")]
        static IEnumerable<CodeInstruction> SecondArgumentToolItemTypeTranspiler(IEnumerable<CodeInstruction> instructions)
        {
            var codeMatcher = new CodeMatcher(instructions);
            while (codeMatcher.MatchStartForward(new CodeMatch(OpCodes.Ldarg_2), new CodeMatch(OpCodes.Beq)).IsValid)
            {
                codeMatcher.Advance(1)
                    .InsertAndAdvance(new CodeInstruction(OpCodes.Call, m_toolCompatability))
                    .SetOpcodeAndAdvance(OpCodes.Brtrue);
            }
            codeMatcher.Start();
            while (codeMatcher.MatchStartForward(new CodeMatch(OpCodes.Ldarg_2), new CodeMatch(OpCodes.Bne_Un)).IsValid)
            {
                codeMatcher.Advance(1)
                    .InsertAndAdvance(new CodeInstruction(OpCodes.Call, m_toolCompatability))
                    .SetOpcodeAndAdvance(OpCodes.Brfalse);
            }
            //codeMatcher.Instructions().Do(instr => Console.WriteLine("tpc  | " + instr.ToString()));
            return codeMatcher.InstructionEnumeration();
        }
        /**/
    }
}

