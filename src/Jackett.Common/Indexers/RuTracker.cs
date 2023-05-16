using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using Jackett.Common.Models;
using Jackett.Common.Models.IndexerConfig;
using Jackett.Common.Models.IndexerConfig.Bespoke;
using Jackett.Common.Services.Interfaces;
using Jackett.Common.Utils;
using Jackett.Common.Utils.Clients;
using Newtonsoft.Json.Linq;
using NLog;

namespace Jackett.Common.Indexers
{
    [ExcludeFromCodeCoverage]
    public class RuTracker : IndexerBase
    {
        public override string Id => "rutracker";
        public override string Name => "RuTracker";
        public override string Description => "RuTracker is a Semi-Private Russian torrent site with a thriving file-sharing community";
        public override string SiteLink { get; protected set; } = "https://rutracker.org/";
        public override string[] AlternativeSiteLinks => new[]
        {
            "https://rutracker.org/",
            "https://rutracker.net/",
            "https://rutracker.nl/"
        };
        public override Encoding Encoding => Encoding.GetEncoding("windows-1251");
        public override string Language => "ru-RU";
        public override string Type => "semi-private";

        public override TorznabCapabilities TorznabCaps => SetCapabilities();

        private new ConfigurationDataRutracker configData => (ConfigurationDataRutracker)base.configData;

        private readonly TitleParser _titleParser = new TitleParser();
        private string LoginUrl => SiteLink + "forum/login.php";
        private string SearchUrl => SiteLink + "forum/tracker.php";

        private string _capSid;
        private string _capCodeField;

        public RuTracker(IIndexerConfigurationService configService, WebClient wc, Logger l, IProtectionService ps, ICacheService cs)
            : base(configService: configService,
                   client: wc,
                   logger: l,
                   p: ps,
                   cacheService: cs,
                   configData: new ConfigurationDataRutracker())
        {
        }

