using AddressablesTools;
using AddressablesTools.Catalog;
using AssetsTools.NET;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

static bool IsUnityFS(string path)
{
    const string unityFs = "UnityFS";
    using AssetsFileReader reader = new AssetsFileReader(path);
    if (reader.BaseStream.Length < unityFs.Length)
    {
        return false;
    }

    return reader.ReadStringLength(unityFs.Length) == unityFs;
}

static void SearchAsset(string[] args)
{
    bool fromBundle = IsUnityFS(args[1]);

    ContentCatalogData ccd;
    if (fromBundle)
        ccd = AddressablesJsonParser.FromBundle(args[1]);
    else
        ccd = AddressablesJsonParser.FromString(File.ReadAllText(args[1]));

    Console.Write("search key to find bundles of: ");
    string? search = Console.ReadLine();

    if (search == null)
    {
        return;
    }

    search = search.ToLower();

    foreach (object k in ccd.Resources.Keys)
    {
        if (k is string s && s.ToLower().Contains(search))
        {
            Console.Write(s);
            foreach (var rsrc in ccd.Resources[s])
            {
                Console.WriteLine($" ({rsrc.ProviderId})");
                if (rsrc.ProviderId == "UnityEngine.ResourceManagement.ResourceProviders.BundledAssetProvider")
                {
                    List<ResourceLocation> o = ccd.Resources[rsrc.Dependency];
                    Console.WriteLine($"  {o[0].InternalId}");
                    if (o.Count > 1)
                    {
                        for (int i = 1; i < o.Count; i++)
                        {
                            Console.WriteLine($"    {o[i].InternalId}");
                        }
                    }
                }
            }
        }
    }
}

static void PatchCrc(string[] args)
{
    bool fromBundle = IsUnityFS(args[1]);

    ContentCatalogData ccd;
    if (fromBundle)
        ccd = AddressablesJsonParser.FromBundle(args[1]);
    else
        ccd = AddressablesJsonParser.FromString(File.ReadAllText(args[1]));

    Console.WriteLine("patching...");

    foreach (var resourceList in ccd.Resources.Values)
    {
        foreach (var rsrc in resourceList)
        {
            if (rsrc.ProviderId == "UnityEngine.ResourceManagement.ResourceProviders.AssetBundleProvider")
            {
                var data = rsrc.Data;
                if (data != null && data is ClassJsonObject classJsonObject)
                {
                    JsonSerializerOptions options = new JsonSerializerOptions()
                    {
                        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
                    };

                    JsonObject? jsonObj = JsonSerializer.Deserialize<JsonObject>(classJsonObject.JsonText);
                    if (jsonObj != null)
                    {
                        jsonObj["m_Crc"] = 0;
                        classJsonObject.JsonText = JsonSerializer.Serialize(jsonObj, options);
                        rsrc.Data = classJsonObject;
                    }
                }
            }
        }
    }

    if (fromBundle)
        AddressablesJsonParser.ToBundle(ccd, args[1], args[1] + ".patched");
    else
        File.WriteAllText(args[1] + ".patched", AddressablesJsonParser.ToJson(ccd));

    File.Move(args[1], args[1] + ".old");
    File.Move(args[1] + ".patched", args[1]);
}

static void ExtractAssetList(string[] args)
{
    var isWin = args[1].Contains("win64");

    bool fromBundle = IsUnityFS(args[1]);

    ContentCatalogData ccd;
    if (fromBundle)
        ccd = AddressablesJsonParser.FromBundle(args[1]);
    else
        ccd = AddressablesJsonParser.FromString(File.ReadAllText(args[1]));

    Dictionary<string, string> bundleHashes = new();

    foreach (var res in ccd.Resources)
    {
        var loc = res.Value;

        if (loc[0].InternalId.StartsWith("0#") || loc[0].InternalId.Contains(".bundle"))
        {
            if (loc[0].Data is ClassJsonObject data)
            {
                var doc = JsonDocument.Parse(data.JsonText);
                var root = doc.RootElement;
                var hash = root.GetProperty("m_Hash").GetString();
                if (hash != null)
                {
                    bundleHashes.TryAdd("0#/"+loc[0].PrimaryKey, hash);
                }
            }
        }
    }

    Dictionary<string, string> assetList = new();

    foreach (object k in ccd.Resources.Keys)
    {
        if (k is string s && !s.Contains(".bundle") && s.Contains("/"))
        {
            foreach (var rsrc in ccd.Resources[k])
            {
                if (rsrc.ProviderId == "UnityEngine.ResourceManagement.ResourceProviders.BundledAssetProvider")
                {
                    List<ResourceLocation> o = ccd.Resources[rsrc.Dependency];
                    assetList.TryAdd(s, o[0].PrimaryKey);
                }
            }
        }
    }

    var hashesJSON = JsonSerializer.Serialize(bundleHashes, new JsonSerializerOptions() { WriteIndented = true });
    using (StreamWriter writer = new StreamWriter(args[1].Replace(".json", "").Replace(".bundle", "")+"_hash.json"))
    {
        writer.Write(hashesJSON);
    }

    var listJSON = JsonSerializer.Serialize(assetList, new JsonSerializerOptions() { WriteIndented = true });
    using (StreamWriter writer = new StreamWriter(args[1].Replace(".json", "").Replace(".bundle", "") + "_list.json"))
    {
        writer.Write(listJSON);
    }

    Console.WriteLine(bundleHashes.Count);
    Console.WriteLine(assetList.Count);
}

if (args.Length < 1)
{
    Console.WriteLine("need args: <mode> <file>");
    Console.WriteLine("modes: search, patch, extract");
    return;
}

if (args.Length < 2)
{
    Console.WriteLine("Where is your catalog.json path?");
    return;
}

if (args[0] == "search")
{
    SearchAsset(args);
}
else if (args[0] == "patch")
{
    PatchCrc(args);
}
else if (args[0] == "extract")
{
    ExtractAssetList(args);
}
else
{
    Console.WriteLine("mode not supported");
}