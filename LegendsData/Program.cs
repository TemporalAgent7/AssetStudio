﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Runtime.Serialization.Formatters.Binary;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Drawing;

using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Linq;

using AssetStudio;
using System.Net;
using System.Collections;
using System.Drawing.Imaging;

namespace LegendsData
{
    class Program
    {
        const int LAST_KNOWN_DEFAULT = 7591;

        public static AssetsManager assetsManager = new AssetsManager();

        static void DownloadAsset(string assetsBaseUrl, string assetName)
        {
            Console.Write(string.Format("Downloading {0}/{1}...", assetsBaseUrl, assetName));

            using (var client = new WebClient())
            {
                client.DownloadFile(string.Format("{0}/{1}", assetsBaseUrl, assetName), string.Format("{0}{1}", Path.GetTempPath(), assetName));
            }

            Console.WriteLine("done!");
        }

        static void Main(string[] args)
        {
            int last_known = LAST_KNOWN_DEFAULT;
            if (args.Length == 1)
            {
                if (!int.TryParse(args[0], out last_known))
                {
                    last_known = LAST_KNOWN_DEFAULT;
                }
            }

            bool download = true;

            if (download)
            {
                var assetsBaseUrl = Utils.GetLastAssetUrl(last_known);

                DownloadAsset(assetsBaseUrl, "bindata");
                DownloadAsset(assetsBaseUrl, "localization");
                DownloadAsset(assetsBaseUrl, "sprite_hero_bundle2");
            }

            string outPath = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);

