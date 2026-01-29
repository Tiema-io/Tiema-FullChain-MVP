using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using Tiema.Runtime.Models;

namespace Tiema.Runtime
{
    internal class Utility
    {
        internal static TiemaConfig LoadConfiguration(string configPath)
        {
            if (!File.Exists(configPath))
                throw new FileNotFoundException($"配置文件不存在: {configPath}");

            var json = File.ReadAllText(configPath);
            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                ReadCommentHandling = JsonCommentHandling.Skip
            };

            return JsonSerializer.Deserialize<TiemaConfig>(json, options);
        }
    }
}
