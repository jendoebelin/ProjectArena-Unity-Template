// The Android permissions the native game's manifest has for online play and voice (native app/AndroidManifest.xml):
// INTERNET, ACCESS_NETWORK_STATE, ACCESS_WIFI_STATE, CHANGE_WIFI_MULTICAST_STATE (LAN games are found by broadcast,
// which Android filters on Wi-Fi without a multicast lock) and RECORD_AUDIO (voice chat; asked for when first needed);
// and the optional passthrough feature (SETTINGS > PASSTHROUGH, task D1).
// Added to the generated Gradle project's manifest on every Android build.
using System.IO;
using System.Xml;
using UnityEditor.Android;
using UnityEngine;

namespace ProjectArena.EditorTools
{
    public class AndroidPermissions : IPostGenerateGradleAndroidProject
    {
        public int callbackOrder => 10;

        static readonly string[] Permissions =
        {
            "android.permission.INTERNET", "android.permission.ACCESS_NETWORK_STATE", "android.permission.ACCESS_WIFI_STATE",
            "android.permission.CHANGE_WIFI_MULTICAST_STATE", "android.permission.RECORD_AUDIO",
        };

        public void OnPostGenerateGradleAndroidProject(string path)
        {
            string manifest = Path.Combine(path, "src", "main", "AndroidManifest.xml");
            if (!File.Exists(manifest)) { Debug.LogWarning("[Android] no manifest at " + manifest); return; }
            var doc = new XmlDocument();
            doc.Load(manifest);
            const string android = "http://schemas.android.com/apk/res/android";
            XmlElement root = doc.DocumentElement;
            int added = 0;
            foreach (string p in Permissions)
            {
                bool has = false;
                foreach (XmlNode n in root.SelectNodes("uses-permission"))
                    if (n is XmlElement e && e.GetAttribute("name", android) == p) has = true;
                if (has) continue;
                XmlElement el = doc.CreateElement("uses-permission");
                el.SetAttribute("name", android, p);
                root.PrependChild(el);
                ++added;
            }
            // SETTINGS > PASSTHROUGH: your room around the arena (optional; native's manifest)
            const string ptFeature = "com.oculus.feature.PASSTHROUGH";
            bool hasPt = false;
            foreach (XmlNode n in root.SelectNodes("uses-feature"))
                if (n is XmlElement e && e.GetAttribute("name", android) == ptFeature) hasPt = true;
            if (!hasPt)
            {
                XmlElement el = doc.CreateElement("uses-feature");
                el.SetAttribute("name", android, ptFeature);
                el.SetAttribute("required", android, "false");
                root.PrependChild(el);
            }
            doc.Save(manifest);
            Debug.Log($"[Android] {added} permissions added (online play, LAN discovery, voice chat)");
        }
    }
}
