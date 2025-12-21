using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Gauniv.WebServer.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Gauniv.WebServer.Services
{
    /// <summary>
    /// Utility to seed games from a JSON file (ToDB_Games.json).
    /// Usage: await AddDataToDB_Games.SeedAsync(dbContext, jsonPath, logger);
    /// </summary>
    public static class AddDataToDB_Games
    {
        private class GameDto
        {
            [JsonPropertyName("name")] public string? Name { get; set; }
            [JsonPropertyName("description")] public string? Description { get; set; }
            [JsonPropertyName("price")] public decimal? Price { get; set; }
            [JsonPropertyName("payload")] public string? Payload { get; set; }
            [JsonPropertyName("genres")] public List<string>? Genres { get; set; }
        }

        private class GameContainer
        {
            [JsonPropertyName("games")] public List<GameDto>? Games { get; set; }
        }

        public static async Task SeedAsync(ApplicationDbContext db, string jsonPath, ILogger? logger = null, CancellationToken cancellationToken = default)
        {
            if (db is null) throw new ArgumentNullException(nameof(db));

            if (string.IsNullOrWhiteSpace(jsonPath))
            {
                logger?.LogWarning("Seed games: jsonPath is null or empty, skipping.");
                return;
            }

            if (!File.Exists(jsonPath))
            {
                logger?.LogWarning("Seed games: file not found: {path}", jsonPath);
                return;
            }

            string local_json;
            try
            {
                local_json = await File.ReadAllTextAsync(jsonPath, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                logger?.LogError(ex, "Failed to read games file {path}", jsonPath);
                return;
            }

            GameContainer? local_container;
            try
            {
                local_container = JsonSerializer.Deserialize<GameContainer>(local_json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });
            }
            catch (JsonException jex)
            {
                logger?.LogError(jex, "Invalid JSON in games file {path}", jsonPath);
                return;
            }

            var local_games = local_container?.Games;
            if (local_games == null || local_games.Count == 0)
            {
                logger?.LogInformation("No games found in {path}", jsonPath);
                return;
            }

            // Normalize entries: trim, ignore empty names, truncate to DB limits, deduplicate by name (case-insensitive)
            var normalized = local_games
                .Where(g => g != null && !string.IsNullOrWhiteSpace(g.Name))
                .Select(g => new GameDto
                {
                    Name = g!.Name!.Trim(),
                    Description = string.IsNullOrWhiteSpace(g.Description) ? string.Empty : g.Description!.Trim(),
                    Price = g.Price ?? 0m,
                    Payload = g.Payload ?? string.Empty,
                    Genres = g.Genres == null ? new List<string>() : g.Genres.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x!.Trim()).Where(x => !string.IsNullOrWhiteSpace(x)).ToList()
                })
                .Select(g => {
                    // truncate to match model constraints
                    if (g.Name!.Length > 150) g.Name = g.Name.Substring(0, 150);
                    if (g.Description != null && g.Description.Length > 1000) g.Description = g.Description.Substring(0, 1000);
                    return g;
                })
                .GroupBy(g => g.Name!, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .ToList();

            if (normalized.Count == 0)
            {
                logger?.LogInformation("No valid games to import after normalization from {path}", jsonPath);
                return;
            }

            try
            {
                // Load existing game names to avoid duplicates
                var existingNames = await db.Games
                    .Select(g => g.Name.ToLowerInvariant())
                    .ToListAsync(cancellationToken)
                    .ConfigureAwait(false);

                var toAddDtos = normalized
                    .Where(g => !existingNames.Contains(g.Name!.ToLowerInvariant()))
                    .ToList();

                if (toAddDtos.Count == 0)
                {
                    logger?.LogInformation("No new games to add. ({count} present)", existingNames.Count);
                    return;
                }

                var providerName = db.Database.ProviderName ?? string.Empty;
                bool supportsTransactions = !providerName.Contains("InMemory", StringComparison.OrdinalIgnoreCase);

                if (supportsTransactions)
                {
                    await using var tx = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

                    var created = await CreateGamesAndAttachCategories(db, toAddDtos, jsonPath, logger, cancellationToken).ConfigureAwait(false);

                    await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                    await tx.CommitAsync(cancellationToken).ConfigureAwait(false);

                    logger?.LogInformation("Added {count} new games from {path}", created, jsonPath);
                }
                else
                {
                    var created = await CreateGamesAndAttachCategories(db, toAddDtos, jsonPath, logger, cancellationToken).ConfigureAwait(false);
                    await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                    logger?.LogInformation("Added {count} new games (no transaction) from {path}", created, jsonPath);
                }
            }
            catch (Exception ex)
            {
                logger?.LogError(ex, "Failed while seeding games from {path}", jsonPath);
                throw;
            }
        }

        private static async Task<int> CreateGamesAndAttachCategories(ApplicationDbContext db, List<GameDto> dtos, string jsonPath, ILogger? logger, CancellationToken cancellationToken)
        {
            int createdCount = 0;

            // Load categories once for efficient matching (case-insensitive dictionary)
            var categories = await db.Categories.ToListAsync(cancellationToken).ConfigureAwait(false);
            var catDict = categories
                .GroupBy(c => c.Libelle, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

            foreach (var dto in dtos)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var game = new Game
                {
                    Name = dto.Name ?? string.Empty,
                    Description = dto.Description ?? string.Empty,
                    Price = dto.Price ?? 0m,
                    Payload = ConvertPayload(dto.Payload, logger, jsonPath)
                };

                // Attach categories if found
                if (dto.Genres != null && dto.Genres.Count > 0)
                {
                    foreach (var genre in dto.Genres.Distinct(StringComparer.OrdinalIgnoreCase))
                    {
                        if (catDict.TryGetValue(genre, out var cat))
                        {
                            game.Categories.Add(cat);
                        }
                        else
                        {
                            logger?.LogWarning("Game '{name}': category '{genre}' not found in DB (file {path})", dto.Name, genre, jsonPath);
                        }
                    }
                }

                db.Games.Add(game);
                createdCount++;
            }

            return createdCount;
        }

        private static byte[] ConvertPayload(string? payload, ILogger? logger, string jsonPath)
        {
            if (string.IsNullOrWhiteSpace(payload)) return new byte[0];
            
            try
            {
                // Resolve the payload path relative to the JSON file location
                var baseDirectory = Path.GetDirectoryName(jsonPath) ?? Directory.GetCurrentDirectory();
                var filePath = Path.Combine(baseDirectory, payload);
                
                // Check if file exists
                if (!File.Exists(filePath))
                {
                    logger?.LogWarning("Payload file not found at {filePath} (relative to {jsonPath})", filePath, jsonPath);
                    return new byte[0];
                }
                
                // Read the binary file
                var data = File.ReadAllBytes(filePath);
                logger?.LogInformation("Loaded payload file {filePath} ({size} bytes)", filePath, data.Length);
                return data;
            }
            catch (Exception ex)
            {
                logger?.LogWarning(ex, "Failed to read payload file {payload} from {jsonPath}", payload, jsonPath);
                return new byte[0];
            }
        }
    }
}

