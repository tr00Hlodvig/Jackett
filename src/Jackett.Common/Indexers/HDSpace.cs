using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using AngleSharp.Html.Parser;
using Jackett.Common.Extensions;
using Jackett.Common.Models;
using Jackett.Common.Models.IndexerConfig;
using Jackett.Common.Services.Interfaces;
using Jackett.Common.Utils;
using Newtonsoft.Json.Linq;
using NLog;
using static Jackett.Common.Models.IndexerConfig.ConfigurationData;
using WebClient = Jackett.Common.Utils.Clients.WebClient;

namespace Jackett.Common.Indexers
{
    [ExcludeFromCodeCoverage]
    public class HDSpace : IndexerBase
    {
        public override string Id => "hdspace";
        public override string Name => "HD-Space";
        public override string Description => "Sharing The Universe";
        public override string SiteLink { get; protected set; } = "https://hd-space.org/";
        public override string Language => "en-US";
        public override string Type => "private";

        public override TorznabCapabilities TorznabCaps => SetCapabilities();

        private string LoginUrl => SiteLink + "index.php?page=login";
        private string SearchUrl => SiteLink + "index.php?page=torrents";

        private new ConfigurationDataBasicLogin configData => (ConfigurationDataBasicLogin)base.configData;

        public HDSpace(IIndexerConfigurationService configService, WebClient wc, Logger l, IProtectionService ps,
            ICacheService cs)
            : base(configService: configService,
                   client: wc,
                   logger: l,
                   p: ps,
                   cacheService: cs,
                   configData: new ConfigurationDataBasicLogin())
        {
            configData.AddDynamic("freeleech", new BoolConfigurationItem("Filter freeleech only") { Value = false });
            configData.AddDynamic("flaresolverr", new DisplayInfoConfigurationItem("FlareSolverr", "This site may use Cloudflare DDoS Protection, therefore Jackett requires <a href=\"https://github.com/Jackett/Jackett#configuring-flaresolverr\" target=\"_blank\">FlareSolverr</a> to access it."));
        }

        private TorznabCapabilities SetCapabilities()
        {
            var caps = new TorznabCapabilities
            {
                TvSearchParams = new List<TvSearchParam>
                {
                    TvSearchParam.Q, TvSearchParam.Season, TvSearchParam.Ep, TvSearchParam.ImdbId
                },
                MovieSearchParams = new List<MovieSearchParam>
                {
                    MovieSearchParam.Q, MovieSearchParam.ImdbId
                },
                MusicSearchParams = new List<MusicSearchParam>
                {
                    MusicSearchParam.Q
                }
            };

            caps.Categories.AddCategoryMapping(15, TorznabCatType.MoviesBluRay, "Movie / Blu-ray");
            caps.Categories.AddCategoryMapping(40, TorznabCatType.MoviesHD, "Movie / Remux");
            caps.Categories.AddCategoryMapping(18, TorznabCatType.MoviesHD, "Movie / 720p");
            caps.Categories.AddCategoryMapping(19, TorznabCatType.MoviesHD, "Movie / 1080p");
            caps.Categories.AddCategoryMapping(46, TorznabCatType.MoviesUHD, "Movie / 2160p");
            caps.Categories.AddCategoryMapping(21, TorznabCatType.TVHD, "TV Show / 720p HDTV");
            caps.Categories.AddCategoryMapping(22, TorznabCatType.TVHD, "TV Show / 1080p HDTV");
            caps.Categories.AddCategoryMapping(45, TorznabCatType.TVUHD, "TV Show / 2160p HDTV");
            caps.Categories.AddCategoryMapping(24, TorznabCatType.TVDocumentary, "Documentary / 720p");
            caps.Categories.AddCategoryMapping(25, TorznabCatType.TVDocumentary, "Documentary / 1080p");
            caps.Categories.AddCategoryMapping(47, TorznabCatType.TVDocumentary, "Documentary / 2160p");
            caps.Categories.AddCategoryMapping(27, TorznabCatType.TVAnime, "Animation / 720p");
            caps.Categories.AddCategoryMapping(28, TorznabCatType.TVAnime, "Animation / 1080p");
            caps.Categories.AddCategoryMapping(48, TorznabCatType.TVAnime, "Animation / 2160p");
            caps.Categories.AddCategoryMapping(30, TorznabCatType.AudioLossless, "Music / HQ Audio");
            caps.Categories.AddCategoryMapping(31, TorznabCatType.AudioVideo, "Music / Videos");
            caps.Categories.AddCategoryMapping(33, TorznabCatType.XXX, "XXX / 720p");
            caps.Categories.AddCategoryMapping(34, TorznabCatType.XXX, "XXX / 1080p");
            caps.Categories.AddCategoryMapping(49, TorznabCatType.XXX, "XXX / 2160p");
            caps.Categories.AddCategoryMapping(36, TorznabCatType.MoviesOther, "Trailers");
            caps.Categories.AddCategoryMapping(37, TorznabCatType.PC, "Software");
            caps.Categories.AddCategoryMapping(38, TorznabCatType.Other, "Others");
            caps.Categories.AddCategoryMapping(41, TorznabCatType.MoviesUHD, "Movie / 4K UHD");

            return caps;
        }