        private TorznabCapabilities SetCapabilities()
        {
            var caps = new TorznabCapabilities
            {
                TvSearchParams = new List<TvSearchParam>
                {
                    TvSearchParam.Q, TvSearchParam.Season, TvSearchParam.Ep
                },
                MovieSearchParams = new List<MovieSearchParam>
                {
                    MovieSearchParam.Q
                },
                MusicSearchParams = new List<MusicSearchParam>
                {
                    MusicSearchParam.Q
                },
                BookSearchParams = new List<BookSearchParam>
                {
                    BookSearchParam.Q
                },
                SupportsRawSearch = true
            };

            // note: when refreshing the categories use the tracker.php page and NOT the search.php page!
            caps.Categories.AddCategoryMapping(22, TorznabCatType.Movies, "Наше кино");
            caps.Categories.AddCategoryMapping(941, TorznabCatType.Movies, "|- Кино СССР");
            caps.Categories.AddCategoryMapping(1666, TorznabCatType.Movies, "|- Детские отечественные фильмы");
            caps.Categories.AddCategoryMapping(376, TorznabCatType.Movies, "|- Авторские дебюты");
            caps.Categories.AddCategoryMapping(106, TorznabCatType.Movies, "|- Фильмы России и СССР на национальных языках [без перевода]");
            caps.Categories.AddCategoryMapping(7, TorznabCatType.MoviesForeign, "Зарубежное кино");
            caps.Categories.AddCategoryMapping(187, TorznabCatType.MoviesForeign, "|- Классика мирового кинематографа");
            caps.Categories.AddCategoryMapping(2090, TorznabCatType.MoviesForeign, "|- Фильмы до 1990 года");
            caps.Categories.AddCategoryMapping(2221, TorznabCatType.MoviesForeign, "|- Фильмы 1991-2000");
            caps.Categories.AddCategoryMapping(2091, TorznabCatType.MoviesForeign, "|- Фильмы 2001-2005");
            caps.Categories.AddCategoryMapping(2092, TorznabCatType.MoviesForeign, "|- Фильмы 2006-2010");
            caps.Categories.AddCategoryMapping(2093, TorznabCatType.MoviesForeign, "|- Фильмы 2011-2015");
            caps.Categories.AddCategoryMapping(2200, TorznabCatType.MoviesForeign, "|- Фильмы 2016-2020");
            caps.Categories.AddCategoryMapping(1950, TorznabCatType.MoviesForeign, "|- Фильмы 2021-2022");
            caps.Categories.AddCategoryMapping(252, TorznabCatType.MoviesForeign, "|- Фильмы 2023");
            caps.Categories.AddCategoryMapping(2540, TorznabCatType.MoviesForeign, "|- Фильмы Ближнего Зарубежья");
            caps.Categories.AddCategoryMapping(934, TorznabCatType.MoviesForeign, "|- Азиатские фильмы");
            caps.Categories.AddCategoryMapping(505, TorznabCatType.MoviesForeign, "|- Индийское кино");
            caps.Categories.AddCategoryMapping(212, TorznabCatType.MoviesForeign, "|- Сборники фильмов");
            caps.Categories.AddCategoryMapping(2459, TorznabCatType.MoviesForeign, "|- Короткий метр");
            caps.Categories.AddCategoryMapping(1235, TorznabCatType.MoviesForeign, "|- Грайндхаус");
            caps.Categories.AddCategoryMapping(166, TorznabCatType.MoviesForeign, "|- Зарубежные фильмы без перевода");
            caps.Categories.AddCategoryMapping(185, TorznabCatType.Audio, "|- Звуковые дорожки и Переводы");
            caps.Categories.AddCategoryMapping(124, TorznabCatType.MoviesOther, "Арт-хаус и авторское кино");
            caps.Categories.AddCategoryMapping(1543, TorznabCatType.MoviesOther, "|- Короткий метр (Арт-хаус и авторское кино)");
            caps.Categories.AddCategoryMapping(709, TorznabCatType.MoviesOther, "|- Документальные фильмы (Арт-хаус и авторское кино)");
            caps.Categories.AddCategoryMapping(1577, TorznabCatType.MoviesOther, "|- Анимация (Арт-хаус и авторское кино)");
            caps.Categories.AddCategoryMapping(511, TorznabCatType.TVOther, "Театр");
            caps.Categories.AddCategoryMapping(93, TorznabCatType.MoviesDVD, "DVD Video");
            caps.Categories.AddCategoryMapping(905, TorznabCatType.MoviesDVD, "|- Классика мирового кинематографа (DVD Video)");
            caps.Categories.AddCategoryMapping(101, TorznabCatType.MoviesDVD, "|- Зарубежное кино (DVD Video)");
            caps.Categories.AddCategoryMapping(100, TorznabCatType.MoviesDVD, "|- Наше кино (DVD Video)");
            caps.Categories.AddCategoryMapping(877, TorznabCatType.MoviesDVD, "|- Фильмы Ближнего Зарубежья (DVD Video)");
            caps.Categories.AddCategoryMapping(1576, TorznabCatType.MoviesDVD, "|- Азиатские фильмы (DVD Video)");
            caps.Categories.AddCategoryMapping(572, TorznabCatType.MoviesDVD, "|- Арт-хаус и авторское кино (DVD Video)");
            caps.Categories.AddCategoryMapping(2220, TorznabCatType.MoviesDVD, "|- Индийское кино (DVD Video)");
            caps.Categories.AddCategoryMapping(1670, TorznabCatType.MoviesDVD, "|- Грайндхаус (DVD Video)");
            caps.Categories.AddCategoryMapping(2198, TorznabCatType.MoviesHD, "HD Video");
            caps.Categories.AddCategoryMapping(1457, TorznabCatType.MoviesUHD, "|- UHD Video");
            caps.Categories.AddCategoryMapping(2199, TorznabCatType.MoviesHD, "|- Классика мирового кинематографа (HD Video)");
            caps.Categories.AddCategoryMapping(313, TorznabCatType.MoviesHD, "|- Зарубежное кино (HD Video)");
            caps.Categories.AddCategoryMapping(312, TorznabCatType.MoviesHD, "|- Наше кино (HD Video)");
            caps.Categories.AddCategoryMapping(1247, TorznabCatType.MoviesHD, "|- Фильмы Ближнего Зарубежья (HD Video)");
            caps.Categories.AddCategoryMapping(2201, TorznabCatType.MoviesHD, "|- Азиатские фильмы (HD Video)");
            caps.Categories.AddCategoryMapping(2339, TorznabCatType.MoviesHD, "|- Арт-хаус и авторское кино (HD Video)");
            caps.Categories.AddCategoryMapping(140, TorznabCatType.MoviesHD, "|- Индийское кино (HD Video)");
            caps.Categories.AddCategoryMapping(194, TorznabCatType.MoviesHD, "|- Грайндхаус (HD Video)");
            caps.Categories.AddCategoryMapping(352, TorznabCatType.Movies3D, "3D/Стерео Кино, Видео, TV и Спорт");
            caps.Categories.AddCategoryMapping(549, TorznabCatType.Movies3D, "|- 3D Кинофильмы");
            caps.Categories.AddCategoryMapping(1213, TorznabCatType.Movies3D, "|- 3D Мультфильмы");
            caps.Categories.AddCategoryMapping(2109, TorznabCatType.Movies3D, "|- 3D Документальные фильмы");
            caps.Categories.AddCategoryMapping(514, TorznabCatType.Movies3D, "|- 3D Спорт");
            caps.Categories.AddCategoryMapping(2097, TorznabCatType.Movies3D, "|- 3D Ролики, Музыкальное видео, Трейлеры к фильмам");
            caps.Categories.AddCategoryMapping(4, TorznabCatType.Movies, "Мультфильмы");
            caps.Categories.AddCategoryMapping(84, TorznabCatType.MoviesUHD, "|- Мультфильмы (UHD Video)");
            caps.Categories.AddCategoryMapping(2343, TorznabCatType.MoviesHD, "|- Отечественные мультфильмы (HD Video)");
            caps.Categories.AddCategoryMapping(930, TorznabCatType.MoviesHD, "|- Иностранные мультфильмы (HD Video)");
            caps.Categories.AddCategoryMapping(2365, TorznabCatType.MoviesHD, "|- Иностранные короткометражные мультфильмы (HD Video)");
            caps.Categories.AddCategoryMapping(1900, TorznabCatType.MoviesDVD, "|- Отечественные мультфильмы (DVD)");
            caps.Categories.AddCategoryMapping(2258, TorznabCatType.MoviesDVD, "|- Иностранные короткометражные мультфильмы (DVD)");
            caps.Categories.AddCategoryMapping(521, TorznabCatType.MoviesDVD, "|- Иностранные мультфильмы (DVD)");
            caps.Categories.AddCategoryMapping(208, TorznabCatType.Movies, "|- Отечественные мультфильмы");
            caps.Categories.AddCategoryMapping(539, TorznabCatType.Movies, "|- Отечественные полнометражные мультфильмы");
            caps.Categories.AddCategoryMapping(209, TorznabCatType.MoviesForeign, "|- Иностранные мультфильмы");
            caps.Categories.AddCategoryMapping(484, TorznabCatType.MoviesForeign, "|- Иностранные короткометражные мультфильмы");
            caps.Categories.AddCategoryMapping(822, TorznabCatType.Movies, "|- Сборники мультфильмов");
            caps.Categories.AddCategoryMapping(181, TorznabCatType.Movies, "|- Мультфильмы без перевода");
            caps.Categories.AddCategoryMapping(921, TorznabCatType.TV, "Мультсериалы");
            caps.Categories.AddCategoryMapping(815, TorznabCatType.TVSD, "|- Мультсериалы (SD Video)");
            caps.Categories.AddCategoryMapping(816, TorznabCatType.TVHD, "|- Мультсериалы (DVD Video)");
            caps.Categories.AddCategoryMapping(1460, TorznabCatType.TVHD, "|- Мультсериалы (HD Video)");
            caps.Categories.AddCategoryMapping(33, TorznabCatType.TVAnime, "Аниме");
            caps.Categories.AddCategoryMapping(1106, TorznabCatType.TVAnime, "|- Онгоинги (HD Video)");
            caps.Categories.AddCategoryMapping(1105, TorznabCatType.TVAnime, "|- Аниме (HD Video)");
            caps.Categories.AddCategoryMapping(599, TorznabCatType.TVAnime, "|- Аниме (DVD)");
            caps.Categories.AddCategoryMapping(1389, TorznabCatType.TVAnime, "|- Аниме (основной подраздел)");
            caps.Categories.AddCategoryMapping(1391, TorznabCatType.TVAnime, "|- Аниме (плеерный подраздел)");
            caps.Categories.AddCategoryMapping(2491, TorznabCatType.TVAnime, "|- Аниме (QC подраздел)");
            caps.Categories.AddCategoryMapping(2544, TorznabCatType.TVAnime, "|- Ван-Пис");
            caps.Categories.AddCategoryMapping(1642, TorznabCatType.TVAnime, "|- Гандам");
            caps.Categories.AddCategoryMapping(1390, TorznabCatType.TVAnime, "|- Наруто");
            caps.Categories.AddCategoryMapping(404, TorznabCatType.TVAnime, "|- Покемоны");
            caps.Categories.AddCategoryMapping(893, TorznabCatType.TVAnime, "|- Японские мультфильмы");
            caps.Categories.AddCategoryMapping(809, TorznabCatType.Audio, "|- Звуковые дорожки (Аниме)");
            caps.Categories.AddCategoryMapping(2484, TorznabCatType.TVAnime, "|- Артбуки и журналы (Аниме)");
            caps.Categories.AddCategoryMapping(1386, TorznabCatType.TVAnime, "|- Обои, сканы, аватары, арт");
            caps.Categories.AddCategoryMapping(1387, TorznabCatType.TVAnime, "|- AMV и другие ролики");
            caps.Categories.AddCategoryMapping(9, TorznabCatType.TV, "Русские сериалы");
            caps.Categories.AddCategoryMapping(81, TorznabCatType.TVHD, "|- Русские сериалы (HD Video)");
            caps.Categories.AddCategoryMapping(920, TorznabCatType.TVSD, "|- Русские сериалы (DVD Video)");
            caps.Categories.AddCategoryMapping(80, TorznabCatType.TV, "|- Сельский детектив");
            caps.Categories.AddCategoryMapping(1535, TorznabCatType.TV, "|- По законам военного времени");
            caps.Categories.AddCategoryMapping(188, TorznabCatType.TV, "|- Московские тайны");
            caps.Categories.AddCategoryMapping(91, TorznabCatType.TV, "|- Я знаю твои секреты");
            caps.Categories.AddCategoryMapping(990, TorznabCatType.TV, "|- Универ / Универ. Новая общага / СашаТаня");
            caps.Categories.AddCategoryMapping(1408, TorznabCatType.TV, "|- Женская версия");
            caps.Categories.AddCategoryMapping(175, TorznabCatType.TV, "|- След");
            caps.Categories.AddCategoryMapping(79, TorznabCatType.TV, "|- Некрасивая подружка");
            caps.Categories.AddCategoryMapping(104, TorznabCatType.TV, "|- Психология преступления");
            caps.Categories.AddCategoryMapping(189, TorznabCatType.TVForeign, "Зарубежные сериалы");
            caps.Categories.AddCategoryMapping(842, TorznabCatType.TVForeign, "|- Новинки и сериалы в стадии показа");
            caps.Categories.AddCategoryMapping(235, TorznabCatType.TVForeign, "|- Сериалы США и Канады");
            caps.Categories.AddCategoryMapping(242, TorznabCatType.TVForeign, "|- Сериалы Великобритании и Ирландии");
            caps.Categories.AddCategoryMapping(819, TorznabCatType.TVForeign, "|- Скандинавские сериалы");
            caps.Categories.AddCategoryMapping(1531, TorznabCatType.TVForeign, "|- Испанские сериалы");
            caps.Categories.AddCategoryMapping(721, TorznabCatType.TVForeign, "|- Итальянские сериалы");
            caps.Categories.AddCategoryMapping(1102, TorznabCatType.TVForeign, "|- Европейские сериалы");
            caps.Categories.AddCategoryMapping(1120, TorznabCatType.TVForeign, "|- Сериалы стран Африки, Ближнего и Среднего Востока");
            caps.Categories.AddCategoryMapping(1214, TorznabCatType.TVForeign, "|- Сериалы Австралии и Новой Зеландии");
            caps.Categories.AddCategoryMapping(489, TorznabCatType.TVForeign, "|- Сериалы Ближнего Зарубежья");
            caps.Categories.AddCategoryMapping(387, TorznabCatType.TVForeign, "|- Сериалы совместного производства нескольких стран");
            caps.Categories.AddCategoryMapping(1359, TorznabCatType.TVForeign, "|- Веб-сериалы, Вебизоды к сериалам и Пилотные серии сериалов");
            caps.Categories.AddCategoryMapping(184, TorznabCatType.TVForeign, "|- Бесстыжие / Shameless (US)");
            caps.Categories.AddCategoryMapping(1171, TorznabCatType.TVForeign, "|- Викинги / Vikings");
            caps.Categories.AddCategoryMapping(1417, TorznabCatType.TVForeign, "|- Во все тяжкие / Breaking Bad");
            caps.Categories.AddCategoryMapping(625, TorznabCatType.TVForeign, "|- Доктор Хаус / House M.D.");
            caps.Categories.AddCategoryMapping(1449, TorznabCatType.TVForeign, "|- Игра престолов / Game of Thrones");
            caps.Categories.AddCategoryMapping(273, TorznabCatType.TVForeign, "|- Карточный Домик / House of Cards");
            caps.Categories.AddCategoryMapping(504, TorznabCatType.TVForeign, "|- Клан Сопрано / The Sopranos");
            caps.Categories.AddCategoryMapping(372, TorznabCatType.TVForeign, "|- Сверхъестественное / Supernatural");
            caps.Categories.AddCategoryMapping(110, TorznabCatType.TVForeign, "|- Секретные материалы / The X-Files");
            caps.Categories.AddCategoryMapping(121, TorznabCatType.TVForeign, "|- Твин пикс / Twin Peaks");
            caps.Categories.AddCategoryMapping(507, TorznabCatType.TVForeign, "|- Теория большого взрыва + Детство Шелдона");
            caps.Categories.AddCategoryMapping(536, TorznabCatType.TVForeign, "|- Форс-мажоры / Костюмы в законе / Suits");
            caps.Categories.AddCategoryMapping(1144, TorznabCatType.TVForeign, "|- Ходячие мертвецы + Бойтесь ходячих мертвецов");
            caps.Categories.AddCategoryMapping(173, TorznabCatType.TVForeign, "|- Черное зеркало / Black Mirror");
            caps.Categories.AddCategoryMapping(195, TorznabCatType.TVForeign, "|- Для некондиционных раздач");
            caps.Categories.AddCategoryMapping(2366, TorznabCatType.TVHD, "Зарубежные сериалы (HD Video)");
            caps.Categories.AddCategoryMapping(119, TorznabCatType.TVUHD, "|- Зарубежные сериалы (UHD Video)");
            caps.Categories.AddCategoryMapping(1803, TorznabCatType.TVHD, "|- Новинки и сериалы в стадии показа (HD Video)");
            caps.Categories.AddCategoryMapping(266, TorznabCatType.TVHD, "|- Сериалы США и Канады (HD Video)");
            caps.Categories.AddCategoryMapping(193, TorznabCatType.TVHD, "|- Сериалы Великобритании и Ирландии (HD Video)");
            caps.Categories.AddCategoryMapping(1690, TorznabCatType.TVHD, "|- Скандинавские сериалы (HD Video)");
            caps.Categories.AddCategoryMapping(1459, TorznabCatType.TVHD, "|- Европейские сериалы (HD Video)");
            caps.Categories.AddCategoryMapping(1463, TorznabCatType.TVHD, "|- Сериалы стран Африки, Ближнего и Среднего Востока (HD Video)");
            caps.Categories.AddCategoryMapping(825, TorznabCatType.TVHD, "|- Сериалы Австралии и Новой Зеландии (HD Video)");
            caps.Categories.AddCategoryMapping(1248, TorznabCatType.TVHD, "|- Сериалы Ближнего Зарубежья (HD Video)");
            caps.Categories.AddCategoryMapping(1288, TorznabCatType.TVHD, "|- Сериалы совместного производства нескольких стран (HD Video)");
            caps.Categories.AddCategoryMapping(1669, TorznabCatType.TVHD, "|- Викинги / Vikings (HD Video)");
            caps.Categories.AddCategoryMapping(2393, TorznabCatType.TVHD, "|- Доктор Хаус / House M.D. (HD Video)");
            caps.Categories.AddCategoryMapping(265, TorznabCatType.TVHD, "|- Игра престолов / Game of Thrones (HD Video)");
            caps.Categories.AddCategoryMapping(2406, TorznabCatType.TVHD, "|- Карточный домик (HD Video)");
            caps.Categories.AddCategoryMapping(2404, TorznabCatType.TVHD, "|- Сверхъестественное / Supernatural (HD Video)");
            caps.Categories.AddCategoryMapping(2405, TorznabCatType.TVHD, "|- Секретные материалы / The X-Files (HD Video)");
            caps.Categories.AddCategoryMapping(2370, TorznabCatType.TVHD, "|- Твин пикс / Twin Peaks (HD Video)");
            caps.Categories.AddCategoryMapping(2396, TorznabCatType.TVHD, "|- Теория Большого Взрыва / The Big Bang Theory (HD Video)");
            caps.Categories.AddCategoryMapping(2398, TorznabCatType.TVHD, "|- Ходячие мертвецы + Бойтесь ходячих мертвецов (HD Video)");
            caps.Categories.AddCategoryMapping(1949, TorznabCatType.TVHD, "|- Черное зеркало / Black Mirror (HD Video)");
            caps.Categories.AddCategoryMapping(1498, TorznabCatType.TVHD, "|- Для некондиционных раздач (HD Video)");
            caps.Categories.AddCategoryMapping(911, TorznabCatType.TVForeign, "Сериалы Латинской Америки, Турции и Индии");
            caps.Categories.AddCategoryMapping(1493, TorznabCatType.TVForeign, "|- Актёры и актрисы латиноамериканских сериалов");
            caps.Categories.AddCategoryMapping(325, TorznabCatType.TVForeign, "|- Сериалы Аргентины");
            caps.Categories.AddCategoryMapping(534, TorznabCatType.TVForeign, "|- Сериалы Бразилии");
            caps.Categories.AddCategoryMapping(594, TorznabCatType.TVForeign, "|- Сериалы Венесуэлы");
            caps.Categories.AddCategoryMapping(1301, TorznabCatType.TVForeign, "|- Сериалы Индии");
            caps.Categories.AddCategoryMapping(607, TorznabCatType.TVForeign, "|- Сериалы Колумбии");
            caps.Categories.AddCategoryMapping(1574, TorznabCatType.TVForeign, "|- Сериалы Латинской Америки с озвучкой (раздачи папками)");
            caps.Categories.AddCategoryMapping(1539, TorznabCatType.TVForeign, "|- Сериалы Латинской Америки с субтитрами");
            caps.Categories.AddCategoryMapping(1940, TorznabCatType.TVForeign, "|- Официальные краткие версии сериалов Латинской Америки");
            caps.Categories.AddCategoryMapping(694, TorznabCatType.TVForeign, "|- Сериалы Мексики");
            caps.Categories.AddCategoryMapping(775, TorznabCatType.TVForeign, "|- Сериалы Перу, Сальвадора, Чили и других стран");
            caps.Categories.AddCategoryMapping(781, TorznabCatType.TVForeign, "|- Сериалы совместного производства");
            caps.Categories.AddCategoryMapping(718, TorznabCatType.TVForeign, "|- Сериалы США (латиноамериканские)");
            caps.Categories.AddCategoryMapping(704, TorznabCatType.TVForeign, "|- Сериалы Турции");
            caps.Categories.AddCategoryMapping(1537, TorznabCatType.TVForeign, "|- Для некондиционных раздач");
            caps.Categories.AddCategoryMapping(2100, TorznabCatType.TVForeign, "Азиатские сериалы");
            caps.Categories.AddCategoryMapping(820, TorznabCatType.TVForeign, "|- Азиатские сериалы (UHD Video)");
            caps.Categories.AddCategoryMapping(915, TorznabCatType.TVForeign, "|- Корейские сериалы");
            caps.Categories.AddCategoryMapping(1242, TorznabCatType.TVForeign, "|- Корейские сериалы (HD Video)");
            caps.Categories.AddCategoryMapping(717, TorznabCatType.TVForeign, "|- Китайские сериалы");
            caps.Categories.AddCategoryMapping(1939, TorznabCatType.TVForeign, "|- Японские сериалы");
            caps.Categories.AddCategoryMapping(2412, TorznabCatType.TVForeign, "|- Сериалы Таиланда, Индонезии, Сингапура");
            caps.Categories.AddCategoryMapping(2102, TorznabCatType.TVForeign, "|- VMV и др. ролики");
            caps.Categories.AddCategoryMapping(1959, TorznabCatType.TVOther, "|- [Видео Юмор] Интеллектуальные игры и викторины");
            caps.Categories.AddCategoryMapping(939, TorznabCatType.TVOther, "|- [Видео Юмор] Реалити и ток-шоу / номинации / показы");
            caps.Categories.AddCategoryMapping(1481, TorznabCatType.TVOther, "|- [Видео Юмор] Детские телешоу");
            caps.Categories.AddCategoryMapping(113, TorznabCatType.TVOther, "|- [Видео Юмор] КВН");
            caps.Categories.AddCategoryMapping(115, TorznabCatType.TVOther, "|- [Видео Юмор] Пост КВН");
            caps.Categories.AddCategoryMapping(882, TorznabCatType.TVOther, "|- [Видео Юмор] Кривое Зеркало / Городок / В Городке");
            caps.Categories.AddCategoryMapping(1482, TorznabCatType.TVOther, "|- [Видео Юмор] Ледовые шоу");
            caps.Categories.AddCategoryMapping(393, TorznabCatType.TVOther, "|- [Видео Юмор] Музыкальные шоу");
            caps.Categories.AddCategoryMapping(1569, TorznabCatType.TVOther, "|- [Видео Юмор] Званый ужин");
            caps.Categories.AddCategoryMapping(373, TorznabCatType.TVOther, "|- [Видео Юмор] Хорошие Шутки");
            caps.Categories.AddCategoryMapping(1186, TorznabCatType.TVOther, "|- [Видео Юмор] Вечерний Квартал");
            caps.Categories.AddCategoryMapping(137, TorznabCatType.TVOther, "|- [Видео Юмор] Фильмы со смешным переводом (пародии)");
            caps.Categories.AddCategoryMapping(2537, TorznabCatType.TVOther, "|- [Видео Юмор] Stand-up comedy");
            caps.Categories.AddCategoryMapping(532, TorznabCatType.TVOther, "|- [Видео Юмор] Украинские Шоу");
            caps.Categories.AddCategoryMapping(827, TorznabCatType.TVOther, "|- [Видео Юмор] Танцевальные шоу, концерты, выступления");
            caps.Categories.AddCategoryMapping(1484, TorznabCatType.TVOther, "|- [Видео Юмор] Цирк");
            caps.Categories.AddCategoryMapping(1485, TorznabCatType.TVOther, "|- [Видео Юмор] Школа злословия");
            caps.Categories.AddCategoryMapping(114, TorznabCatType.TVOther, "|- [Видео Юмор] Сатирики и юмористы");
            caps.Categories.AddCategoryMapping(1332, TorznabCatType.TVOther, "|- Юмористические аудиопередачи");
            caps.Categories.AddCategoryMapping(1495, TorznabCatType.TVOther, "|- Аудио и видео ролики (Приколы и юмор)");
            caps.Categories.AddCategoryMapping(413, TorznabCatType.AudioVideo, "Музыкальное SD видео");
            caps.Categories.AddCategoryMapping(445, TorznabCatType.AudioVideo, "|- Классическая и современная академическая музыка (Видео)");
            caps.Categories.AddCategoryMapping(702, TorznabCatType.AudioVideo, "|- Опера, Оперетта и Мюзикл (Видео)");
            caps.Categories.AddCategoryMapping(1990, TorznabCatType.AudioVideo, "|- Балет и современная хореография (Видео)");
            caps.Categories.AddCategoryMapping(1793, TorznabCatType.AudioVideo, "|- Классика в современной обработке, Classical Crossover (Видео)");
            caps.Categories.AddCategoryMapping(1141, TorznabCatType.AudioVideo, "|- Фольклор, Народная и Этническая музыка и фламенко (Видео)");
            caps.Categories.AddCategoryMapping(1775, TorznabCatType.AudioVideo, "|- New Age, Relax, Meditative, Рэп, Хип-Хоп, R'n'B, Reggae, Ska, Dub (Видео)");
            caps.Categories.AddCategoryMapping(1227, TorznabCatType.AudioVideo, "|- Зарубежный и Отечественный Шансон, Авторская и Военная песня (Видео)");
            caps.Categories.AddCategoryMapping(475, TorznabCatType.AudioVideo, "|- Музыка других жанров, Советская эстрада, ретро, романсы (Видео)");
            caps.Categories.AddCategoryMapping(1121, TorznabCatType.AudioVideo, "|- Отечественная поп-музыка (Видео)");
            caps.Categories.AddCategoryMapping(431, TorznabCatType.AudioVideo, "|- Зарубежная Поп-музыка, Eurodance, Disco (Видео)");
            caps.Categories.AddCategoryMapping(2378, TorznabCatType.AudioVideo, "|- Восточноазиатская поп-музыка (Видео)");
            caps.Categories.AddCategoryMapping(2383, TorznabCatType.AudioVideo, "|- Разножанровые сборные концерты и сборники видеоклипов (Видео)");
            caps.Categories.AddCategoryMapping(2305, TorznabCatType.AudioVideo, "|- Джаз и Блюз (Видео)");
            caps.Categories.AddCategoryMapping(1782, TorznabCatType.AudioVideo, "|- Rock (Видео)");
            caps.Categories.AddCategoryMapping(1787, TorznabCatType.AudioVideo, "|- Metal (Видео)");
            caps.Categories.AddCategoryMapping(1789, TorznabCatType.AudioVideo, "|- Зарубежный Alternative, Punk, Independent (Видео)");
            caps.Categories.AddCategoryMapping(1791, TorznabCatType.AudioVideo, "|- Отечественный Рок, Панк, Альтернатива (Видео)");
            caps.Categories.AddCategoryMapping(1912, TorznabCatType.AudioVideo, "|- Электронная музыка (Видео)");
            caps.Categories.AddCategoryMapping(1189, TorznabCatType.AudioVideo, "|- Документальные фильмы о музыке и музыкантах (Видео)");
            caps.Categories.AddCategoryMapping(2403, TorznabCatType.AudioVideo, "Музыкальное DVD видео");
            caps.Categories.AddCategoryMapping(984, TorznabCatType.AudioVideo, "|- Классическая и современная академическая музыка (DVD Video)");
            caps.Categories.AddCategoryMapping(983, TorznabCatType.AudioVideo, "|- Опера, Оперетта и Мюзикл (DVD видео)");
            caps.Categories.AddCategoryMapping(2352, TorznabCatType.AudioVideo, "|- Балет и современная хореография (DVD Video)");
            caps.Categories.AddCategoryMapping(2384, TorznabCatType.AudioVideo, "|- Классика в современной обработке, Classical Crossover (DVD Video)");
            caps.Categories.AddCategoryMapping(1142, TorznabCatType.AudioVideo, "|- Фольклор, Народная и Этническая музыка и Flamenco (DVD Video)");
            caps.Categories.AddCategoryMapping(1107, TorznabCatType.AudioVideo, "|- New Age, Relax, Meditative, Рэп, Хип-Хоп, R'n'B, Reggae, Ska, Dub (DVD Video)");
            caps.Categories.AddCategoryMapping(1228, TorznabCatType.AudioVideo, "|- Зарубежный и Отечественный Шансон, Авторская и Военная песня (DVD Video)");
            caps.Categories.AddCategoryMapping(988, TorznabCatType.AudioVideo, "|- Музыка других жанров, Советская эстрада, ретро, романсы (DVD Video)");
            caps.Categories.AddCategoryMapping(1122, TorznabCatType.AudioVideo, "|- Отечественная поп-музыка (DVD Video)");
            caps.Categories.AddCategoryMapping(986, TorznabCatType.AudioVideo, "|- Зарубежная Поп-музыка, Eurodance, Disco (DVD Video)");
            caps.Categories.AddCategoryMapping(2379, TorznabCatType.AudioVideo, "|- Восточноазиатская поп-музыка (DVD Video)");
            caps.Categories.AddCategoryMapping(2088, TorznabCatType.AudioVideo, "|- Разножанровые сборные концерты и сборники видеоклипов (DVD Video)");
            caps.Categories.AddCategoryMapping(2304, TorznabCatType.AudioVideo, "|- Джаз и Блюз (DVD Видео)");
            caps.Categories.AddCategoryMapping(1783, TorznabCatType.AudioVideo, "|- Зарубежный Rock (DVD Video)");
            caps.Categories.AddCategoryMapping(1788, TorznabCatType.AudioVideo, "|- Зарубежный Metal (DVD Video)");
            caps.Categories.AddCategoryMapping(1790, TorznabCatType.AudioVideo, "|- Зарубежный Alternative, Punk, Independent (DVD Video)");
            caps.Categories.AddCategoryMapping(1792, TorznabCatType.AudioVideo, "|- Отечественный Рок, Метал, Панк, Альтернатива (DVD Video)");
            caps.Categories.AddCategoryMapping(1886, TorznabCatType.AudioVideo, "|- Электронная музыка (DVD Video)");
            caps.Categories.AddCategoryMapping(2509, TorznabCatType.AudioVideo, "|- Документальные фильмы о музыке и музыкантах (DVD Video)");
            caps.Categories.AddCategoryMapping(2507, TorznabCatType.AudioVideo, "Неофициальные DVD видео");
            caps.Categories.AddCategoryMapping(2263, TorznabCatType.AudioVideo, "|- Классическая музыка, Опера, Балет, Мюзикл (Неофициальные DVD Video)");
            caps.Categories.AddCategoryMapping(2511, TorznabCatType.AudioVideo, "|- Шансон, Авторская песня, Сборные концерты, МДЖ (Неофициальные DVD Video)");
            caps.Categories.AddCategoryMapping(2264, TorznabCatType.AudioVideo, "|- Зарубежная и Отечественная Поп-музыка (Неофициальные DVD Video)");
            caps.Categories.AddCategoryMapping(2262, TorznabCatType.AudioVideo, "|- Джаз и Блюз (Неофициальные DVD Video)");
            caps.Categories.AddCategoryMapping(2261, TorznabCatType.AudioVideo, "|- Зарубежная и Отечественная Рок-музыка (Неофициальные DVD Video)");
            caps.Categories.AddCategoryMapping(1887, TorznabCatType.AudioVideo, "|- Электронная музыка (Неофициальные DVD Video)");
            caps.Categories.AddCategoryMapping(2531, TorznabCatType.AudioVideo, "|- Прочие жанры (Неофициальные DVD видео)");
            caps.Categories.AddCategoryMapping(2400, TorznabCatType.AudioVideo, "Музыкальное HD видео");
            caps.Categories.AddCategoryMapping(1812, TorznabCatType.AudioVideo, "|- Классическая и современная академическая музыка (HD Video)");
            caps.Categories.AddCategoryMapping(655, TorznabCatType.AudioVideo, "|- Опера, Оперетта и Мюзикл (HD Видео)");
            caps.Categories.AddCategoryMapping(1777, TorznabCatType.AudioVideo, "|- Балет и современная хореография (HD Video)");
            caps.Categories.AddCategoryMapping(2530, TorznabCatType.AudioVideo, "|- Фольклор, Народная, Этническая музыка и Flamenco (HD Видео)");
            caps.Categories.AddCategoryMapping(2529, TorznabCatType.AudioVideo, "|- New Age, Relax, Meditative, Рэп, Хип-Хоп, R'n'B, Reggae, Ska, Dub (HD Видео)");
            caps.Categories.AddCategoryMapping(1781, TorznabCatType.AudioVideo, "|- Музыка других жанров, Разножанровые сборные концерты (HD видео)");
            caps.Categories.AddCategoryMapping(2508, TorznabCatType.AudioVideo, "|- Зарубежная поп-музыка (HD Video)");
            caps.Categories.AddCategoryMapping(2426, TorznabCatType.AudioVideo, "|- Отечественная поп-музыка (HD видео)");
            caps.Categories.AddCategoryMapping(2351, TorznabCatType.AudioVideo, "|- Восточноазиатская Поп-музыка (HD Video)");
            caps.Categories.AddCategoryMapping(2306, TorznabCatType.AudioVideo, "|- Джаз и Блюз (HD Video)");
            caps.Categories.AddCategoryMapping(1795, TorznabCatType.AudioVideo, "|- Зарубежный рок (HD Video)");
            caps.Categories.AddCategoryMapping(2271, TorznabCatType.AudioVideo, "|- Отечественный рок (HD видео)");
            caps.Categories.AddCategoryMapping(1913, TorznabCatType.AudioVideo, "|- Электронная музыка (HD Video)");
            caps.Categories.AddCategoryMapping(1784, TorznabCatType.AudioVideo, "|- UHD музыкальное видео");
            caps.Categories.AddCategoryMapping(1892, TorznabCatType.AudioVideo, "|- Документальные фильмы о музыке и музыкантах (HD Video)");
            caps.Categories.AddCategoryMapping(518, TorznabCatType.AudioVideo, "Некондиционное музыкальное видео (Видео, DVD видео, HD видео)");

            return caps;
        }

