using System.Text.Json.Nodes;

namespace PhasmaStrap.Networking
{
    public static class AssetWarpPolicy
    {
        private const string LOG_IDENT = "AssetWarpPolicy";

        public const string Host = "assetdelivery.roblox.com";

        private const string BatchFragment = "/v1/assets/batch";

        public static bool IsEnabled =>
            App.Settings.Prop.AssetWarpEnabled &&
            (App.Settings.Prop.AssetWarpDisableAllTextures ||
             App.Settings.Prop.AssetWarpDisableAllDecals ||
             App.Settings.Prop.AssetWarpDisableAllImages ||
             App.Settings.Prop.AssetWarpDisableAllAnimations ||
             App.Settings.Prop.AssetWarpDisableAllMeshes);

        public static byte[]? TransformRequest(ProxiedRequest request)
        {
            if (!IsEnabled)
                return null;

            if (!request.Method.Equals("POST", StringComparison.OrdinalIgnoreCase))
                return null;

            if (!request.Path.Contains(BatchFragment, StringComparison.OrdinalIgnoreCase))
                return null;

            if (request.Body.Length == 0)
                return null;

            JsonNode? parsed;
            try
            {
                parsed = JsonNode.Parse(request.Body);
            }
            catch (Exception)
            {
                return null;
            }

            if (parsed is not JsonArray source)
                return null;

            var output = new JsonArray();
            bool modified = false;

            foreach (JsonNode? node in source)
            {
                if (node is not JsonObject entry)
                    continue;

                string typeId = ReadString(entry["assetTypeId"]);
                string typeName = ReadString(entry["assetType"]).ToLowerInvariant();

                if (ShouldRemove(typeId, typeName))
                {
                    modified = true;
                    continue;
                }

                output.Add(JsonNode.Parse(entry.ToJsonString()));
            }

            if (!modified)
                return null;

            App.Logger.WriteLine(LOG_IDENT, $"Asset batch trimmed from {source.Count} to {output.Count} entries");
            return JsonSerializer.SerializeToUtf8Bytes(output);
        }

        private static bool ShouldRemove(string typeId, string typeName)
        {
            bool textures = App.Settings.Prop.AssetWarpDisableAllTextures && (typeId == "63" || typeName == "texture" || typeName == "texturepack");
            bool decals = App.Settings.Prop.AssetWarpDisableAllDecals && (typeId == "13" || typeName == "decal");
            bool images = App.Settings.Prop.AssetWarpDisableAllImages && (typeId == "1" || typeName == "image");
            bool animations = App.Settings.Prop.AssetWarpDisableAllAnimations && (typeId == "24" || typeName == "animation");
            bool meshes = App.Settings.Prop.AssetWarpDisableAllMeshes && (typeId == "40" || typeId == "4" || typeName == "mesh" || typeName == "meshpart");

            return textures || decals || images || animations || meshes;
        }

        private static string ReadString(JsonNode? node)
        {
            if (node is null)
                return "";

            return node.ToJsonString().Trim('"');
        }
    }
}
