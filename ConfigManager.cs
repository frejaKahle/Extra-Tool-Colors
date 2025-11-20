using BepInEx.Configuration;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace ExtraToolColors
{
    public struct ModifyMultipleToolsSetting
    {
        public ConfigEntry<bool> applySetting;
        public ConfigEntry<string> setting;
        public List<string> affectedTools;

        public ModifyMultipleToolsSetting(string section, ConfigFile file, string booleanSettingName, IEnumerable<string> affectedSettings, string options, string defaultValue = "NOSETTING")
        {
            applySetting = file.Bind(section, "Modify_All_Instances_of_" + ConfigManager.ProcessSettingName(booleanSettingName) + "_as_One", true, "Setting to determine whether the following setting affects all instances of " + booleanSettingName + " (true, false)");
            setting = file.Bind(section, "Modify_Instances_of_" + ConfigManager.ProcessSettingName(booleanSettingName) + "_to_be", defaultValue, "Setting to apply to all instances of " + booleanSettingName + " if previous setting is set to true " + options);
            affectedTools = affectedSettings.ToList();
        }
    }

    public class ConfigManager
    {
        public static List<string> internalSkillToolNames = new List<string>() {
            // Silk Skills
            "Silk Spear", "Thread Sphere", "Parry",
            "Silk Charge", "Slik Bomb", "Silk Boss Needle" 
        };

        public static List<string> internalRedToolNames = new List<string>() {
            // Red Tools
            "Straight Pin", "Tri Pin", "Sting Shard",
            "Tack", "Harpoon", "Curve Claws", "Curve Claws Upgraded",
            "Shakra Ring", "Pimpilo", "Conch Drill",
            "WebShot Weaver", "WebShot Architect", "WebShot Forge", "Screw Attack", "Cogwork Saw",
            "Cogwork Flier", "Rosary Cannon", "Lightning Rod",
            "Flintstone", "Silk Snare", "Flea Brew", "Lifeblood Syringe", "Extractor"
        };

        public static List<string> internalBlueToolNames = new List<string>() {
            // Blue Tools
            "Mosscreep Tool 1", "Mosscreep Tool 2", "Lava Charm", "Bell Bind",
            "Poison Pouch", "Fractured Mask", "MultiBind",
            "White Ring", "Brolly Spike", "Quickbind",
            "Spool Extender", "Reserve Bind", "Dazzle Bind", "Dazzle Bind Upgraded",
            "Revenge Crystal", "Thief Claw", "Zap Imbuement",
            "Quick Sling", "Maggot Charm", "Longneedle",
            "Wisp Lanter", "Flea Charm", "Pinstress Tool"
        };

        public static List<string> internalYellowToolNames = new List<string>(){
            // Yellow Tools
            "Compass", "Bone Necklace", "RosaryMagnet",
            "Weighted Anklet", "Barbed Wire", "Dead Mans Purse", "Shell Satchel",
            "Magnetite Dice", "Scuttlebrace", "Wallcling",
            "Musician Charm", "Sprintmaster", "Thief Charm"
        };

        public static string attackToolOptions = "(Skill, Red, Pink)";

        public static string toolOptions = "(Skill, Red, Blue, Yellow, Green, Purple, Orange, Pink)";

        public ConfigFile file;

        public string title;

        public Dictionary<string, ConfigEntry<string>> toolSettings;

        public List<ModifyMultipleToolsSetting> modifyMultiples;

        public List<Tuple<string, List<string>, string, string>> ModifyMultipleAsOne { get; private set; } = new List<Tuple<string, List<string>, string, string>>()
        {
            Tuple.Create( "WebShot", new List<string>{"WebShot Weaver", "WebShot Architect", "WebShot Forge" }, attackToolOptions, "Red" ),
            Tuple.Create( "Mosscreep", new List<string>{"Mosscreep Tool 1", "Mosscreep Tool 2" }, toolOptions , "Blue"),
            Tuple.Create( "Curve Claws", new List<string>{"Curve Claws", "Curve Claws Upgraded" }, attackToolOptions, "Red" ),
            Tuple.Create( "Dazzle Bind", new List<string>{ "Dazzle Bind", "Dazzle Bind Upgraded" }, toolOptions, "Blue" )
        };

        public ConfigEntry<bool> modifyMosscreepsAsOne;

        public ConfigEntry<bool> modifyCurveClawsAsOne;

        public ConfigEntry<bool> modifyDazzleBindsAsOne;

        public List<ConfigEntry<string>> toolTypes;

        public ConfigManager(ConfigFile configFile, string section = "ExtraToolColorsTransmog")
        {
            file = configFile;
            title = section;
        }

        public void Init()
        {
            modifyMultiples = new List<ModifyMultipleToolsSetting>();
            toolSettings = new Dictionary<string, ConfigEntry<string>>();
            foreach(var tuple in ModifyMultipleAsOne)
            {
                modifyMultiples.Add(new ModifyMultipleToolsSetting(title,file,tuple.Item1,tuple.Item2,tuple.Item3,tuple.Item4));
            }

            foreach (var toolname in internalSkillToolNames)
            {
                toolSettings.Add(toolname, file.Bind(title, ProcessSettingName(toolname), "Skill", "Changes Tool Type of " + toolname + " to specified type" + (!modifyMultiples.Any(mmts => mmts.affectedTools.Contains(toolname)) ? "if not set by its modify multiple setting " : " ") + attackToolOptions));
            }
            foreach (var toolname in internalRedToolNames)
            {
                toolSettings.Add(toolname, file.Bind(title, ProcessSettingName(toolname), "Red", "Changes Tool Type of " + toolname + " to specified type" + (!modifyMultiples.Any(mmts => mmts.affectedTools.Contains(toolname)) ? "if not set by its modify multiple setting " : " ") + attackToolOptions));
            }
            foreach (var toolname in internalBlueToolNames)
            {
                toolSettings.Add(toolname, file.Bind(title, ProcessSettingName(toolname), "Blue", "Changes Tool Type of " + toolname + " to specified type" + (!modifyMultiples.Any(mmts => mmts.affectedTools.Contains(toolname)) ? "if not set by its modify multiple setting " : " ") + toolOptions));
            }
            foreach (var toolname in internalYellowToolNames)
            {
                toolSettings.Add(toolname, file.Bind(title, ProcessSettingName(toolname), "Yellow", "Changes Tool Type of " + toolname + " to specified type" + (!modifyMultiples.Any(mmts => mmts.affectedTools.Contains(toolname)) ? "if not set by its modify multiple setting " : " ") + toolOptions));
            }
        }
        public Dictionary<string, ToolItemType> GetAllEntries()
        {
            var returnValue = new Dictionary<string, ToolItemType>();
            foreach(string key in toolSettings.Keys)
            {
                ToolItemType? value = GetEntryByName(key);
                if (value != null) returnValue.Add(key, (ToolItemType)value);
            }
            return returnValue;
        }

        public ToolItemType? GetEntryByName(string toolName)
        {
            if (!toolSettings.ContainsKey(toolName)) return null;
            string value = toolSettings[toolName].Value;
            foreach (ModifyMultipleToolsSetting setting in modifyMultiples)
            {
                if (setting.applySetting.Value && setting.affectedTools.Contains(toolName)) return ConvertStringToToolType(setting.setting.Value);
            }
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
    }
}
