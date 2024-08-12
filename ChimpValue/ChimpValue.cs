using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using HarmonyLib;
using MelonLoader;
using Stronghold1DE;
using static EngineInterface;

namespace ChimpValue
{
    public class ChimpValueMod : MelonMod
    {
        public static byte[] previousTroopTypesAvailable;
        private static readonly Dictionary<int, int> chimpValues = new Dictionary<int, int>();
        private static IntPtr cachedModuleBaseAddress = IntPtr.Zero;
        public static Dictionary<int, int> chimpGoldCosts = new Dictionary<int, int>();
        private static Dictionary<string, string> lastLoggedMessages = new Dictionary<string, string>();

        public override void OnInitializeMelon()
        {
            var harmony = new HarmonyLib.Harmony("com.yourname.memorymod");
            harmony.PatchAll(Assembly.GetExecutingAssembly());
        }

        public static void UpdateChimpValuesFromConfig(Dictionary<Enums.eChimps, int> configValues)
        {
            chimpValues.Clear();
            chimpGoldCosts.Clear();

            foreach (var kvp in configValues)
            {
                int index = -1;

                switch (kvp.Key)
                {
                    case Enums.eChimps.CHIMP_TYPE_ARCHER:
                        index = 22;
                        break;
                    case Enums.eChimps.CHIMP_TYPE_XBOWMAN:
                        index = 23;
                        break;
                    case Enums.eChimps.CHIMP_TYPE_SPEARMAN:
                        index = 24;
                        break;
                    case Enums.eChimps.CHIMP_TYPE_PIKEMAN:
                        index = 25;
                        break;
                    case Enums.eChimps.CHIMP_TYPE_MACEMAN:
                        index = 26;
                        break;
                    case Enums.eChimps.CHIMP_TYPE_SWORDSMAN:
                        index = 27;
                        break;
                    case Enums.eChimps.CHIMP_TYPE_KNIGHT:
                        index = 28;
                        break;
                }

                if (index != -1)
                {
                    chimpValues[index] = kvp.Value;
                    chimpGoldCosts[index] = kvp.Value;
                }
            }
        }

        public static void ModifyMemory()
        {
            if (cachedModuleBaseAddress == IntPtr.Zero)
            {
                string dllPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"Stronghold 1 Definitive Edition_Data\Plugins\x86_64\StrongholdDE.dll");

                if (!File.Exists(dllPath))
                {
                    LogMessage("DLL", $"DLL file not found at path: {dllPath}");
                    return;
                }

                cachedModuleBaseAddress = LoadLibrary(dllPath);

                if (cachedModuleBaseAddress == IntPtr.Zero)
                {
                    LogMessage("DLL", "Failed to load StrongholdDE.dll!");
                    return;
                }
            }

            int[] offsets = new int[]
            {
                0x451CD0,
                0x451CD4,
                0x451CD8,
                0x451CDC,
                0x451CE0,
                0x451CE4,
                0x451CE8
            };

            foreach (var kvp in chimpValues)
            {
                int index = kvp.Key;
                if (index >= 0 && index < offsets.Length)
                {
                    IntPtr address = IntPtr.Add(cachedModuleBaseAddress, offsets[index]);
                    WriteMemory(Process.GetCurrentProcess().Handle, address, kvp.Value);
                    LogMessage("Memory", $"Modified value at {address.ToString("X")} to {kvp.Value}");
                }
            }
        }

        private static void WriteMemory(IntPtr processHandle, IntPtr address, int value)
        {
            byte[] buffer = BitConverter.GetBytes(value);
            WriteProcessMemory(processHandle, address, buffer, buffer.Length, out _);
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr LoadLibrary(string dllToLoad);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool WriteProcessMemory(IntPtr hProcess, IntPtr lpBaseAddress, byte[] lpBuffer, int nSize, out int lpNumberOfBytesWritten);

        public static void LogMessage(string context, string message)  // Добавляем контекст
        {
            if (!lastLoggedMessages.TryGetValue(context, out string lastMessage) || lastMessage != message)
            {
                MelonLogger.Msg(message);
                lastLoggedMessages[context] = message;
            }
        }
    }

