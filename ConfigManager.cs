using BepInEx.Configuration;
using HutongGames.PlayMaker.Actions;
using InControl;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using UnityStandardAssets.ImageEffects;

namespace ExtraToolColors
{
    public struct ModifyMultipleToolsSetting
    {
        public ConfigEntry<string> setting;
        public List<string> affectedTools;

        public ModifyMultipleToolsSetting(string section, ConfigFile file, string toolName, IEnumerable<string> affectedSettings, AcceptableValueList<string> options, string defaultValue = "NOSETTING", EventHandler settingChangedEvent = null)
        {
            //applySetting = file.Bind(section, "Modify_All_Instances_of_" + ConfigManager.ProcessSettingName(booleanSettingName) + "_as_One", true, "Setting to determine whether the following setting affects all instances of " + booleanSettingName + " (true, false)");
            //applySetting.SettingChanged += settingChangedEvent;
            setting = file.Bind(section, toolName, defaultValue, 
                new ConfigDescription("Changes " + toolName + " tool type", options));
            setting.SettingChanged += settingChangedEvent;
            affectedTools = affectedSettings.ToList();
        }
    }

    public class ConfigManager
    {
        public static List<string> internalSkillToolNames = new List<string>() {
            // Silk Skills
            "Silk Spear", "Thread Sphere", "Parry",
            "Silk Charge", "Silk Bomb", "Silk Boss Needle"
        };

        public static List<string> internalRedToolNames = new List<string>() {
            // Red Tools
            "Straight Pin", "Tri Pin", "Sting Shard",
            "Tack", "Harpoon",
            "Shakra Ring", "Pimpilo", "Conch Drill",
            "Screw Attack", "Cogwork Saw",
            "Cogwork Flier", "Rosary Cannon", "Lightning Rod",
            "Flintstone", "Silk Snare", "Flea Brew", "Lifeblood Syringe", "Extractor"
        };

        public static List<string> internalBlueToolNames = new List<string>() {
            // Blue Tools
            "Lava Charm", "Bell Bind",
            "Poison Pouch", "Fractured Mask", "MultiBind",
            "White Ring", "Brolly Spike", "Quickbind",
            "Spool Extender", "Reserve Bind",
            "Revenge Crystal", "Thief Claw", "Zap Imbuement",
            "Quick Sling", "Maggot Charm", "Longneedle",
            "Wisp Lantern", "Flea Charm", "Pinstress Tool"
        };

        public static List<string> internalYellowToolNames = new List<string>(){
            // Yellow Tools
            "Compass", "Bone Necklace", "RosaryMagnet",
            "Weighted Anklet", "Barbed Wire", "Dead Mans Purse", "Shell Satchel",
            "Magnetite Dice", "Scuttlebrace", "Wallcling",
            "Musician Charm", "Sprintmaster", "Thief Charm"
        };
        public static List<string> InternalIntToolNames(int i)
        {
            switch (i)
            {
                case 0: return internalSkillToolNames;
                case 1: return internalRedToolNames;
                case 2: return internalBlueToolNames;
                case 3: return internalYellowToolNames;
                default: return new List<string>();
            }
        }
        public static AcceptableValueList<string> attackToolOptions = new AcceptableValueList<string>("White", "Red", "Pink");

        public static string attackToolOptionsString = "(White, Red, Pink)";

        public static AcceptableValueList<string> toolOptions = new AcceptableValueList<string>("White", "Red", "Blue", "Yellow", "Green", "Purple", "Orange", "Pink");

        public static string toolOptionsString = "(White, Red, Blue, Yellow, Green, Purple, Orange, Pink)";

        public ConfigFile file;

        public string title;

        public Dictionary<string, ConfigEntry<string>> toolSettings;

        public List<ModifyMultipleToolsSetting> modifyMultiples;

        public List<Tuple<string, List<string>, AcceptableValueList<string>, string>> ModifyMultipleAsOne { get; private set; } = new List<Tuple<string, List<string>, AcceptableValueList<string>, string>>()
        {
            Tuple.Create( "WebShot", new List<string>{"WebShot Weaver", "WebShot Architect", "WebShot Forge" }, attackToolOptions, "Red" ),
            Tuple.Create( "Mosscreep", new List<string>{"Mosscreep Tool 1", "Mosscreep Tool 2" }, toolOptions, "Blue"),
            Tuple.Create( "Curve Claws", new List<string>{"Curve Claws", "Curve Claws Upgraded" }, attackToolOptions, "Red" ),
            Tuple.Create( "Dazzle Bind", new List<string>{ "Dazzle Bind", "Dazzle Bind Upgraded" }, toolOptions, "Blue" )
        };

