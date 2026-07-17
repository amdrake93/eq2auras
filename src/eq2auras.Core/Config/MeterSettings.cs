using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;

namespace Eq2Auras.Core.Config
{
    /// Parse Meter module settings (SPEC Part III §Settings). Enabled defaults false
    /// (0-value rule): the meter is opt-in. Enabled is a show/hide toggle over the
    /// persisted Windows list, which persists independently — disabling keeps the
    /// configs, enabling restores exactly them.
    [DataContract]
    public sealed class MeterSettings
    {
        [DataMember(Name = "enabled")]
        public bool Enabled { get; set; }

        // Legacy slice-1 single-window fields. Retained only to migrate a pre-multi-window
        // settings.json into Windows[0] (same shape as Settings' flat->panels migration);
        // Normalize consumes then clears them, so new files carry them null/false.
        [DataMember(Name = "left")]
        public double? Left { get; set; }
        [DataMember(Name = "top")]
        public double? Top { get; set; }
        [DataMember(Name = "metricKey")]
        public string MetricKey { get; set; }
        [DataMember(Name = "locked")]
        public bool Locked { get; set; }

        // Initializer covers the real-ctor paths (Settings.Parse's corrupt-file catch and
        // SettingsStore.Load's no-file case return `new Settings()` WITHOUT Normalize, same
        // as Panels/PaletteArgb). DCJS skips initializers, so the deserialize path still
        // arrives null and is migrated/seeded in Normalize.
        [DataMember(Name = "windows")]
        public List<MeterWindowConfig> Windows { get; set; } = new List<MeterWindowConfig>();

        /// DCJS skips initializers, so Windows may be null. Migrates a legacy single-window
        /// file into one config, drops null entries, and seeds one default window when the
        /// meter is enabled but has none (SPEC Part III §Multiple windows — an enabled meter
        /// always has at least one window; the first-ever enable seeds a default).
        public void Normalize()
        {
            if (Windows == null)
            {
                Windows = new List<MeterWindowConfig>();
                if (Left.HasValue || Top.HasValue || MetricKey != null || Locked)
                {
                    Windows.Add(new MeterWindowConfig
                    {
                        Left = Left,
                        Top = Top,
                        MetricKey = MetricKey,
                        Locked = Locked,
                    });
                }
            }

            // Legacy flat fields are consumed into Windows; keep new files clean.
            Left = null;
            Top = null;
            MetricKey = null;
            Locked = false;

            Windows = Windows.Where(w => w != null).ToList();

            if (Enabled && Windows.Count == 0) Windows.Add(new MeterWindowConfig());
        }
    }
}
