// -----------------------------------------------------------------------------
// File: Assets/RocketSim/Scripts/UI/RightConfigPanel.Environment.cs
// Purpose: Builds the Environment tab: weather presets, wind controls, compass direction, and air-density settings.
// Documentation: Comments in this file use plain language to describe intent,
// so the simulator architecture is easier to understand and maintain.
// -----------------------------------------------------------------------------

using UnityEngine.UIElements;
using UnityEngine;

namespace RocketSim
{
    public partial class RightConfigPanel
    {
        private static readonly WeatherType[] WeatherOptions =
        {
            WeatherType.Clear,
            WeatherType.Cloudy,
            WeatherType.Rainy,
            WeatherType.Windy,
            WeatherType.Stormy,
        };

        /// <summary>
        /// Builds wind, weather, and derived air-density controls for the shared environment config.
        /// </summary>
        void BuildEnvironmentTab(VisualElement c)
        {
            c.Clear();
            BuildWindControls(c);
            BuildWeatherSelector(c);
            BuildEnvironmentReadouts(c);
        }

        /// <summary>
        /// Builds wind enable, speed, gust, and drift-rate controls.
        /// </summary>
        void BuildWindControls(VisualElement root)
        {
            root.Add(UIHelper.SectionLabel("Wind"));
            root.Add(UIHelper.Toggle("Wind Enabled", envConfig.windEnabled, v =>
            {
                envConfig.SetWindEnabled(v);
                BuildEnvironmentTab(root);
            }));

            var windSpeed = UIHelper.Slider("Wind Speed (m/s)", envConfig.windSpeed, 0f, 30f, v =>
            {
                envConfig.SetWindSpeed(v);
                RefreshEnvironmentReadouts(root);
            }, "Sets the steady wind speed applied to the rocket.", 1, " m/s");
            windSpeed.SetEnabled(envConfig.windEnabled);
            root.Add(windSpeed);

            var gustAmplitude = UIHelper.Slider("Gust Speed", envConfig.windGustAmplitude, 0f, 15f, v =>
            {
                envConfig.SetWindGustAmplitude(v);
                RefreshEnvironmentReadouts(root);
            }, "Adds temporary wind-speed changes above and below the steady wind.", 1, " m/s");
            gustAmplitude.SetEnabled(envConfig.windEnabled);
            root.Add(gustAmplitude);

            var windDriftRate = UIHelper.Slider("Direction Drift", envConfig.windChangeRate, 0f, 1f, v =>
            {
                envConfig.SetWindChangeRate(v);
                RefreshEnvironmentReadouts(root);
            }, "Sets how quickly the wind direction wanders over time.", 2, "/s");
            windDriftRate.SetEnabled(envConfig.windEnabled);
            root.Add(windDriftRate);

            var gustFrequency = UIHelper.Slider("Gust Frequency", envConfig.gustFrequencyHz, 0f, 2f, v =>
            {
                envConfig.gustFrequencyHz = v;
            }, "Sets how many gust cycles occur each second.", 2, " Hz");
            gustFrequency.SetEnabled(envConfig.windEnabled);
            root.Add(gustFrequency);

            var randomDirection = UIHelper.Toggle("Random Each Episode", envConfig.randomizeWindDirectionEachEpisode, v =>
            {
                envConfig.randomizeWindDirectionEachEpisode = v;
                BuildEnvironmentTab(root);
            });
            randomDirection.SetEnabled(envConfig.windEnabled);
            root.Add(randomDirection);

            if (!envConfig.randomizeWindDirectionEachEpisode)
            {
                var direction = UIHelper.Slider("Wind Direction", envConfig.windDirectionDeg, 0f, 359f,
                    v => envConfig.windDirectionDeg = v,
                    "Sets the compass direction toward which the wind pushes.", 0, "deg");
                direction.SetEnabled(envConfig.windEnabled);
                root.Add(direction);
                root.Add(BuildWindCompass(envConfig.windDirectionDeg));
            }
        }

        /// <summary>
        /// Builds weather buttons and updates their active styling after selection.
        /// </summary>
        void BuildWeatherSelector(VisualElement root)
        {
            root.Add(UIHelper.SectionLabel("Environment Presets"));

            var weatherRow = new VisualElement();
            weatherRow.AddToClassList("rs-btn-group");

            foreach (WeatherType weather in WeatherOptions)
            {
                var capturedWeather = weather;
                var button = new Button(() =>
                {
                    envConfig.ApplyWeatherPreset(capturedWeather);
                    BuildEnvironmentTab(root);
                }) { text = weather.ToString() };

                button.AddToClassList("rs-weather-btn");
                button.userData = weather;
                weatherRow.Add(button);
            }

            root.Add(weatherRow);
            RefreshWeatherButtons(weatherRow);
            root.Add(BuildGroupDetail(
                "These are convenient wind and aerodynamic-density presets, not a high-fidelity weather simulation."));
        }

        /// <summary>
        /// Builds read-only environment values derived from the current settings.
        /// </summary>
        void BuildEnvironmentReadouts(VisualElement root)
        {
            root.Add(UIHelper.SectionLabel("Derived"));

            root.Add(UIHelper.Slider("Aerodynamic Density Scale", envConfig.airDensityMultiplier, 0.5f, 1.5f, value =>
            {
                envConfig.airDensityMultiplier = value;
                RefreshEnvironmentReadouts(root);
            }, "Domain-randomization scale for aerodynamic drag and fin forces; it is not a literal weather density model.", 2, "x"));

            var densityRow = UIHelper.ReadOnly("Air Density Multiplier", envConfig.AirDensityMultiplier.ToString("F3"));
            densityRow.name = "label-air-density";
            root.Add(densityRow);
        }

        /// <summary>
        /// Updates weather button active styling to match the current environment config.
        /// </summary>
        void RefreshWeatherButtons(VisualElement row)
        {
            foreach (var child in row.Children())
                if (child is Button { userData: WeatherType wt } b)
                    b.EnableInClassList("rs-weather-btn-active", wt == envConfig.weather);
        }

        /// <summary>
        /// Refreshes derived environment labels affected by wind or weather changes.
        /// </summary>
        void RefreshEnvironmentReadouts(VisualElement root)
        {
            UpdateLabelText(root, "label-air-density", envConfig.AirDensityMultiplier.ToString("F3"));
        }

        /// <summary>Shows wind direction without adding a custom drawing dependency.</summary>
        VisualElement BuildWindCompass(float directionDeg)
        {
            float radians = directionDeg * Mathf.Deg2Rad;
            string horizontal = Mathf.Sin(radians) > 0.35f ? "E" : Mathf.Sin(radians) < -0.35f ? "W" : "";
            string vertical = Mathf.Cos(radians) > 0.35f ? "N" : Mathf.Cos(radians) < -0.35f ? "S" : "";
            return UIHelper.ReadOnly("Compass", string.IsNullOrEmpty(vertical + horizontal) ? "N" : vertical + horizontal);
        }


    }
}
