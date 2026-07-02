using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;

namespace Eq2Auras.Core.SelfUpdate
{
    /// The slice of GitHub's "get release by tag" API response the self-updater needs.
    /// Parsed with DataContractJsonSerializer — deliberately NOT JavaScriptSerializer:
    /// referencing System.Web.Extensions breaks the WPF XAML markup compiler (CI-proven).
    [DataContract]
    public sealed class ReleaseManifest
    {
        [DataMember(Name = "tag_name")]
        public string TagName { get; set; }

        [DataMember(Name = "published_at")]
        public string PublishedAt { get; set; }

        [DataMember(Name = "assets")]
        public List<ReleaseAsset> Assets { get; set; }

        public static ReleaseManifest Parse(string json)
        {
            var serializer = new DataContractJsonSerializer(typeof(ReleaseManifest));
            using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(json)))
            {
                var manifest = (ReleaseManifest)serializer.ReadObject(stream);
                if (manifest.Assets == null) manifest.Assets = new List<ReleaseAsset>();
                return manifest;
            }
        }
    }

    [DataContract]
    public sealed class ReleaseAsset
    {
        [DataMember(Name = "name")]
        public string Name { get; set; }

        /// The API asset URL — downloading it with Accept: application/octet-stream
        /// returns the binary (works for private repos, unlike browser_download_url).
        [DataMember(Name = "url")]
        public string ApiUrl { get; set; }
    }
}
