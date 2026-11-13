using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Globalization;
using Newtonsoft.Json.Linq;
using Windows.Storage;

namespace GGMaps
{
    /// <summary>Recent searches (LocalFolder\history.json), newest first, max 30 entries.</summary>
    public static class HistoryStore
    {
        private const string FileName = "history.json";
        private const int Max = 30;
        private static List<HistoryItem> _items;

        public static async Task<List<HistoryItem>> LoadAsync()
        {
            if (_items != null) return _items;
            _items = new List<HistoryItem>();
            try
            {
                // TryGetItemAsync does not exist on Windows Phone 8.1
                var file = await ApplicationData.Current.LocalFolder.GetFileAsync(FileName);
                string text = await FileIO.ReadTextAsync(file);
                // hand-written (de)serialisation: reflection-based JsonConvert hits a security exception on WP8.1
                var arr = JArray.Parse(text);
                foreach (var t in arr)
                {
                    _items.Add(new HistoryItem
                    {
                        Text = (string)t["t"],
                        Secondary = (string)t["s"],
                        PlaceId = (string)t["p"],
                        Lat = t["la"] != null ? (double)t["la"] : 0,
                        Lon = t["lo"] != null ? (double)t["lo"] : 0,
                        Time = new DateTime(t["ti"] != null ? (long)t["ti"] : 0)
                    });
                }
            }
            catch (System.IO.FileNotFoundException) { }
            catch (Exception ex) { DiagLog.Write("history load " + ex.Message); }
            return _items;
        }

        public static async Task AddAsync(HistoryItem item)
        {
            if (item == null || string.IsNullOrWhiteSpace(item.Text)) return;
            var items = await LoadAsync();
            items.RemoveAll(h => string.Equals(h.Text, item.Text, StringComparison.OrdinalIgnoreCase) &&
                                 string.Equals(h.PlaceId ?? "", item.PlaceId ?? "", StringComparison.Ordinal));
            item.Time = DateTime.Now;
            items.Insert(0, item);
            if (items.Count > Max) items.RemoveRange(Max, items.Count - Max);
            await SaveAsync();
        }

        public static async Task RemoveAsync(HistoryItem item)
        {
            var items = await LoadAsync();
            items.Remove(item);
            await SaveAsync();
        }

        public static async Task ClearAsync()
        {
            var items = await LoadAsync();
            items.Clear();
            await SaveAsync();
        }

        private static async Task SaveAsync()
        {
            try
            {
                var file = await ApplicationData.Current.LocalFolder.CreateFileAsync(FileName, CreationCollisionOption.ReplaceExisting);
                var arr = new JArray();
                foreach (var h in _items)
                {
                    arr.Add(new JObject
                    {
                        ["t"] = h.Text, ["s"] = h.Secondary, ["p"] = h.PlaceId,
                        ["la"] = h.Lat, ["lo"] = h.Lon, ["ti"] = h.Time.Ticks
                    });
                }
                await FileIO.WriteTextAsync(file, arr.ToString(Newtonsoft.Json.Formatting.None));
            }
            catch (Exception ex) { DiagLog.Write("history save " + ex.Message); }
        }
    }
}
