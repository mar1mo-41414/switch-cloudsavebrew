using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace SwitchCloudSaveBrew.Core.Config;

public static class ConfigLoader
{
    private static readonly IDeserializer Deserializer = new DeserializerBuilder()
        .WithNamingConvention(UnderscoredNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .Build();

    public static AppConfig Load(string path)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException(L.Pick($"config.yaml not found: {path}", $"config.yamlが見つかりません: {path}"), path);

        var yaml = File.ReadAllText(path);
        var config = Deserializer.Deserialize<AppConfig>(yaml)
            ?? throw new InvalidDataException(L.Pick($"config.yaml is empty or invalid: {path}", $"config.yamlが空か不正です: {path}"));

        // Applies to every subsequent message this process prints,
        // including Validate() below and any Core code called after this
        // — the CLI is the only caller of Load() early enough for this to
        // matter (the GUI never calls Load() before its own language is
        // already set from GuiSettings).
        L.Current = L.Parse(config.Language);

        Validate(config, path);
        return config;
    }

    private static void Validate(AppConfig config, string path)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(config.Remote.GitUrl))
            errors.Add(L.Pick("remote.git_url is required", "remote.git_url は必須です"));
        if (string.IsNullOrWhiteSpace(config.Keys.ProdKeysPath))
            errors.Add(L.Pick("keys.prod_keys_path is required", "keys.prod_keys_path は必須です"));
        if (string.IsNullOrWhiteSpace(config.Keys.SdSeed))
            errors.Add(L.Pick("keys.sd_seed is required", "keys.sd_seed は必須です"));

        for (var i = 0; i < config.Games.Count; i++)
        {
            var game = config.Games[i];
            if (string.IsNullOrWhiteSpace(game.TitleId))
                errors.Add(L.Pick($"games[{i}].title_id is required", $"games[{i}].title_id は必須です"));
            if (game.Targets.Count == 0)
                errors.Add(L.Pick($"games[{i}].targets must contain at least one entry", $"games[{i}].targets には1つ以上のエントリが必要です"));

            for (var j = 0; j < game.Targets.Count; j++)
            {
                var target = game.Targets[j];
                if (string.IsNullOrWhiteSpace(target.Emulator))
                    errors.Add(L.Pick($"games[{i}].targets[{j}].emulator is required", $"games[{i}].targets[{j}].emulator は必須です"));
                if (string.IsNullOrWhiteSpace(target.SavePath))
                    errors.Add(L.Pick($"games[{i}].targets[{j}].save_path is required", $"games[{i}].targets[{j}].save_path は必須です"));
            }
        }

        if (errors.Count > 0)
            throw new InvalidDataException(
                L.Pick($"invalid config ({path}):\n", $"不正なconfig ({path}):\n") + string.Join('\n', errors.Select(e => $"  - {e}")));
    }
}
