using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Media;

namespace GGMaps
{
    public sealed class PlacePhoto
    {
        /// <summary>googleusercontent base URL without the size suffix (size appended per use).</summary>
        public string Name;
        public string Author;
        public string Url { get; set; }        // thumbnail url
        public string LargeUrl { get; set; }   // viewer url (filled when the viewer opens)
    }

    public sealed class PlaceReview
    {
        public string Author { get; set; }
        public string AuthorPhoto { get; set; }
        public double Rating { get; set; }
        public string When { get; set; }
        public string Text { get; set; }
        public string Stars { get { return Fmt.Stars(Rating); } }
        public string Initial { get { return string.IsNullOrEmpty(Author) ? "?" : Author.Substring(0, 1).ToUpper(); } }
        public Visibility TextVisibility { get { return string.IsNullOrEmpty(Text) ? Visibility.Collapsed : Visibility.Visible; } }
    }

    /// <summary>A Google place parsed from the Google Maps web "search" response, plus display helpers for bindings.</summary>
    public sealed class Place
    {
        public string Id { get; set; }          // place id (ChIJ...)
        public string FeatureId { get; set; }   // 0x...:0x...
        public string Name { get; set; }
        public string Address { get; set; }
        public string ShortAddress { get; set; }
        public LatLng Location { get; set; }
        public double Rating { get; set; }
        public int RatingCount { get; set; }
        public string TypeLabel { get; set; }
        public string PriceText { get; set; }
        /// <summary>Google's own status line: "Đang mở cửa · Đóng cửa vào 18:00", "Mở cả ngày", "Đã đóng cửa · Mở cửa lúc 6:00 Thứ 7"…</summary>
        public string StatusText { get; set; }
        public string Phone { get; set; }
        public string IntlPhone { get; set; }
        public string Website { get; set; }
        public string Summary { get; set; }
        public string PlusCode { get; set; }
        /// <summary>"Thứ Sáu: 08:00–18:00"…; Google lists today first.</summary>
        public List<string> WeekdayHours = new List<string>();
        public List<PlacePhoto> Photos = new List<PlacePhoto>();
        public List<PlaceReview> Reviews = new List<PlaceReview>();
        public bool IsDroppedPin { get; set; }
        public bool HasDetails { get; set; }

        /// <summary>Metres from the user's position (NaN = unknown). Set by the page.</summary>
        public double DistanceMeters = double.NaN;
        public int Index { get; set; }

        public string MapsUri
        {
            get
            {
                if (!string.IsNullOrEmpty(Id)) return "https://www.google.com/maps/place/?q=place_id:" + Id;
                return "https://www.google.com/maps/search/?api=1&query=" + Location.ToString();
            }
        }

        public bool? OpenNow
        {
            get
            {
                string s = StatusText;
                if (string.IsNullOrEmpty(s)) return null;
                if (s.StartsWith("Đang mở") || s.StartsWith("Mở cả ngày") || s.StartsWith("Mở cửa 24") || s.StartsWith("Sắp đóng")) return true;
                if (s.StartsWith("Đã đóng") || s.StartsWith("Đóng cửa") || s.StartsWith("Tạm thời đóng") || s.StartsWith("Sắp mở")) return false;
                return null;
            }
        }

        // ----------------------------------------------------------- display helpers

        public string Title { get { return Name ?? ""; } }
        public bool HasRating { get { return RatingCount > 0 && Rating > 0; } }
        public Visibility RatingVisibility { get { return HasRating ? Visibility.Visible : Visibility.Collapsed; } }
        public string RatingText { get { return Rating.ToString("0.0", Fmt.Vi); } }
        public string StarsText { get { return Fmt.Stars(Rating); } }
        public string RatingCountText { get { return "(" + RatingCount.ToString("#,0", Fmt.Vi) + ")"; } }

        /// <summary>"Quán cà phê · ₫₫ · 1,2 km"</summary>
        public string MetaLine
        {
            get
            {
                var parts = new List<string>();
                if (!string.IsNullOrEmpty(TypeLabel)) parts.Add(TypeLabel);
                if (!string.IsNullOrEmpty(PriceText)) parts.Add(PriceText);
                if (!double.IsNaN(DistanceMeters)) parts.Add(Fmt.Distance(DistanceMeters));
                return string.Join(" · ", parts);
            }
        }

        public Visibility MetaVisibility { get { return MetaLine.Length > 0 ? Visibility.Visible : Visibility.Collapsed; } }
        public string AddressLine { get { return !string.IsNullOrEmpty(ShortAddress) ? ShortAddress : (Address ?? ""); } }

        /// <summary>Coloured first part of the status line.</summary>
        public string OpenStatus
        {
            get
            {
                string s = StatusText ?? "";
                int i = s.IndexOf(" · ", StringComparison.Ordinal);
                return i > 0 ? s.Substring(0, i) : s;
            }
        }

        /// <summary>Rest of the status line (" · Đóng cửa vào 18:00").</summary>
        public string OpenDetail
        {
            get
            {
                string s = StatusText ?? "";
                int i = s.IndexOf(" · ", StringComparison.Ordinal);
                return i > 0 ? s.Substring(i) : "";
            }
        }

        public Visibility OpenVisibility { get { return string.IsNullOrEmpty(StatusText) ? Visibility.Collapsed : Visibility.Visible; } }

        public Brush OpenBrush
        {
            get
            {
                string s = OpenStatus;
                if (s.StartsWith("Sắp")) return Fmt.OrangeBrush;
                return OpenNow == true ? Fmt.GreenBrush : Fmt.RedBrush;
            }
        }

