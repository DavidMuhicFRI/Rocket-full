using System;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

namespace RocketSim
{
    /// <summary>Validates and freezes editable session data for one launch.</summary>
    public static class SimulationSessionSnapshotFactory
    {
        public static bool TryCreate(
            SimulationSessionConfig source,
            int revision,
            out SimulationSessionSnapshot snapshot,
            out SessionValidationResult validation)
        {
            validation = SimulationSessionValidator.Validate(source);
            snapshot = null;
            if (!validation.IsValid) return false;

            SimulationSessionConfig frozen = source.DeepCopy();
            string json = JsonUtility.ToJson(frozen);
            snapshot = new SimulationSessionSnapshot(
                frozen,
                Math.Max(1, revision),
                Sha256Hex(json),
                DateTime.UtcNow.ToString("O"));
            return true;
        }

        public static string Sha256Hex(string value)
        {
            using SHA256 sha = SHA256.Create();
            byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(value ?? string.Empty));
            var text = new StringBuilder(hash.Length * 2);
            for (int i = 0; i < hash.Length; i++) text.Append(hash[i].ToString("x2"));
            return text.ToString();
        }
    }
}