        public ConfigEntry<bool> modifyMosscreepsAsOne;

        public ConfigEntry<bool> modifyCurveClawsAsOne;

        public ConfigEntry<bool> modifyDazzleBindsAsOne;

        public List<ConfigEntry<string>> toolTypes;

        public Dictionary<string, ToolItemType> changes;

        public Dictionary<string, ToolItemType> RealChanges
        {
            get;
            private set;
        }

        public void SetChange(string toolName, ToolItemType type)
        {
            if (!changes.ContainsKey(toolName))
                changes.Add(toolName, type);
            else
                changes[toolName] = type;
            bool a, b;
            a = RealChanges.ContainsKey(toolName);
            b = InternalIntToolNames((int)type).Contains(toolName);
            foreach (var tup in ModifyMultipleAsOne)
            {
                ToolItemType? MMAO_t = ConvertStringToToolType(tup.Item4);
                if (MMAO_t.HasValue && tup.Item2.Contains(toolName) && MMAO_t.Value == type) { b = true; break; }
            }
            switch ((a ? 2 : 0) + (b ? 1 : 0))
            {
                case 0: RealChanges.Add(toolName, type); Console.WriteLine(toolName); break;
                case 1: break;
                case 2: RealChanges[toolName] = type; break;
                case 3: RealChanges.Remove(toolName); break;
            }
        }
        public void RemoveChange(string toolName)
        {
            if (changes.ContainsKey(toolName)) changes.Remove(toolName);
            if (RealChanges.ContainsKey(toolName)) RealChanges.Remove(toolName);
        }

        public ConfigManager(ConfigFile configFile, string section = "ExtraToolColorsTransmog")
        {
            file = configFile;
            title = section;
            changes = new Dictionary<string, ToolItemType>();
        }


        public void Init()
        {
            modifyMultiples = new List<ModifyMultipleToolsSetting>();
            toolSettings = new Dictionary<string, ConfigEntry<string>>();
            foreach (var tuple in ModifyMultipleAsOne)
            {
                modifyMultiples.Add(new ModifyMultipleToolsSetting(title, file, tuple.Item1, tuple.Item2, tuple.Item3, tuple.Item4, MultipleSettingChangedEvent));
            }

            foreach (var toolname in internalSkillToolNames)
            {
                toolSettings.Add(toolname, file.Bind(title, toolname, "White",
                    new ConfigDescription("Changes " + toolname + " tool type", attackToolOptions)));
                toolSettings[toolname].SettingChanged += SingleSettingChangedEvent;
            }
            foreach (var toolname in internalRedToolNames)
            {
                toolSettings.Add(toolname, file.Bind(title, toolname, "Red",
                    new ConfigDescription("Changes " + toolname + " tool type", attackToolOptions)));
                toolSettings[toolname].SettingChanged += SingleSettingChangedEvent;
            }
            foreach (var toolname in internalBlueToolNames)
            {
                toolSettings.Add(toolname, file.Bind(title, toolname, "Blue",
                    new ConfigDescription("Changes " + toolname + " tool type", toolOptions)));
                toolSettings[toolname].SettingChanged += SingleSettingChangedEvent;
            }
            foreach (var toolname in internalYellowToolNames)
            {
                toolSettings.Add(toolname, file.Bind(title, toolname, "Yellow",
                    new ConfigDescription("Changes " + toolname + " tool type", toolOptions)));
                toolSettings[toolname].SettingChanged += SingleSettingChangedEvent;
            }
            changes = GetAllEntries();
            RealChanges = GetAllRealChangedEntries();
        }
        protected virtual void SingleSettingChangedEvent(object sender, EventArgs e)
        {
            if (sender is ConfigEntry<string>)
            {
                var entry = sender as ConfigEntry<string>;
                string key = UnProcessSettingName(entry.Definition.Key);
                if (key != null)
                {
                    ToolItemType? type = GetEntryByName(key);
                    if (type.HasValue)
                    {
                        SetChange(key, type.Value);
                    }
                }
            }

        }
        protected virtual void MultipleSettingChangedEvent(object sender, EventArgs e)
        {
            if (sender is ConfigEntry<string> || sender is ConfigEntry<bool>)
            {
                foreach (var multiple in modifyMultiples)
                {
                    if (multiple.setting == sender as ConfigEntry<string>)
                    {
                        ToolItemType? type = ConvertStringToToolType(multiple.setting.Value);
                        if (type.HasValue)
                            foreach (string toolName in multiple.affectedTools)
                                SetChange(toolName, type.Value);
                        else
                            foreach (string toolName in multiple.affectedTools)
                                RemoveChange(toolName);
                    }
                }
            }
        }
        public Dictionary<string, ToolItemType> GetAllEntries()
        {
            var returnValue = new Dictionary<string, ToolItemType>();
            foreach (string key in toolSettings.Keys)
            {
                ToolItemType? value = GetEntryByName(key);
                if (value != null) returnValue.Add(key, (ToolItemType)value);
            }
            return returnValue;
        }
        public Dictionary<string, ToolItemType> GetAllRealChangedEntries()
        {
            var returnValue = new Dictionary<string, ToolItemType>();
            foreach (string key in toolSettings.Keys)
            {
                ToolItemType? value = GetEntryByName(key);
                if (value != null && !InternalIntToolNames((int)value.Value).Contains(key)) returnValue.Add(key, (ToolItemType)value);
            }
            return returnValue;
        }

