// -----------------------------------------------------------------------------
// File: Assets/RocketSim/Scripts/Core/Hardware/VehiclePresetRepository.cs
// Purpose: Persists any number of named user vehicle presets without coupling
// them to task, reward, trainer, environment, or telemetry configuration.
// -----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace RocketSim
{
    /// <summary>A named, complete snapshot of vehicle hardware configuration.</summary>
    [Serializable]
    public sealed class UserVehiclePreset
    {
        public string name;
        public RocketPartsConfig vehicle;
    }

    [Serializable]
    internal sealed class UserVehiclePresetCollection
    {
        public List<UserVehiclePreset> presets = new();
    }

    /// <summary>
    /// Owns the user preset catalog stored in Unity's persistent-data folder.
    /// Built-in presets are intentionally kept in code and cannot be overwritten
    /// or deleted through this repository.
    /// </summary>
    public static class VehiclePresetRepository
    {
        public const string Falcon9BuiltInName = "Falcon 9";
        public const string SimpleBuiltInName = "Simple";
        public const int MaximumNameLength = 64;

        static string CatalogPath => Path.Combine(
            Application.persistentDataPath,
            "VehiclePresets.json");

        /// <summary>Returns all valid user presets sorted by name.</summary>
        public static IReadOnlyList<UserVehiclePreset> LoadAll(out string error)
        {
            if (!TryReadCatalog(out UserVehiclePresetCollection catalog, out error))
                return Array.Empty<UserVehiclePreset>();

            catalog.presets.Sort((left, right) =>
                StringComparer.OrdinalIgnoreCase.Compare(left.name, right.name));
            return catalog.presets;
        }

        /// <summary>
        /// Saves a new named vehicle or replaces an existing user vehicle with
        /// the same name. Returns whether an existing entry was overwritten.
        /// </summary>
        public static bool TrySave(
            string requestedName,
            RocketPartsConfig vehicle,
            out string normalizedName,
            out bool overwritten,
            out string error)
        {
            normalizedName = string.Empty;
            overwritten = false;
            error = string.Empty;

            if (!TryNormalizeName(requestedName, out normalizedName, out error))
                return false;
            if (IsBuiltInName(normalizedName))
            {
                error = $"'{normalizedName}' is a built-in preset and cannot be overwritten.";
                return false;
            }
            if (vehicle == null)
            {
                error = "There is no vehicle configuration to save.";
                return false;
            }
            if (!TryReadCatalog(out UserVehiclePresetCollection catalog, out error))
                return false;

            RocketPartsConfig snapshot = CloneVehicle(vehicle);
            snapshot.hardwarePreset = RocketHardwarePreset.Custom;
            string savedName = normalizedName;

            int existingIndex = catalog.presets.FindIndex(entry =>
                string.Equals(entry.name, savedName, StringComparison.OrdinalIgnoreCase));
            var saved = new UserVehiclePreset
            {
                name = savedName,
                vehicle = snapshot
            };

            if (existingIndex >= 0)
            {
                catalog.presets[existingIndex] = saved;
                overwritten = true;
            }
            else
            {
                catalog.presets.Add(saved);
            }

            return TryWriteCatalog(catalog, out error);
        }

        /// <summary>Loads one named preset as an independent mutable config.</summary>
        public static bool TryLoad(string requestedName, out RocketPartsConfig vehicle, out string error)
        {
            vehicle = null;
            if (!TryNormalizeName(requestedName, out string normalizedName, out error))
                return false;
            if (!TryReadCatalog(out UserVehiclePresetCollection catalog, out error))
                return false;

            UserVehiclePreset saved = catalog.presets.Find(entry =>
                string.Equals(entry.name, normalizedName, StringComparison.OrdinalIgnoreCase));
            if (saved?.vehicle == null)
            {
                error = $"Vehicle preset '{normalizedName}' was not found.";
                return false;
            }

            vehicle = CloneVehicle(saved.vehicle);
            vehicle.hardwarePreset = RocketHardwarePreset.Custom;
            vehicle.ClampPhysicalRanges();
            return true;
        }

        /// <summary>Deletes a user preset; built-in names are always rejected.</summary>
        public static bool TryDelete(string requestedName, out string error)
        {
            if (!TryNormalizeName(requestedName, out string normalizedName, out error))
                return false;
            if (IsBuiltInName(normalizedName))
            {
                error = $"'{normalizedName}' is a built-in preset and cannot be deleted.";
                return false;
            }
            if (!TryReadCatalog(out UserVehiclePresetCollection catalog, out error))
                return false;

            int removed = catalog.presets.RemoveAll(entry =>
                string.Equals(entry.name, normalizedName, StringComparison.OrdinalIgnoreCase));
            if (removed == 0)
            {
                error = $"Vehicle preset '{normalizedName}' was not found.";
                return false;
            }

            return TryWriteCatalog(catalog, out error);
        }

        /// <summary>Validates and trims a user-facing preset name.</summary>
        public static bool TryNormalizeName(string requestedName, out string normalizedName, out string error)
        {
            normalizedName = requestedName?.Trim() ?? string.Empty;
            error = string.Empty;

            if (normalizedName.Length == 0)
            {
                error = "Enter a name for the vehicle preset.";
                return false;
            }
            if (normalizedName.Length > MaximumNameLength)
            {
                error = $"Vehicle preset names can contain at most {MaximumNameLength} characters.";
                return false;
            }
            for (int i = 0; i < normalizedName.Length; i++)
            {
                if (char.IsControl(normalizedName[i]))
                {
                    error = "Vehicle preset names cannot contain control characters.";
                    return false;
                }
            }

            return true;
        }

        public static bool IsBuiltInName(string name) =>
            string.Equals(name?.Trim(), Falcon9BuiltInName, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(name?.Trim(), SimpleBuiltInName, StringComparison.OrdinalIgnoreCase);

        static bool TryReadCatalog(out UserVehiclePresetCollection catalog, out string error)
        {
            catalog = new UserVehiclePresetCollection();
            error = string.Empty;
            if (!File.Exists(CatalogPath))
                return true;

            try
            {
                catalog = JsonUtility.FromJson<UserVehiclePresetCollection>(File.ReadAllText(CatalogPath));
                if (catalog?.presets == null)
                    throw new InvalidDataException("The preset catalog has no preset list.");

                catalog.presets.RemoveAll(entry =>
                    entry == null || entry.vehicle == null ||
                    !TryNormalizeName(entry.name, out _, out _));
                return true;
            }
            catch (Exception ex)
            {
                catalog = new UserVehiclePresetCollection();
                error = $"Could not read vehicle presets: {ex.Message}";
                return false;
            }
        }

        static bool TryWriteCatalog(UserVehiclePresetCollection catalog, out string error)
        {
            error = string.Empty;
            string temporaryPath = CatalogPath + ".tmp";

            try
            {
                string directory = Path.GetDirectoryName(CatalogPath);
                if (!string.IsNullOrEmpty(directory))
                    Directory.CreateDirectory(directory);

                catalog.presets.Sort((left, right) =>
                    StringComparer.OrdinalIgnoreCase.Compare(left.name, right.name));
                File.WriteAllText(temporaryPath, JsonUtility.ToJson(catalog, true));

                if (File.Exists(CatalogPath))
                {
                    try
                    {
                        File.Replace(temporaryPath, CatalogPath, null);
                    }
                    catch (Exception ex) when (
                        ex is PlatformNotSupportedException || ex is IOException)
                    {
                        File.Copy(temporaryPath, CatalogPath, true);
                        File.Delete(temporaryPath);
                    }
                }
                else
                {
                    File.Move(temporaryPath, CatalogPath);
                }

                return true;
            }
            catch (Exception ex)
            {
                error = $"Could not save vehicle presets: {ex.Message}";
                try
                {
                    if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
                }
                catch
                {
                    // Preserve the original write error; a stale temporary file
                    // can safely be replaced by the next successful save.
                }
                return false;
            }
        }

        static RocketPartsConfig CloneVehicle(RocketPartsConfig source) =>
            JsonUtility.FromJson<RocketPartsConfig>(JsonUtility.ToJson(source));
    }
}