        public override async Task<ConfigurationData> GetConfigurationForSetup()
        {
            try
            {
                configData.CookieHeader.Value = null;
                var response = await RequestWithCookiesAsync(LoginUrl);
                var parser = new HtmlParser();
                var doc = parser.ParseDocument(response.ContentString);
                var captchaimg = doc.QuerySelector("img[src^=\"https://static.rutracker.cc/captcha/\"]");

                if (captchaimg != null)
                {
                    var captchaImage = await RequestWithCookiesAsync(captchaimg.GetAttribute("src"));
                    configData.CaptchaImage.Value = captchaImage.ContentBytes;

                    _capCodeField = doc.QuerySelector("input[name^=\"cap_code_\"]")?.GetAttribute("name");
                    _capSid = doc.QuerySelector("input[name=\"cap_sid\"]")?.GetAttribute("value");
                }
                else
                    configData.CaptchaImage.Value = null;
            }
            catch (Exception e)
            {
                logger.Error("Error loading configuration: " + e);
            }

            return configData;
        }

        public override async Task<IndexerConfigurationStatus> ApplyConfiguration(JToken configJson)
        {
            LoadValuesFromJson(configJson);

            var pairs = new Dictionary<string, string>
            {
                { "login_username", configData.Username.Value },
                { "login_password", configData.Password.Value },
                { "login", "Login" }
            };

            if (!string.IsNullOrWhiteSpace(_capSid))
            {
                pairs.Add("cap_sid", _capSid);
                pairs.Add(_capCodeField, configData.CaptchaText.Value);

                _capSid = null;
                _capCodeField = null;
            }

            var result = await RequestLoginAndFollowRedirect(LoginUrl, pairs, CookieHeader, true, null, LoginUrl, true);
            await ConfigureIfOK(result.Cookies, result.ContentString != null && result.ContentString.Contains("id=\"logged-in-username\""), () =>
            {
                var errorMessage = "Unknown error message, please report";
                var parser = new HtmlParser();
                var doc = parser.ParseDocument(result.ContentString);
                var errormsg = doc.QuerySelector("div.msg-main");
                if (errormsg != null)
                    errorMessage = errormsg.TextContent;
                errormsg = doc.QuerySelector("h4[class=\"warnColor1 tCenter mrg_16\"]");
                if (errormsg != null)
                    errorMessage = errormsg.TextContent;

                throw new ExceptionWithConfigData(errorMessage, configData);
            });
            return IndexerConfigurationStatus.RequiresTesting;
        }

