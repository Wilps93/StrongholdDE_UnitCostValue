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
        private static readonly Dictionary<int, int> chimpValues = new Dictionary<int, int>(); // Словарь для хранения значений, считанных из конфигурационного файла
        private static IntPtr cachedModuleBaseAddress = IntPtr.Zero; // Кэшируем базовый адрес модуля
        public static Dictionary<int, int> chimpGoldCosts = new Dictionary<int, int>(); // Словарь для хранения стоимости в золоте

        public override void OnInitializeMelon()
        {
            // Создаем экземпляр Harmony и патчим методы
            var harmony = new HarmonyLib.Harmony("com.yourname.memorymod");
            harmony.PatchAll();
        }

        public static void UpdateChimpValuesFromConfig(Dictionary<Enums.eChimps, int> configValues)
        {
            chimpValues.Clear(); // Очищаем предыдущие значения
            chimpGoldCosts.Clear(); // Очищаем предыдущие значения стоимости в золоте

            // Заполняем словарь значениями из файла конфигурации
            foreach (var kvp in configValues)
            {
                int index = -1;

                // Используем традиционный switch для C# 7.3
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
                    chimpGoldCosts[index] = kvp.Value; // Стоимость в золоте устанавливается равной значению из конфигурации
                }
            }
        }

        public static void ModifyMemory()
        {
            // Используем кэшированный базовый адрес модуля, если он уже был загружен
            if (cachedModuleBaseAddress == IntPtr.Zero)
            {
                string dllPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"Stronghold 1 Definitive Edition_Data\Plugins\x86_64\StrongholdDE.dll");

                if (!File.Exists(dllPath))
                {
                    MelonLogger.Error($"DLL file not found at path: {dllPath}");
                    return;
                }

                cachedModuleBaseAddress = LoadLibrary(dllPath);

                if (cachedModuleBaseAddress == IntPtr.Zero)
                {
                    MelonLogger.Error("Failed to load StrongholdDE.dll!");
                    return;
                }
            }

            // Список смещений
            int[] offsets = new int[]
            {
                0x451CD0, // Лучник
                0x451CD4, // Арбалетчик
                0x451CD8, // Копейщик
                0x451CDC, // Пикинер
                0x451CE0, // Пехотинец
                0x451CE4, // Мечник
                0x451CE8  // Рыцарь
            };

            // Изменяем значения в памяти, используя значения из конфигурационного файла
            foreach (var kvp in chimpValues)
            {
                int index = kvp.Key;
                if (index >= 0 && index < offsets.Length)
                {
                    IntPtr address = IntPtr.Add(cachedModuleBaseAddress, offsets[index]);
                    WriteMemory(Process.GetCurrentProcess().Handle, address, kvp.Value);
                    MelonLogger.Msg($"Modified value at {address.ToString("X")} to {kvp.Value}");
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
    }

    [HarmonyPatch(typeof(MainViewModel))]
    [HarmonyPatch("set_BriefingMissionTitle")] // Указываем явное название метода-сеттера
    public static class BriefingMissionTitlePatch
    {
        private static string lastLoggedTitle = string.Empty; // Переменная для хранения последнего залогированного значения

        // Метод, который будет вызываться после установки BriefingMissionTitle
        [HarmonyPostfix]
        public static void Postfix(string value)
        {
            // Преобразуем значение на основе содержимого файла
            string transformedValue = TransformValueFromFile(value);

            // Логируем новое значение BriefingMissionTitle, если оно отличается от последнего залогированного и не является пустым
            if (!string.IsNullOrEmpty(transformedValue) && transformedValue != lastLoggedTitle)
            {
                MelonLogger.Msg($"BriefingMissionTitle set to: {transformedValue}");
                lastLoggedTitle = transformedValue; // Обновляем последнее залогированное значение

                // Используем название карты как есть
                LoadMapConfig(transformedValue);
            }
        }

        private static string TransformValueFromFile(string originalValue)
        {
            try
            {
                // Путь относительно корневой директории приложения
                string filePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"UserData\Config\TextBrifing.txt");

                if (File.Exists(filePath))
                {
                    var lines = File.ReadAllLines(filePath);

                    foreach (var line in lines)
                    {
                        var parts = line.Split('=');

                        if (parts.Length == 2 && string.Equals(parts[0].Trim(), originalValue.Trim(), StringComparison.OrdinalIgnoreCase))
                        {
                            // Возвращаем значение после "=" игнорируя первый пробел
                            return parts[1].TrimStart();
                        }
                    }
                }
                else
                {
                    MelonLogger.Warning($"TextBrifing.txt file not found at path: {filePath}");
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"Failed to transform value from file: {ex.Message}");
            }

            // Если ничего не найдено, возвращаем оригинальное значение
            return originalValue;
        }

        private static void LoadMapConfig(string mapName)
        {
            bool configLoaded = false;

            // Единственный путь для проверки
            string directory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"UserData\Config\Maps");

            string filePath = FindFileIgnoringCase(directory, $"{mapName}.txt");

            if (!string.IsNullOrEmpty(filePath) && File.Exists(filePath))
            {
                try
                {
                    string configContent = File.ReadAllText(filePath);
                    MelonLogger.Msg($"Loaded config from {filePath}:\n{configContent}");

                    var configValues = ParseConfigFile(configContent);
                    ChimpValueMod.UpdateChimpValuesFromConfig(configValues);

                    // Применяем изменения в памяти только после загрузки конфигурации
                    ChimpValueMod.ModifyMemory();

                    configLoaded = true;
                }
                catch (Exception ex)
                {
                    MelonLogger.Error($"Failed to load config from {filePath}: {ex.Message}");
                }
            }
            else
            {
                MelonLogger.Warning($"Config file not found for map: {mapName}");
            }

            if (!configLoaded)
            {
                // Если конфигурация не была загружена, устанавливаем дефолтные значения
                MelonLogger.Warning("No config file found, using default values.");
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

            // Передаем словарь defaultValues в метод UpdateChimpValuesFromConfig
            ChimpValueMod.UpdateChimpValuesFromConfig(defaultValues);

            // Обновляем стоимость в золоте
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
                MelonLogger.Error($"Failed to search for file '{fileName}' in directory '{directory}': {ex.Message}");
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
                            MelonLogger.Warning($"Unknown chimp type in config: {split[0]}");
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

    // Патч для getChimpGoldCost
    [HarmonyPatch(typeof(GameData))]
    [HarmonyPatch("getChimpGoldCost")]
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
}