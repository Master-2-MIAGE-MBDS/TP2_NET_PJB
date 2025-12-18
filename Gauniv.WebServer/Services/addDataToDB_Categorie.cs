using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
    /// Utility to seed categories from a JSON file (ToDB_Categories.json).
    /// Usage: await AddDataToDB_Categorie.SeedAsync(dbContext, jsonPath, logger);
    /// </summary>
    public static class AddDataToDB_Categorie
    {
        private class GenreContainer
        {
            [JsonPropertyName("genres")]
            public List<string>? Genres { get; set; }
        }

        public static async Task SeedAsync(ApplicationDbContext db, string jsonPath, ILogger? logger = null, CancellationToken cancellationToken = default)
        {
            if (db is null) throw new ArgumentNullException(nameof(db));

            if (string.IsNullOrWhiteSpace(jsonPath))
            {
                logger?.LogWarning("Seed categories: jsonPath is null or empty, skipping.");
                return;
            }

            if (!File.Exists(jsonPath))
            {
                logger?.LogWarning("Seed categories: file not found: {path}", jsonPath);
                return;
            }

            string local_json;
            try
            {
                local_json = await File.ReadAllTextAsync(jsonPath, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                logger?.LogError(ex, "Failed to read categories file {path}", jsonPath);
                return;
            }

            GenreContainer? local_container;
            try
            {
                local_container = JsonSerializer.Deserialize<GenreContainer>(local_json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });
            }
            catch (JsonException jex)
            {
                logger?.LogError(jex, "Invalid JSON in categories file {path}", jsonPath);
                return;
            }

            var local_genres = local_container?.Genres;
            if (local_genres == null || local_genres.Count == 0)
            {
                logger?.LogInformation("No genres found in {path}", jsonPath);
                return;
            }

            // Normalize: trim, ignore empty, truncate to 50 chars (Categorie.Libelle has MaxLength(50)), deduplicate (case-insensitive)
            var local_normalized = local_genres
                .Where(g => !string.IsNullOrWhiteSpace(g))
                .Select(g => g.Trim())
                .Where(g => !string.IsNullOrWhiteSpace(g))
                .Select(g => g.Length > 50 ? g.Substring(0, 50) : g)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (local_normalized.Count == 0)
            {
                logger?.LogInformation("No valid genres to import after normalization from {path}", jsonPath);
                return;
            }

            try
            {
                // Load existing libelles from DB (case-insensitive compare)
                var existingLibelles = await db.Categories
                    .Select(c => c.Libelle.ToLowerInvariant())
                    .ToListAsync(cancellationToken)
                    .ConfigureAwait(false);

                var local_toAdd = local_normalized
                    .Where(g => !existingLibelles.Contains(g.ToLowerInvariant()))
                    .Select(g => new Categorie { Libelle = g })
                    .ToList();

                if (local_toAdd.Count == 0)
                {
                    logger?.LogInformation("No new categories to add. ({count} present)", existingLibelles.Count);
                    return;
                }

                // Try to use a transaction if supported
                try
                {
                    // Detect if the current provider supports transactions (InMemory does not)
                    var providerName = db.Database.ProviderName ?? string.Empty;
                    bool supportsTransactions = !providerName.Contains("InMemory", StringComparison.OrdinalIgnoreCase);

                    if (supportsTransactions)
                    {
                        await using var local_tx = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

                        db.Categories.AddRange(local_toAdd);
                        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

                        await local_tx.CommitAsync(cancellationToken).ConfigureAwait(false);

                        logger?.LogInformation("Added {count} new categories from {path}", local_toAdd.Count, jsonPath);
                    }
                    else
                    {
                        // Provider doesn't support transactions (e.g. InMemory), perform simple save
                        db.Categories.AddRange(local_toAdd);
                        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                        logger?.LogInformation("Added {count} new categories (no transaction) from {path}", local_toAdd.Count, jsonPath);
                    }
                }
                catch (Exception ex)
                {
                    // Log and rethrow unexpected exceptions
                    logger?.LogError(ex, "Failed while seeding categories from {path}", jsonPath);
                    throw;
                }
            }
            catch (Exception ex)
            {
                logger?.LogError(ex, "Failed while seeding categories from {path}", jsonPath);
                throw;
            }
        }
    }
}