        protected override async Task<IEnumerable<ReleaseInfo>> PerformQuery(TorznabQuery query)
        {
            var searchUrl = CreateSearchUrlForQuery(query);

            var results = await RequestWithCookiesAsync(searchUrl);
            if (!results.ContentString.Contains("id=\"logged-in-username\""))
            {
                // re login
                await ApplyConfiguration(null);
                results = await RequestWithCookiesAsync(searchUrl);
            }

            var releases = new List<ReleaseInfo>();

            try
            {
                var rows = GetReleaseRows(results);
                foreach (var row in rows)
                {
                    var release = ParseReleaseRow(row);
                    if (release != null)
                    {
                        releases.Add(release);
                    }
                }
            }
            catch (Exception ex)
            {
                OnParseError(results.ContentString, ex);
            }

            return releases;
        }

        public override async Task<byte[]> Download(Uri link)
        {
            if (configData.UseMagnetLinks.Value && link.PathAndQuery.Contains("viewtopic.php?t="))
            {
                var response = await RequestWithCookiesAsync(link.ToString());

                var parser = new HtmlParser();
                var dom = parser.ParseDocument(response.ContentString);
                var magnetLink = dom.QuerySelector("table.attach a.magnet-link[href^=\"magnet:?\"]")?.GetAttribute("href");

                if (magnetLink == null)
                    throw new Exception($"Failed to fetch magnet link from {link}");

                link = new Uri(magnetLink);
            }

            return await base.Download(link);
        }