    [HarmonyPatch(typeof(GameData), "getChimpGoldCost")]
    public static class GetChimpGoldCostPatch
    {
        [HarmonyPostfix]
        public static void Postfix(ref int __result, int troopChimpType)
        {
            if (ChimpValueMod.chimpGoldCosts.TryGetValue(troopChimpType, out int cost))
            {
                __result = cost;
            }
        }
    }

    [HarmonyPatch(typeof(EngineInterface), "CopyPlayStateStruct")]
    public static class CopyPlayStateStructPatch
    {
        public static void Postfix(ref PlayState __result)
        {
            if (__result?.troop_types_available != null)
            {
                if (ChimpValueMod.previousTroopTypesAvailable == null || !__result.troop_types_available.SequenceEqual(ChimpValueMod.previousTroopTypesAvailable))
                {
                    string hexValues = string.Join(" ", __result.troop_types_available.Select(b => b.ToString("X2")));
                    ChimpValueMod.LogMessage("TroopTypes", $"Troop Types Available (Hex): {hexValues}");

                    ChimpValueMod.previousTroopTypesAvailable = (byte[])__result.troop_types_available.Clone();
                }
            }
        }
    }

    [HarmonyPatch(typeof(GameData), "setGameState")]
    public static class Patch_SetGameState
    {
        private static string previousCurrentMapName;
        private static string previousTransformedMapName;

        [HarmonyPostfix]
        public static void Postfix(GameData __instance)
        {
            if (__instance.currentMapName != previousCurrentMapName)
            {
                string mapName = __instance.currentMapName;
                string transformedMapName = mapName;

                if (IsMapNameInRussian(mapName))
                {
                    transformedMapName = TransformMapName(mapName);

                    if (transformedMapName != mapName)
                    {
                        ChimpValueMod.LogMessage("MapName", $"Transformed Map Name: {transformedMapName}");
                    }
                }

                // Проверка, если имя карты уже преобразовано и не изменилось, не выполнять дальнейшие действия
                if (previousTransformedMapName == transformedMapName)
                {
                    return;
                }

                LoadMapConfig(transformedMapName);
                previousCurrentMapName = mapName;
                previousTransformedMapName = transformedMapName;
            }
        }

        private static bool IsMapNameInRussian(string mapName)
        {
            return mapName.Any(c => c >= 'А' && c <= 'я');
        }

        private static string TransformMapName(string originalMapName)
        {
            try
            {
                string filePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"UserData\Config\MapNameTranslations.txt");

                if (File.Exists(filePath))
                {
                    var lines = File.ReadAllLines(filePath);

                    foreach (var line in lines)
                    {
                        var parts = line.Split('=');

                        if (parts.Length == 2 && string.Equals(parts[0].Trim(), originalMapName.Trim(), StringComparison.OrdinalIgnoreCase))
                        {
                            return parts[1].TrimStart();
                        }
                    }
                }
                else
                {
                    ChimpValueMod.LogMessage("MapNameTranslation", $"MapNameTranslations.txt file not found at path: {filePath}");
                }
            }
            catch (Exception ex)
            {
                ChimpValueMod.LogMessage("MapNameTranslation", $"Failed to transform map name from file: {ex.Message}");
            }

            return originalMapName;
        }

        private static void LoadMapConfig(string mapName)
        {
            bool configLoaded = false;

            string directory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"UserData\Config\Maps");

            string filePath = FindFileIgnoringCase(directory, $"{mapName}.txt");

            if (!string.IsNullOrEmpty(filePath) && File.Exists(filePath))
            {
                try
                {
                    string configContent = File.ReadAllText(filePath);
                    ChimpValueMod.LogMessage("ConfigLoad", $"Loaded config from {filePath}:\n{configContent}");

                    var configValues = ParseConfigFile(configContent);
                    ChimpValueMod.UpdateChimpValuesFromConfig(configValues);

                    ChimpValueMod.ModifyMemory();

                    configLoaded = true;
                }
                catch (Exception ex)
                {
                    ChimpValueMod.LogMessage("ConfigLoad", $"Failed to load config from {filePath}: {ex.Message}");
                }
            }
            else
            {
                ChimpValueMod.LogMessage("ConfigLoad", $"Config file not found for map: {mapName}");
            }

