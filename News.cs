using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace DMGLauncher;

public record Article(string Title, string Excerpt, DateTime Date, string Link, string? Image);

// Latest posts from duelmonstersgenesis.com through WordPress's public REST API.
public static class News
{
    public const string Site = "https://duelmonstersgenesis.com/";
    const string Api = Site + "wp-json/wp/v2/posts?per_page=10&_embed=wp:featuredmedia&_fields=date,link,title,excerpt,_links,_embedded";

    public static async Task<List<Article>> Latest()
    {
        using var doc = JsonDocument.Parse(await Byond.Http.GetStringAsync(Api));
        return doc.RootElement.EnumerateArray().Select(p => new Article(
            Text(p.GetProperty("title").GetProperty("rendered").GetString()),
            Text(p.GetProperty("excerpt").GetProperty("rendered").GetString()),
            p.GetProperty("date").GetDateTime(),
            p.GetProperty("link").GetString() ?? Site,
            ImageOf(p))).ToList();
    }

    // Site content is untrusted: only the site's own https pages are opened, and images come only from it.
    public static bool IsSiteUrl(string? url) => url != null && url.StartsWith(Site, StringComparison.OrdinalIgnoreCase);

    static string Text(string? html) => WebUtility.HtmlDecode(Regex.Replace(html ?? "", "<[^>]+>", "")).Replace("[…]", "…").Trim();

    // The featured image's "medium_large" size if WordPress made one, else the original.
    static string? ImageOf(JsonElement post)
    {
        if (!post.TryGetProperty("_embedded", out var emb) || !emb.TryGetProperty("wp:featuredmedia", out var media) || media.GetArrayLength() == 0)
            return null;
        var m = media[0];
        string? url = null;
        if (m.TryGetProperty("media_details", out var d) && d.TryGetProperty("sizes", out var sizes))
            foreach (var name in new[] { "medium_large", "large", "medium" })
                if (sizes.TryGetProperty(name, out var size)) { url = size.GetProperty("source_url").GetString(); break; }
        url ??= m.TryGetProperty("source_url", out var src) ? src.GetString() : null;
        return IsSiteUrl(url) ? url : null;
    }
}