        private string CreateSearchUrlForQuery(in TorznabQuery query)
        {
            var queryCollection = new NameValueCollection();

            var searchString = query.SearchTerm;
            //  replace any space, special char, etc. with % (wildcard)
            var ReplaceRegex = new Regex("[^a-zA-Zа-яА-Я0-9]+");
            if (!string.IsNullOrWhiteSpace(searchString))
                searchString = ReplaceRegex.Replace(searchString, "%");

            // if the search string is empty use the getnew view
            if (string.IsNullOrWhiteSpace(searchString))
            {
                queryCollection.Add("nm", searchString);
            }
            else // use the normal search
            {
                searchString = searchString.Replace("-", " ");
                if (query.Season != 0)
                {
                    searchString += " Сезон: " + query.Season;
                }
                if (query.Episode != null)
                {
                    searchString += " Серии: " + query.Episode;
                }
                queryCollection.Add("nm", searchString);
            }

            if (query.HasSpecifiedCategories)
                queryCollection.Add("f", string.Join(",", MapTorznabCapsToTrackers(query)));

            var searchUrl = SearchUrl + "?" + queryCollection.GetQueryString();

            return searchUrl;
        }

        private IHtmlCollection<IElement> GetReleaseRows(WebResult results)
        {
            var parser = new HtmlParser();
            var doc = parser.ParseDocument(results.ContentString);
            var rows = doc.QuerySelectorAll("table#tor-tbl > tbody > tr");
            return rows;
        }