            // ensure paths
            Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "extracted"));
            Directory.CreateDirectory(Path.Combine(outPath, "extracted"));
            Directory.CreateDirectory(Path.Combine(outPath, "extracted_assets"));

            // extract with AssetBundle
            assetsManager.LoadFiles(new[] { string.Format("{0}bindata", Path.GetTempPath()), string.Format("{0}localization", Path.GetTempPath()), string.Format("{0}sprite_hero_bundle2", Path.GetTempPath()) });

            // Localization
            foreach (AssetStudio.Object asset in assetsManager.assetsFileList[1].Objects)
            {
                if (asset is AssetStudio.MonoBehaviour)
                {
                    var monoBehaviour = asset as AssetStudio.MonoBehaviour;

                    var type = monoBehaviour.ToType();
                    var str = JsonConvert.SerializeObject(type);
                    File.WriteAllText(Path.Combine(outPath, "extracted", monoBehaviour.m_Name + ".json"), str);
                }
            }

            // Image assets
            foreach (AssetStudio.Object asset in assetsManager.assetsFileList[2].Objects)
            {
                if (asset is AssetStudio.Texture2D)
                {
                    var textureAsset = asset as AssetStudio.Texture2D;
                    var bitmap = textureAsset.ConvertToBitmap(true);
                    bitmap.Save(Path.Combine(outPath, "extracted_assets", textureAsset.m_Name + ".png"), ImageFormat.Png);
                }
            }

            // bindata
            foreach (AssetStudio.Object asset in assetsManager.assetsFileList[0].Objects)
            {
                if (asset is AssetStudio.TextAsset)
                {
                    var textAsset = asset as AssetStudio.TextAsset;

                    File.WriteAllBytes(Path.Combine(Path.GetTempPath(), "extracted", textAsset.m_Name), textAsset.m_Script);
                }
            }

            DeserializeData deserializeData = new DeserializeData(Path.Combine(Path.GetTempPath(), "extracted"), Path.Combine(outPath, "extracted"));
            deserializeData.Start();
        }
    }

    public class ListDictionaryConverter : JsonConverter
    {
        private static (Type kvp, Type list, Type enumerable, Type[] args) GetTypes(Type objectType)
        {
            var args = objectType.GenericTypeArguments;
            var kvpType = typeof(KeyValuePair<,>).MakeGenericType(args);
            var listType = typeof(List<>).MakeGenericType(kvpType);
            var enumerableType = typeof(IEnumerable<>).MakeGenericType(kvpType);

            return (kvpType, listType, enumerableType, args);
        }

        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            /*var (kvpType, listType, _, args) = GetTypes(value.GetType());

            var keys = ((IDictionary)value).Keys.GetEnumerator();
            var values = ((IDictionary)value).Values.GetEnumerator();
            var cl = listType.GetConstructor(Array.Empty<Type>());
            var ckvp = kvpType.GetConstructor(args);

            var list = (IList)cl!.Invoke(Array.Empty<object>());
            while (keys.MoveNext() && values.MoveNext())
            {
                list.Add(ckvp!.Invoke(new[] { keys.Current, values.Current }));
            }

            serializer.Serialize(writer, list);*/

            var dictionary = (IDictionary)value;

            writer.WriteStartArray();

            var en = dictionary.GetEnumerator();
            while (en.MoveNext())
            {
                writer.WriteStartArray();
                serializer.Serialize(writer, en.Key.ToString());
                serializer.Serialize(writer, en.Value);
                writer.WriteEndArray();
            }

            writer.WriteEndArray();
        }

        public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
        {
            var (_, listType, enumerableType, args) = GetTypes(objectType);

            var list = ((IList)(serializer.Deserialize(reader, listType)));

            var ci = objectType.GetConstructor(new[] { enumerableType });
            if (ci == null)
            {
                ci = typeof(Dictionary<,>).MakeGenericType(args).GetConstructor(new[] { enumerableType });
            }

            var dict = (IDictionary)ci!.Invoke(new object[] { list });

            return dict;
        }

        public override bool CanConvert(Type objectType)
        {
            if (!objectType.IsGenericType) return objectType.IsAssignableTo(typeof(IDictionary));

            var args = objectType.GenericTypeArguments;
            return args.Length == 2 && objectType.IsAssignableTo(typeof(IDictionary<,>).MakeGenericType(args));
        }
    }

    public class DeserializeData
    {
        private string _basePath;
        private string _outPath;

        public DeserializeData(string basePath, string outPath)
        {
            _basePath = basePath;
            _outPath = outPath;
        }

        private string toJson(object obj)
        {
            JsonSerializerSettings settings = new JsonSerializerSettings();
            settings.Formatting = Formatting.Indented;
            settings.NullValueHandling = NullValueHandling.Ignore;
            settings.MissingMemberHandling = MissingMemberHandling.Ignore;
            settings.Converters = new JsonConverter[] { new StringEnumConverter()/*, new ListDictionaryConverter()*/ };
            settings.Error = (sender, args) =>
            {
                args.ErrorContext.Handled = true;
            };

            return JsonConvert.SerializeObject(obj, settings);
        }

        private void convertBytesToJson(string name, string fixUp = "")
        {
            Console.Write(string.Format("Parsing {0}...", name));
            try
            {
                BinaryFormatter formatter = new BinaryFormatter();
                using (FileStream fs = File.Open(Path.Combine(_basePath, name), FileMode.Open))
                {
                    object obj = formatter.Deserialize(fs);

                    using (FileStream fsJson = File.Create(Path.Combine(_outPath, name + ".json")))
                    {
                        string value = toJson(obj);
                        if (!string.IsNullOrEmpty(fixUp))
                        {
                            int i = 0;
                            value = Regex.Replace(value, fixUp, (match) => string.Format("{0}{1} ", fixUp, i++));
                        }

                        byte[] info = new System.Text.UTF8Encoding(true).GetBytes(value);
                        fsJson.Write(info, 0, info.Length);
                    }
                }
            }
            catch(Exception ex)
            {
                Console.WriteLine("failed!");
                return;
            }

            Console.WriteLine("done!");
        }

        public void Start()
        {
            convertBytesToJson("GSAccessoryItems");
            convertBytesToJson("GSAccessoryStatGeneration");
            convertBytesToJson("GSAccessoryStatGrowth", "GSAccessoryKey1");
            convertBytesToJson("GSAccessoryUpgrading", "GSAccessoryKey");
            convertBytesToJson("GSBaseStat");
            convertBytesToJson("GSBattle");
            convertBytesToJson("GSBattleEnemy");
            convertBytesToJson("GSBattleModifier");
            convertBytesToJson("GSCharacter");
            convertBytesToJson("GSCutSceneDialogue");
            convertBytesToJson("GSCutSceneStory");
            convertBytesToJson("GSEffect");
            convertBytesToJson("GSEffectType");
            convertBytesToJson("GSEpisodes");
            convertBytesToJson("GSGear");
            convertBytesToJson("GSGlossary");
            convertBytesToJson("GSGearLevel");
            convertBytesToJson("GSItem");
            convertBytesToJson("GSLevel");
            convertBytesToJson("GSMissionEffects");
            convertBytesToJson("GSMissionNodes");
            convertBytesToJson("GSMissionObjective");
            convertBytesToJson("GSMissionRewards");
            convertBytesToJson("GSMissions");
            convertBytesToJson("GSMorale");
            convertBytesToJson("GSNodeEncounter");
            convertBytesToJson("GSNodeExploration");
            convertBytesToJson("GSNodeMapData");
            convertBytesToJson("GSNodeReplayRewards");
            convertBytesToJson("GSNodeRewards");
            convertBytesToJson("GSProperties");
            convertBytesToJson("GSPvPLeagues");
            convertBytesToJson("GSQuip");
            convertBytesToJson("GSRank");
            convertBytesToJson("GSRarity");
            convertBytesToJson("GSReplayRewards");
            convertBytesToJson("GSShuttlecraft");
            convertBytesToJson("GSSkill");
            convertBytesToJson("GSSkillUpgrade");
        }
    }

    public class Utils
    {
        public static string GetLastAssetUrl(int last_known)
        {
            string assetBundleLocation = getLatestGood(last_known);

            return string.Format("{0}/OSX/OSXRed", assetBundleLocation);
        }

        static async Task<string> getConfig(int buildNumber)
        {
            try
            {
                var url = string.Format("http://cdn0.client-files.proj-red.emeraldcitygames.ca/endpoints/stable1/v{0}/stable1-OSX-release-endpoint.json?ts=timestamp", buildNumber);

                using var client = new HttpClient();

                var content = await client.GetStringAsync(url);

                return JObject.Parse(content).SelectToken("assetBundleLocation").Value<string>();
            }
            catch
            {
                return string.Empty;
            }
        }

        static string getLatestGood(int last_known)
        {
            List<string> urls = new List<string>();

            var tasks = new List<Task>();

            for (int i = last_known; i < last_known + 10; i++)
            {
                tasks.Add(getConfig(i).ContinueWith((url) =>
                {
                    lock (urls)
                    {
                        urls.Add(url.Result);
                    }
                }));
            }

            Task t = Task.WhenAll(tasks);
            try
            {
                t.Wait();
            }
            catch { }

            urls.Sort();

            return urls[urls.Count - 1];
        }
    }
}