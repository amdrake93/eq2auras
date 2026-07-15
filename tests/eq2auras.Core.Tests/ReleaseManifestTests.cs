using Eq2Auras.Core.SelfUpdate;
using Xunit;

public class ReleaseManifestTests
{
    // Trimmed but shape-faithful GitHub "get release by tag" API response.
    private const string SampleJson = @"{
      ""url"": ""https://api.github.com/repos/amdrake93/eq2auras/releases/230000000"",
      ""tag_name"": ""dev-latest"",
      ""name"": ""dev-latest"",
      ""prerelease"": true,
      ""published_at"": ""2026-07-01T20:33:19Z"",
      ""assets"": [
        {
          ""url"": ""https://api.github.com/repos/amdrake93/eq2auras/releases/assets/111"",
          ""name"": ""eq2auras.Core.dll"",
          ""content_type"": ""application/x-msdownload"",
          ""size"": 12345,
          ""browser_download_url"": ""https://github.com/amdrake93/eq2auras/releases/download/dev-latest/eq2auras.Core.dll""
        },
        {
          ""url"": ""https://api.github.com/repos/amdrake93/eq2auras/releases/assets/222"",
          ""name"": ""eq2auras.dll"",
          ""content_type"": ""application/x-msdownload"",
          ""size"": 67890,
          ""browser_download_url"": ""https://github.com/amdrake93/eq2auras/releases/download/dev-latest/eq2auras.dll""
        }
      ]
    }";

    [Fact]
    public void Parse_extracts_tag_publishedAt_and_assets()
    {
        var manifest = ReleaseManifest.Parse(SampleJson);

        Assert.Equal("dev-latest", manifest.TagName);
        Assert.Equal("2026-07-01T20:33:19Z", manifest.PublishedAt);
        Assert.Equal(2, manifest.Assets.Count);
        Assert.Equal("eq2auras.Core.dll", manifest.Assets[0].Name);
        Assert.Equal("https://api.github.com/repos/amdrake93/eq2auras/releases/assets/111", manifest.Assets[0].ApiUrl);
        Assert.Equal("eq2auras.dll", manifest.Assets[1].Name);
    }

    [Fact]
    public void Parse_reads_release_name_and_asset_browser_download_url()
    {
        var manifest = ReleaseManifest.Parse(SampleJson);

        Assert.Equal("dev-latest", manifest.Name);
        Assert.Equal(
            "https://github.com/amdrake93/eq2auras/releases/download/dev-latest/eq2auras.Core.dll",
            manifest.Assets[0].BrowserDownloadUrl);
    }

    [Fact]
    public void Parse_tolerates_missing_assets()
    {
        var manifest = ReleaseManifest.Parse(@"{ ""tag_name"": ""x"", ""published_at"": ""y"" }");

        Assert.Equal("x", manifest.TagName);
        Assert.Empty(manifest.Assets);
    }
}