        public override async Task<IndexerConfigurationStatus> ApplyConfiguration(JToken configJson)
        {
            LoadValuesFromJson(configJson);

            var loginPage = await RequestWithCookiesAsync(LoginUrl, string.Empty);

            var pairs = new Dictionary<string, string>
            {
                { "uid", configData.Username.Value },
                { "pwd", configData.Password.Value }
            };

            // Send Post
            var response = await RequestLoginAndFollowRedirect(LoginUrl, pairs, loginPage.Cookies, true, referer: LoginUrl);

            await ConfigureIfOK(response.Cookies, response.ContentString?.Contains("logout.php") == true || response.ContentString?.Contains("Rank: Parked") == true, () =>
            {
                var parser = new HtmlParser();
                var dom = parser.ParseDocument(response.ContentString);
                var errorMessages = dom
                   .QuerySelectorAll("table.lista td.lista span[style*=\"#FF0000\"], table.lista td.header:contains(\"login attempts\")")
                   .Select(r => r.TextContent.Trim())
                   .Where(m => m.IsNotNullOrWhiteSpace())
                   .ToArray();

                throw new ExceptionWithConfigData(errorMessages.Any() ? errorMessages.Join(" ") : "Unknown error message, please report.", configData);
            });

            return IndexerConfigurationStatus.RequiresTesting;
        }

        protected override async Task<IEnumerable<ReleaseInfo>> PerformQuery(TorznabQuery query)
        {
            var releases = new List<ReleaseInfo>();
            var queryCollection = new NameValueCollection
            {
                {"active", "0"},
                {"category", string.Join(";", MapTorznabCapsToTrackers(query))}
            };

            if (query.IsImdbQuery)
            {
                queryCollection.Set("options", "2");
                queryCollection.Set("search", query.ImdbIDShort);
            }
            else
            {
                queryCollection.Set("options", "0");
                queryCollection.Set("search", query.GetQueryString().Replace(".", " "));
            }

            var response = await RequestWithCookiesAndRetryAsync($"{SearchUrl}&{queryCollection.GetQueryString()}");

            try
            {
                var resultParser = new HtmlParser();
                var searchResultDocument = resultParser.ParseDocument(response.ContentString);
                var rows = searchResultDocument.QuerySelectorAll("table.lista > tbody > tr");

                foreach (var row in rows)
                {
                    // this tracker has horrible markup, find the result rows by looking for the style tag before each one
                    var prev = row.PreviousElementSibling;
                    if (prev == null || !string.Equals(prev.NodeName, "style", StringComparison.OrdinalIgnoreCase))
                        continue;

                    var release = new ReleaseInfo
                    {
                        MinimumRatio = 1,
                        MinimumSeedTime = 86400 // 24 hours
                    };

                    if (row.QuerySelector("img[title=\"FreeLeech\"]") != null)
                        release.DownloadVolumeFactor = 0;
                    else if (row.QuerySelector("img[src=\"images/sf.png\"]") != null) // side freeleech
                        release.DownloadVolumeFactor = 0;
                    else if (row.QuerySelector("img[title=\"Half FreeLeech\"]") != null)
                        release.DownloadVolumeFactor = 0.5;
                    else
                        release.DownloadVolumeFactor = 1;
                    if (((BoolConfigurationItem)configData.GetDynamic("freeleech")).Value &&
                        release.DownloadVolumeFactor != 0)
                        continue;
                    release.UploadVolumeFactor = 1;

                    var qLink = row.Children[1].FirstElementChild;
                    release.Title = qLink.TextContent.Trim();
                    release.Details = new Uri(SiteLink + qLink.GetAttribute("href"));
                    release.Guid = release.Details;
                    release.Link = new Uri(SiteLink + row.Children[3].FirstElementChild.GetAttribute("href"));

                    var torrentTitle = ParseUtil.GetArgumentFromQueryString(release.Link.ToString(), "f")?.Replace(".torrent", "").Trim();
                    if (!string.IsNullOrWhiteSpace(torrentTitle))
                        release.Title = WebUtility.HtmlDecode(torrentTitle);

                    var qGenres = row.QuerySelector("span[style=\"color: #000000 \"]");
                    var description = "";
                    if (qGenres != null)
                        description = qGenres.TextContent.Split('\xA0').Last().Replace(" ", "");

                    var imdbLink = row.Children[1].QuerySelector("a[href*=imdb]");
                    if (imdbLink != null)
                        release.Imdb = ParseUtil.GetImdbID(imdbLink.GetAttribute("href").Split('/').Last());

                    var dateStr = row.Children[4].TextContent.Trim();
                    //"July 11, 2015, 13:34:09", "Today|Yesterday at 20:04:23"
                    release.PublishDate = DateTimeUtil.FromUnknown(dateStr);
                    var sizeStr = row.Children[5].TextContent;
                    release.Size = ParseUtil.GetBytes(sizeStr);
                    release.Seeders = ParseUtil.CoerceInt(row.Children[7].TextContent);
                    release.Peers = ParseUtil.CoerceInt(row.Children[8].TextContent) + release.Seeders;
                    var grabs = row.QuerySelector("td:nth-child(10)").TextContent;
                    grabs = grabs.Replace("---", "0");
                    release.Grabs = ParseUtil.CoerceInt(grabs);

                    var categoryLink = row.QuerySelector("a[href^=\"index.php?page=torrents&category=\"]").GetAttribute("href");
                    var cat = ParseUtil.GetArgumentFromQueryString(categoryLink, "category");
                    release.Category = MapTrackerCatToNewznab(cat);

                    release.Description = description;
                    if (release.Genres == null)
                        release.Genres = new List<string>();
                    release.Genres = release.Genres.Union(description.Split(',')).ToList();

                    releases.Add(release);
                }
            }
            catch (Exception ex)
            {
                OnParseError(response.ContentString, ex);
            }

            return releases;
        }
    }
}
