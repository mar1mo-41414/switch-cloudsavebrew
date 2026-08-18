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
            throw new FileNotFoundException($"config file not found: {path}", path);

        var yaml = File.ReadAllText(path);
        var config = Deserializer.Deserialize<AppConfig>(yaml)
            ?? throw new InvalidDataException($"config file is empty or invalid: {path}");

        Validate(config, path);
        return config;
    }

    private static void Validate(AppConfig config, string path)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(config.Remote.GitUrl))
            errors.Add("remote.git_url is required");
        if (string.IsNullOrWhiteSpace(config.Keys.ProdKeysPath))
            errors.Add("keys.prod_keys_path is required");
        if (string.IsNullOrWhiteSpace(config.Keys.SdSeed))
            errors.Add("keys.sd_seed is required");

        for (var i = 0; i < config.Games.Count; i++)
        {
            var game = config.Games[i];
            if (string.IsNullOrWhiteSpace(game.TitleId))
                errors.Add($"games[{i}].title_id is required");
            if (game.Targets.Count == 0)
                errors.Add($"games[{i}].targets must contain at least one entry");

            for (var j = 0; j < game.Targets.Count; j++)
            {
                var target = game.Targets[j];
                if (string.IsNullOrWhiteSpace(target.Emulator))
                    errors.Add($"games[{i}].targets[{j}].emulator is required");
                if (string.IsNullOrWhiteSpace(target.SavePath))
                    errors.Add($"games[{i}].targets[{j}].save_path is required");
            }
        }

        if (errors.Count > 0)
            throw new InvalidDataException(
                $"invalid config ({path}):\n" + string.Join('\n', errors.Select(e => $"  - {e}")));
    }
}