            if (!configLoaded)
            {
                ChimpValueMod.LogMessage("ConfigLoad", "No config file found, using default values.");
                SetDefaultChimpValues();
                ChimpValueMod.ModifyMemory();
            }
        }

        private static void SetDefaultChimpValues()
        {
            var defaultValues = new Dictionary<Enums.eChimps, int>
        {
            { Enums.eChimps.CHIMP_TYPE_ARCHER, 12 },
            { Enums.eChimps.CHIMP_TYPE_SPEARMAN, 8 },
            { Enums.eChimps.CHIMP_TYPE_MACEMAN, 20 },
            { Enums.eChimps.CHIMP_TYPE_XBOWMAN, 20 },
            { Enums.eChimps.CHIMP_TYPE_PIKEMAN, 20 },
            { Enums.eChimps.CHIMP_TYPE_SWORDSMAN, 40 },
            { Enums.eChimps.CHIMP_TYPE_KNIGHT, 40 }
        };

            ChimpValueMod.UpdateChimpValuesFromConfig(defaultValues);

            ChimpValueMod.chimpGoldCosts = new Dictionary<int, int>
            {
                [22] = 12,
                [23] = 20,
                [24] = 8,
                [25] = 20,
                [26] = 20,
                [27] = 40,
                [28] = 40
            };
        }

        private static string FindFileIgnoringCase(string directory, string fileName)
        {
            try
            {
                return Directory.EnumerateFiles(directory, fileName, SearchOption.TopDirectoryOnly)
                                .FirstOrDefault(f => string.Equals(Path.GetFileName(f), fileName, StringComparison.OrdinalIgnoreCase));
            }
            catch (Exception ex)
            {
                ChimpValueMod.LogMessage("FileSearch", $"Failed to search for file '{fileName}' in directory '{directory}': {ex.Message}");
                return null;
            }
        }

        private static Dictionary<Enums.eChimps, int> ParseConfigFile(string configContent)
        {
            var configValues = new Dictionary<Enums.eChimps, int>();

            foreach (var line in configContent.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var split = line.Split('=');
                if (split.Length == 2)
                {
                    Enums.eChimps chimpEnum;
                    switch (split[0].ToUpperInvariant())
                    {
                        case "CHIMP_TYPE_ARCHER":
                            chimpEnum = Enums.eChimps.CHIMP_TYPE_ARCHER;
                            break;
                        case "CHIMP_TYPE_XBOWMAN":
                            chimpEnum = Enums.eChimps.CHIMP_TYPE_XBOWMAN;
                            break;
                        case "CHIMP_TYPE_SPEARMAN":
                            chimpEnum = Enums.eChimps.CHIMP_TYPE_SPEARMAN;
                            break;
                        case "CHIMP_TYPE_PIKEMAN":
                            chimpEnum = Enums.eChimps.CHIMP_TYPE_PIKEMAN;
                            break;
                        case "CHIMP_TYPE_MACEMAN":
                            chimpEnum = Enums.eChimps.CHIMP_TYPE_MACEMAN;
                            break;
                        case "CHIMP_TYPE_SWORDSMAN":
                            chimpEnum = Enums.eChimps.CHIMP_TYPE_SWORDSMAN;
                            break;
                        case "CHIMP_TYPE_KNIGHT":
                            chimpEnum = Enums.eChimps.CHIMP_TYPE_KNIGHT;
                            break;
                        default:
                            ChimpValueMod.LogMessage("ConfigParse", $"Unknown chimp type in config: {split[0]}");
                            continue;
                    }

                    if (int.TryParse(split[1], out int value))
                    {
                        configValues[chimpEnum] = value;
                    }
                }
            }

            return configValues;
        }
    }
}