        private ReleaseInfo ParseReleaseRow(IElement row)
        {
            try
            {
                var qDownloadLink = row.QuerySelector("td.tor-size > a.tr-dl");
                if (qDownloadLink == null) // Expects moderation
                    return null;

                var link = new Uri(SiteLink + "forum/" + qDownloadLink.GetAttribute("href"));

                var qDetailsLink = row.QuerySelector("td.t-title-col > div.t-title > a.tLink");
                var details = new Uri(SiteLink + "forum/" + qDetailsLink.GetAttribute("href"));

                var title = qDetailsLink.TextContent.Trim();
                var category = GetCategoryOfRelease(row);

                var size = GetSizeOfRelease(row);

                var seeders = GetSeedersOfRelease(row);
                var leechers = ParseUtil.CoerceInt(row.QuerySelector("td:nth-child(8)").TextContent);

                var grabs = ParseUtil.CoerceLong(row.QuerySelector("td:nth-child(9)").TextContent);

                var publishDate = GetPublishDateOfRelease(row);

                var release = new ReleaseInfo
                {
                    MinimumRatio = 1,
                    MinimumSeedTime = 0,
                    Title = _titleParser.Parse(
                        title,
                        category,
                        configData.StripRussianLetters.Value,
                        configData.MoveAllTagsToEndOfReleaseTitle.Value,
                        configData.MoveFirstTagsToEndOfReleaseTitle.Value,
                        configData.AddRussianToTitle.Value
                    ),
                    Description = title,
                    Details = details,
                    Link = configData.UseMagnetLinks.Value ? details : link,
                    Guid = details,
                    Size = size,
                    Seeders = seeders,
                    Peers = leechers + seeders,
                    Grabs = grabs,
                    PublishDate = publishDate,
                    Category = category,
                    DownloadVolumeFactor = 1,
                    UploadVolumeFactor = 1
                };

                return release;
            }
            catch (Exception ex)
            {
                logger.Error($"{Id}: Error while parsing row '{row.OuterHtml}':\n\n{ex}");
                return null;
            }
        }

