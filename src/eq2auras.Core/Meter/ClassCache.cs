using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;

namespace Eq2Auras.Core.Meter
{
    /// The persisted learned name→class store (SPEC Part III §Class colors, §Settings): its own DCJS
    /// file, eager-loaded at init, flushed with confident diffs at encounter end. `ClassCacheEntry`
    /// is the per-record shape. A missing/corrupt file parses to an empty cache (self-heals).
    [DataContract]
    public sealed class ClassCache
    {
        [DataMember] public List<ClassCacheEntry> Entries { get; set; } = new List<ClassCacheEntry>();

        public static ClassCache Parse(string json)
        {
            try
            {
                var serializer = new DataContractJsonSerializer(typeof(ClassCache));
                using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(json ?? "")))
                {
                    var cache = (ClassCache)serializer.ReadObject(stream) ?? new ClassCache();
                    if (cache.Entries == null) cache.Entries = new List<ClassCacheEntry>();
                    return cache;
                }
            }
            catch
            {
                return new ClassCache();
            }
        }

        public string ToJson()
        {
            var serializer = new DataContractJsonSerializer(typeof(ClassCache));
            using (var stream = new MemoryStream())
            {
                serializer.WriteObject(stream, this);
                return Encoding.UTF8.GetString(stream.ToArray());
            }
        }
    }
}
