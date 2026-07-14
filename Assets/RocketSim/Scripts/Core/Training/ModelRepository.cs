// -----------------------------------------------------------------------------
// File: Assets/RocketSim/Scripts/Core/Training/ModelRepository.cs
// Purpose: Maps a run id to its expected Unity Resources ONNX model path and
// provides the small existence/load/error-message API used by inference.
// Documentation: Comments in this file use plain language to describe intent,
// so the simulator architecture is easier to understand and maintain.
// -----------------------------------------------------------------------------

using Unity.InferenceEngine;
using UnityEngine;

namespace RocketSim
{
    internal static class ModelRepository
    {
        /// <summary>
        /// Returns the Resources path where a run's ONNX model is expected.
        /// </summary>
        public static string ResourcePath(string runId) => $"Models/{runId}";

        /// <summary>
        /// Returns whether a run id resolves to a loadable ONNX model asset.
        /// </summary>
        public static bool HasModel(string runId) => LoadModel(runId) != null;

        /// <summary>
        /// Loads a run model from Unity Resources for inference mode.
        /// </summary>
        public static ModelAsset LoadModel(string runId) => string.IsNullOrWhiteSpace(runId) ? null : Resources.Load<ModelAsset>(ResourcePath(runId));

        /// <summary>
        /// Builds the error text shown when inference cannot find a run's model.
        /// </summary>
        public static string MissingModelMessage(string runId) => $"[TrainingAreaManager] Model not found in Resources/{ResourcePath(runId)} " + $"— ensure the .onnx is at Assets/RocketSim/Resources/Models/{runId}.onnx";
    }
}