        private int GetSeedersOfRelease(in IElement row)
        {
            var seeders = 0;
            var qSeeders = row.QuerySelector("td:nth-child(7)");
            if (qSeeders != null && !qSeeders.TextContent.Contains("дн"))
            {
                var seedersString = qSeeders.QuerySelector("b")?.TextContent.Trim();
                if (!string.IsNullOrWhiteSpace(seedersString))
                    seeders = ParseUtil.CoerceInt(seedersString);
            }
            return seeders;
        }

        private ICollection<int> GetCategoryOfRelease(in IElement row)
        {
            var forum = row.QuerySelector("td.f-name-col > div.f-name > a")?.GetAttribute("href");
            var cat = ParseUtil.GetArgumentFromQueryString(forum, "f");

            return MapTrackerCatToNewznab(cat);
        }

        private long GetSizeOfRelease(in IElement row) => ParseUtil.GetBytes(row.QuerySelector("td.tor-size")?.GetAttribute("data-ts_text"));

        private DateTime GetPublishDateOfRelease(in IElement row) => DateTimeUtil.UnixTimestampToDateTime(long.Parse(row.QuerySelector("td:nth-child(10)")?.GetAttribute("data-ts_text")));

        public class TitleParser
        {
            private static readonly List<Regex> _FindTagsInTitlesRegexList = new List<Regex>
            {
                new Regex(@"\((?>\((?<c>)|[^()]+|\)(?<-c>))*(?(c)(?!))\)"),
                new Regex(@"\[(?>\[(?<c>)|[^\[\]]+|\](?<-c>))*(?(c)(?!))\]")
            };

