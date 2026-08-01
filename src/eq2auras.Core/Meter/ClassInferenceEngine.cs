using System.Collections.Generic;

namespace Eq2Auras.Core.Meter
{
    /// The learned name→class map + inference rules (SPEC Part III §Class colors). The disk cache
    /// warm-starts the COLOR (shown immediately); a name is re-read each encounter until THIS encounter
    /// confirms it (first catalog hit) — so a between-fight persona swap is caught, while within a fight
    /// a confirmed combatant is skipped (the read shrinks toward zero). First hit commits (premium procs
    /// win within a call); confident disagreement overrides live + persists; an unknown fight never
    /// demotes. Thread-safe: IsCommitted runs on ACT's UI thread during the snapshot;
    /// Observe/ColorForName/ResetEncounter on the WPF dispatcher thread.
    public sealed class ClassInferenceEngine
    {
        private struct Record { public Subclass Subclass; public FinalClass Final; }

        private readonly object _gate = new object();
        private readonly Dictionary<string, Record> _map =
            new Dictionary<string, Record>(System.StringComparer.OrdinalIgnoreCase);              // the color (warm-started + learned)
        private readonly HashSet<string> _confirmedThisEncounter =
            new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);                         // drives the read-skip; reset each encounter
        private readonly HashSet<string> _dirty =
            new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);

        public bool HasDirty { get { lock (_gate) return _dirty.Count > 0; } }
        public void ClearDirty() { lock (_gate) _dirty.Clear(); }

        /// Reopen every combatant to re-read next encounter (a between-fight persona swap is caught);
        /// the warm-start colors survive. Called by the Plugin at each encounter start.
        public void ResetEncounter() { lock (_gate) _confirmedThisEncounter.Clear(); }

        /// The read-skip predicate (SPEC §Class colors — "committed combatants are skipped"): confirmed
        /// THIS encounter. A warm-started-but-unconfirmed name returns false, so it is still read.
        public bool IsCommitted(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            lock (_gate) return _confirmedThisEncounter.Contains(name);
        }

        /// The color — from this-session evidence OR the warm-start cache — shown immediately.
        public int ColorForName(string name)
        {
            if (string.IsNullOrEmpty(name)) return SubclassColors.Grey;
            lock (_gate)
                return _map.TryGetValue(name, out var rec)
                    ? SubclassColors.ArgbFor(rec.Subclass)
                    : SubclassColors.Grey;
        }

        public void Observe(string name, IReadOnlyList<string> abilityNames)
        {
            if (string.IsNullOrEmpty(name) || abilityNames == null) return;

            bool found = false, foundPremium = false;
            Subclass subclass = Subclass.Unknown;
            FinalClass final = FinalClass.Unknown;
            foreach (var ability in abilityNames)
            {
                if (!ClassSignatures.TryResolve(ability, out var sc, out var fc)) continue;
                bool premium = ClassSignatures.IsPremium(ability);
                if (!found || (premium && !foundPremium))
                {
                    subclass = sc;
                    final = fc;
                    foundPremium = premium;
                    found = true;
                    if (premium) break;   // premium is definitive — stop scanning
                }
            }
            if (found) Commit(name, subclass, final);
        }

        private void Commit(string name, Subclass subclass, FinalClass final)
        {
            lock (_gate)
            {
                _confirmedThisEncounter.Add(name);   // confirmed this encounter → skip the rest of this fight
                if (_map.TryGetValue(name, out var existing) && existing.Subclass == subclass)
                {
                    // Same subclass: correct the enrichment final when a final-specific tell gives a NEW one —
                    // Unknown → known, OR a within-subclass betrayal (Swashbuckler → Brigand). A SHARED re-read
                    // (final Unknown) never downgrades a known final (SPEC §Class colors — the override corrects
                    // final drift).
                    if (final != FinalClass.Unknown && final != existing.Final)
                    {
                        _map[name] = new Record { Subclass = subclass, Final = final };
                        _dirty.Add(name);
                    }
                    return;
                }
                _map[name] = new Record { Subclass = subclass, Final = final };   // new or confident override
                _dirty.Add(name);
            }
        }

        public IReadOnlyList<ClassCacheEntry> Export()
        {
            lock (_gate)
            {
                var list = new List<ClassCacheEntry>(_map.Count);
                foreach (var pair in _map)
                    list.Add(new ClassCacheEntry { Name = pair.Key, Subclass = pair.Value.Subclass, Final = pair.Value.Final });
                return list;
            }
        }

        public void Import(IEnumerable<ClassCacheEntry> entries)
        {
            if (entries == null) return;
            lock (_gate)
                foreach (var entry in entries)
                    if (!string.IsNullOrEmpty(entry.Name) && entry.Subclass != Subclass.Unknown)
                        _map[entry.Name] = new Record { Subclass = entry.Subclass, Final = entry.Final };
            // Color only — NOT confirmed (so this session still reads and can override a persona swap)
            // and NOT dirty (came from disk).
        }
    }
}