        public string FirstPhotoUrl { get { return Photos.Count > 0 ? Photos[0].Url : null; } }
        public Visibility PhotoVisibility { get { return Photos.Count > 0 ? Visibility.Visible : Visibility.Collapsed; } }

        // ----------------------------------------------------------- parsing (Google Maps web arrays)

        private static readonly Regex PhotoUrlRx = new Regex(@"^https://lh\d\.googleusercontent\.com/");

        /// <summary>Photo URL at the given size (googleusercontent "=w..-h..-k-no" suffix).</summary>
        public static string SizedPhoto(string baseUrl, int w, int h)
        {
            if (string.IsNullOrEmpty(baseUrl)) return null;
            return baseUrl + "=w" + w + "-h" + h + "-k-no";
        }

        /// <summary>
        /// Parses one place entry. Index map (Google Maps web, 2026): 11 name, 9 [_,_,lat,lng], 4 [.. 7 rating, 8 count],
        /// 13 categories, 39 full address, 2 address lines, 7 [website], 178 [[intl phone,..,..,local]], 203 hours / status,
        /// 32 description, 175[9] reviews, 78 place id, 10 feature id, 183[2][2] plus code.
        /// </summary>
        public static Place FromGoogle(JToken p)
        {
            var place = new Place();
            if (p == null) return place;
            place.Name = MapsApi.Str(p, 11);
            place.Id = MapsApi.Str(p, 78);
            place.FeatureId = MapsApi.Str(p, 10);
            double lat = MapsApi.Num(p, 9, 2), lng = MapsApi.Num(p, 9, 3);
            if (!double.IsNaN(lat) && !double.IsNaN(lng)) place.Location = new LatLng(lat, lng);

            double rating = MapsApi.Num(p, 4, 7), count = MapsApi.Num(p, 4, 8);
            if (!double.IsNaN(rating)) place.Rating = rating;
            if (!double.IsNaN(count)) place.RatingCount = (int)count;
            place.PriceText = MapsApi.Str(p, 4, 2);
            place.TypeLabel = MapsApi.Str(p, 13, 0);

            place.Address = MapsApi.Str(p, 39) ?? MapsApi.Str(p, 18);
            var lines = MapsApi.Arr(p, 2);
            if (lines != null && lines.Count > 0)
            {
                var parts = new List<string>();
                for (int i = 0; i < lines.Count && i < 2; i++)
                {
                    string s = lines[i].Type == JTokenType.String ? (string)lines[i] : null;
                    if (!string.IsNullOrEmpty(s)) parts.Add(s);
                }
                place.ShortAddress = string.Join(", ", parts);
            }

            place.Website = MapsApi.Str(p, 7, 0);
            place.IntlPhone = MapsApi.Str(p, 178, 0, 0);
            place.Phone = MapsApi.Str(p, 178, 0, 3) ?? place.IntlPhone;
            place.Summary = MapsApi.Str(p, 32, 1, 1) ?? MapsApi.Str(p, 32, 0, 1);
            place.PlusCode = MapsApi.Str(p, 183, 2, 2, 0);

            // hours: 203[0] = days (today first), each [0] day name, [3][k][0] interval text; 203[1][4][0] = status line
            place.StatusText = MapsApi.Str(p, 203, 1, 4, 0);
            var days = MapsApi.Arr(p, 203, 0);
            if (days != null)
            {
                foreach (var day in days)
                {
                    string name = MapsApi.Str(day, 0);
                    var spans = MapsApi.Arr(day, 3);
                    var texts = new List<string>();
                    if (spans != null) foreach (var sp in spans) { string t = MapsApi.Str(sp, 0); if (!string.IsNullOrEmpty(t)) texts.Add(t); }
                    if (!string.IsNullOrEmpty(name))
                        place.WeekdayHours.Add(name + ": " + (texts.Count > 0 ? string.Join(", ", texts) : "Đóng cửa"));
                }
            }

            // photos: every googleusercontent photo url in the photo blocks (37 = header photos, 72 = gallery)
            var seen = new HashSet<string>();
            foreach (int block in new[] { 37, 72 })
            {
                var groups = MapsApi.Arr(p, block, 0);
                if (groups == null) continue;
                foreach (var g in groups)
                {
                    string url = MapsApi.Str(g, 6, 0);
                    if (url == null || !PhotoUrlRx.IsMatch(url)) continue;
                    int eq = url.LastIndexOf('=');
                    string baseUrl = eq > 0 ? url.Substring(0, eq) : url;
                    if (!seen.Add(baseUrl)) continue;
                    place.Photos.Add(new PlacePhoto { Name = baseUrl, Url = SizedPhoto(baseUrl, 400, 300) });
                    if (place.Photos.Count >= 12) break;
                }
            }

            // reviews: 175[9][0][0][i][0]
            var reviews = MapsApi.Arr(p, 175, 9, 0, 0);
            if (reviews != null)
            {
                foreach (var rr in reviews)
                {
                    var r = MapsApi.At(rr, 0);
                    if (r == null) continue;
                    double stars = MapsApi.Num(r, 2, 0, 0);
                    place.Reviews.Add(new PlaceReview
                    {
                        Author = MapsApi.Str(r, 1, 4, 5, 0) ?? "",
                        AuthorPhoto = MapsApi.Str(r, 1, 4, 5, 1),
                        When = MapsApi.Str(r, 1, 6),
                        Rating = double.IsNaN(stars) ? 0 : stars,
                        Text = MapsApi.Str(r, 2, 15, 0, 0)
                    });
                    if (place.Reviews.Count >= 10) break;
                }
            }
            place.HasDetails = place.Reviews.Count > 0 || !string.IsNullOrEmpty(place.Summary);
            return place;
        }
    }
}