        public ToolItemType? GetEntryByName(string toolName)
        {
            if (!toolSettings.ContainsKey(toolName)) return null;
            foreach (ModifyMultipleToolsSetting setting in modifyMultiples)
            {
                if (setting.affectedTools.Contains(toolName) && setting.setting.Value != (string)setting.setting.DefaultValue) return ConvertStringToToolType(setting.setting.Value);
            }
            string value = toolSettings[toolName].Value;
            if (value == (string)toolSettings[toolName].DefaultValue) return null;
            return ConvertStringToToolType(value);
        }

        public static ToolItemType? ConvertStringToToolType(string str)
        {
            string upper = str.ToUpper();
            if (Regex.IsMatch(upper, "4|(GREEN)|((?=.*((BLUE)|(DEFEND)|1)).*((YELLOW)|(EXPLORE)|2))")) return ExtraToolColors.Green;
            else if (Regex.IsMatch(upper, "5|(PURPLE)|((?=.*((BLUE)|(DEFEND)|1)).*((RED)|(ATTACK)|0))")) return ExtraToolColors.Purple;
            else if (Regex.IsMatch(upper, "6|(ORANGE)|((?=.*((YELLOW)|(EXPLORE)|2)).*((RED)|(ATTACK)|0))")) return ExtraToolColors.Orange;
            else if (Regex.IsMatch(upper, "7|(PINK)|((?=.*((WHITE)|(SKILL)|3)).*((RED)|(ATTACK)|0))")) return ExtraToolColors.Pink;
            else if (Regex.IsMatch(upper, "0|(RED)|(ATTACK)")) return ToolItemType.Red;
            else if (Regex.IsMatch(upper, "1|(BLUE)|(DEFEND)")) return ToolItemType.Blue;
            else if (Regex.IsMatch(upper, "2|(YELLOW)|(EXPLORE)")) return ToolItemType.Yellow;
            else if (Regex.IsMatch(upper, "3|(SKILL)|(WHITE)")) return ToolItemType.Skill;
            return null;
        }
        public static string ProcessSettingName(string settingName)
        {
            return settingName.Replace(' ', '_');
        }
        public static string UnProcessSettingName(string settingName)
        {
            return settingName.Replace('_', ' ');
        }
        public void Randomize(int seed)
        {
            Random rand = new Random(seed);
            foreach (var item in toolSettings)
            {
                ToolItemType? type = GetEntryByName(item.Key);
                if (type.HasValue)
                {
                    int newT = ExtraToolColors.AttackOnlyTypes.Contains(type.Value) ? (int)ExtraToolColors.AttackOnlyTypes[rand.Next(ExtraToolColors.AttackOnlyTypes.Count)] : rand.Next(ExtraToolColors.N_TYPES);
                    item.Value.SetSerializedValue(ExtraToolColors.toolItemTypeNames[newT]);
                    SetChange(item.Key, (ToolItemType)newT);
                }
            }
            foreach (var item in modifyMultiples)
            {
                ToolItemType? type = ConvertStringToToolType((string)item.setting.DefaultValue);
                if (type.HasValue)
                {
                    int newT = ExtraToolColors.AttackOnlyTypes.Contains(type.Value) ? (int)ExtraToolColors.AttackOnlyTypes[rand.Next(ExtraToolColors.AttackOnlyTypes.Count)] : rand.Next(ExtraToolColors.N_TYPES);
                    item.setting.SetSerializedValue(ExtraToolColors.toolItemTypeNames[newT]);
                    foreach (var toolName in item.affectedTools)
                    {
                        SetChange(toolName, (ToolItemType)newT);
                    }
                } 
            }    
        }
        public void ResetToDefaults()
        {
            changes.Clear();
            RealChanges.Clear();
            foreach (var item in toolSettings)
                item.Value.BoxedValue = item.Value.DefaultValue;
            foreach (var mod in modifyMultiples)
                mod.setting.BoxedValue =  mod.setting.DefaultValue;
        }
    }
}