            private readonly Regex _stripCyrillicRegex = new Regex(@"(\([\p{IsCyrillic}\W]+\))|(^[\p{IsCyrillic}\W\d]+\/ )|([\p{IsCyrillic} \-]+,+)|([\p{IsCyrillic}]+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);

            private readonly Regex _tvTitleCommaRegex = new Regex(@"\s(\d+),(\d+)", RegexOptions.Compiled);
            private readonly Regex _tvTitleCyrillicXRegex = new Regex(@"([\s-])Х+([\s\)\]])", RegexOptions.Compiled | RegexOptions.IgnoreCase);

            private readonly Regex _tvTitleRusSeasonEpisodeOfRegex = new Regex(@"Сезон\s*[:]*\s+(\d+).+(?:Серии|Эпизод|Выпуски)+\s*[:]*\s+(\d+(?:-\d+)?)\s*из\s*([\w?])", RegexOptions.Compiled | RegexOptions.IgnoreCase);
            private readonly Regex _tvTitleRusSeasonEpisodeRegex = new Regex(@"Сезон\s*[:]*\s+(\d+).+(?:Серии|Эпизод|Выпуски)+\s*[:]*\s+(\d+(?:-\d+)?)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
            private readonly Regex _tvTitleRusSeasonRegex = new Regex(@"Сезон\s*[:]*\s+(\d+(?:-\d+)?)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
            private readonly Regex _tvTitleRusEpisodeOfRegex = new Regex(@"(?:Серии|Эпизод|Выпуски)+\s*[:]*\s+(\d+(?:-\d+)?)\s*из\s*([\w?])", RegexOptions.Compiled | RegexOptions.IgnoreCase);
            private readonly Regex _tvTitleRusEpisodeRegex = new Regex(@"(?:Серии|Эпизод|Выпуски)+\s*[:]*\s+(\d+(?:-\d+)?)", RegexOptions.Compiled | RegexOptions.IgnoreCase);

            public string Parse(string title,
                                ICollection<int> category,
                                bool stripCyrillicLetters = true,
                                bool moveAllTagsToEndOfReleaseTitle = false,
                                bool moveFirstTagsToEndOfReleaseTitle = false,
                                bool addRussianToTitle = false)
            {
                // https://www.fileformat.info/info/unicode/category/Pd/list.htm
                title = Regex.Replace(title, @"\p{Pd}", "-", RegexOptions.Compiled | RegexOptions.IgnoreCase);

                // replace double 4K quality in title
                title = Regex.Replace(title, @"\b(2160p), 4K\b", "$1", RegexOptions.Compiled | RegexOptions.IgnoreCase);

                if (IsAnyTvCategory(category))
                {
                    title = _tvTitleCommaRegex.Replace(title, " $1-$2");
                    title = _tvTitleCyrillicXRegex.Replace(title, "$1XX$2");

                    title = _tvTitleRusSeasonEpisodeOfRegex.Replace(title, "S$1E$2 of $3");
                    title = _tvTitleRusSeasonEpisodeRegex.Replace(title, "S$1E$2");
                    title = _tvTitleRusSeasonRegex.Replace(title, "S$1");
                    title = _tvTitleRusEpisodeOfRegex.Replace(title, "E$1 of $2");
                    title = _tvTitleRusEpisodeRegex.Replace(title, "E$1");
                }
                else if (IsAnyMovieCategory(category))
                {
                    // remove director's name from title
                    // rutracker movies titles look like: russian name / english name (russian director / english director) other stuff
                    // Ирландец / The Irishman (Мартин Скорсезе / Martin Scorsese) [2019, США, криминал, драма, биография, WEB-DL 1080p] Dub (Пифагор) + MVO (Jaskier) + AVO (Юрий Сербин) + Sub Rus, Eng + Original Eng
                    // this part should be removed: (Мартин Скорсезе / Martin Scorsese)
                    title = Regex.Replace(title, @"(\([\p{IsCyrillic}\W]+)\s/\s(.+?)\)", string.Empty, RegexOptions.Compiled | RegexOptions.IgnoreCase);

                    // Bluray quality fix: radarr parse Blu-ray Disc as Bluray-1080p but should be BR-DISK
                    title = Regex.Replace(title, @"\bBlu-ray Disc\b", "BR-DISK", RegexOptions.Compiled | RegexOptions.IgnoreCase);
                }

                // language fix: all rutracker releases contains russian track
                if (addRussianToTitle && (IsAnyTvCategory(category) || IsAnyMovieCategory(category)) && !Regex.Match(title, "\bRUS\b", RegexOptions.IgnoreCase).Success)
                    title += " RUS";

                if (stripCyrillicLetters)
                    title = _stripCyrillicRegex.Replace(title, string.Empty).Trim(' ', '-');

                if (moveAllTagsToEndOfReleaseTitle)
                    title = MoveAllTagsToEndOfReleaseTitle(title);
                else if (moveFirstTagsToEndOfReleaseTitle)
                    title = MoveFirstTagsToEndOfReleaseTitle(title);

                if (IsAnyAudioCategory(category))
                    title = DetectRereleaseInReleaseTitle(title);

                title = Regex.Replace(title, @"\b-Rip\b", "Rip", RegexOptions.Compiled | RegexOptions.IgnoreCase);
                title = Regex.Replace(title, @"\bHDTVRip\b", "HDTV", RegexOptions.Compiled | RegexOptions.IgnoreCase);
                title = Regex.Replace(title, @"\bWEB-DLRip\b", "WEB-DL", RegexOptions.Compiled | RegexOptions.IgnoreCase);
                title = Regex.Replace(title, @"\bWEBDLRip\b", "WEB-DL", RegexOptions.Compiled | RegexOptions.IgnoreCase);
                title = Regex.Replace(title, @"\bWEBDL\b", "WEB-DL", RegexOptions.Compiled | RegexOptions.IgnoreCase);
                title = Regex.Replace(title, @"\bКураж-Бамбей\b", "kurazh", RegexOptions.Compiled | RegexOptions.IgnoreCase);

                title = Regex.Replace(title, @"\(\s*\/\s*", "(", RegexOptions.Compiled);
                title = Regex.Replace(title, @"\s*\/\s*\)", ")", RegexOptions.Compiled);

                title = Regex.Replace(title, @"[\[\(]\s*[\)\]]", "", RegexOptions.Compiled);

                title = title.Trim(' ', '&', ',', '.', '!', '?', '+', '-', '_', '|', '/', '\\', ':');

                // replace multiple spaces with a single space
                title = Regex.Replace(title, @"\s+", " ");

                return title.Trim();
            }

            private static bool IsAnyTvCategory(ICollection<int> category) => category.Contains(TorznabCatType.TV.ID) || TorznabCatType.TV.SubCategories.Any(subCat => category.Contains(subCat.ID));

            private static bool IsAnyMovieCategory(ICollection<int> category) => category.Contains(TorznabCatType.Movies.ID) || TorznabCatType.Movies.SubCategories.Any(subCat => category.Contains(subCat.ID));

            private static bool IsAnyAudioCategory(ICollection<int> category) => category.Contains(TorznabCatType.Audio.ID) || TorznabCatType.Audio.SubCategories.Any(subCat => category.Contains(subCat.ID));

            private static string MoveAllTagsToEndOfReleaseTitle(string input)
            {
                var output = input;
                foreach (var findTagsRegex in _FindTagsInTitlesRegexList)
                {
                    foreach (Match match in findTagsRegex.Matches(input))
                    {
                        var tag = match.ToString();
                        output = $"{output.Replace(tag, "")} {tag}".Trim();
                    }
                }

                return output.Trim();
            }

            private static string MoveFirstTagsToEndOfReleaseTitle(string input)
            {
                var output = input;
                foreach (var findTagsRegex in _FindTagsInTitlesRegexList)
                {
                    var expectedIndex = 0;
                    foreach (Match match in findTagsRegex.Matches(output))
                    {
                        if (match.Index > expectedIndex)
                        {
                            var substring = output.Substring(expectedIndex, match.Index - expectedIndex);
                            if (string.IsNullOrWhiteSpace(substring))
                                expectedIndex = match.Index;
                            else
                                break;
                        }

                        var tag = match.ToString();
                        var regex = new Regex(Regex.Escape(tag));
                        output = $"{regex.Replace(output, string.Empty, 1)} {tag}".Trim();
                        expectedIndex += tag.Length;
                    }
                }

                return output.Trim();
            }

            /// <summary>
            /// Searches the release title to find a 'year1/year2' pattern that would indicate that this is a re-release of an old music album.
            /// If the release is found to be a re-release, this is added to the title as a new tag.
            /// Not to be confused with discographies; they mostly follow the 'year1-year2' pattern.
            /// </summary>
            private static string DetectRereleaseInReleaseTitle(string input)
            {
                var fullTitle = input;

                var squareBracketTags = input.FindSubstringsBetween('[', ']', includeOpeningAndClosing: true);
                input = input.RemoveSubstrings(squareBracketTags);

                var roundBracketTags = input.FindSubstringsBetween('(', ')', includeOpeningAndClosing: true);
                input = input.RemoveSubstrings(roundBracketTags);

                var regex = new Regex(@"\d{4}");
                var yearsInTitle = regex.Matches(input);

                if (yearsInTitle == null || yearsInTitle.Count < 2)
                {
                    //Can only be a re-release if there's at least 2 years in the title.
                    return fullTitle;
                }

                regex = new Regex(@"(\d{4}) *\/ *(\d{4})");
                var regexMatch = regex.Match(input);
                if (!regexMatch.Success)
                {
                    //Not in the expected format. Return the unaltered title.
                    return fullTitle;
                }

                var originalYear = regexMatch.Groups[1].ToString();
                fullTitle = fullTitle.Replace(regexMatch.ToString(), originalYear);

                return fullTitle + "(Re-release)";
            }
        }
    }
}
