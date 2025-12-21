using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Gauniv.WebServer.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Gauniv.WebServer.Services
{
    public static class AddDataToDB_Users
    {
        private class SeedUser
        {
            public string UserName { get; set; } = string.Empty;
            public string Email { get; set; } = string.Empty;
            public string? FirstName { get; set; }
            public string? LastName { get; set; }
            public string Password { get; set; } = string.Empty;
            public string[]? Roles { get; set; }
        }

        public static async Task SeedAsync(ApplicationDbContext db, string usersJsonPath, UserManager<User> userManager, RoleManager<IdentityRole> roleManager, ILogger? logger = null, CancellationToken cancellationToken = default)
        {
            if (db is null) throw new ArgumentNullException(nameof(db));
            if (userManager is null) throw new ArgumentNullException(nameof(userManager));
            if (roleManager is null) throw new ArgumentNullException(nameof(roleManager));

            if (string.IsNullOrWhiteSpace(usersJsonPath) || !File.Exists(usersJsonPath))
            {
                logger?.LogWarning("Users json path not provided or file not found: {path}", usersJsonPath);
                return;
            }

            var text = await File.ReadAllTextAsync(usersJsonPath, cancellationToken).ConfigureAwait(false);
            List<SeedUser>? entries = null;
            try
            {
                entries = JsonSerializer.Deserialize<List<SeedUser>>(text, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            }
            catch (Exception ex)
            {
                logger?.LogError(ex, "Failed to deserialize users json from {path}", usersJsonPath);
                throw;
            }

            if (entries == null || entries.Count == 0)
            {
                logger?.LogInformation("No users to seed in {path}", usersJsonPath);
                return;
            }

            // Ensure roles exist
            var allRoles = entries.SelectMany(e => e.Roles ?? Array.Empty<string>()).Distinct(StringComparer.OrdinalIgnoreCase);
            foreach (var r in allRoles)
            {
                if (string.IsNullOrWhiteSpace(r)) continue;
                if (!await roleManager.RoleExistsAsync(r))
                {
                    var res = await roleManager.CreateAsync(new IdentityRole(r));
                    if (!res.Succeeded)
                    {
                        logger?.LogWarning("Failed to create role {role}: {errors}", r, string.Join(";", res.Errors.Select(e => e.Description).ToArray()));
                    }
                }
            }

            foreach (var su in entries)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (string.IsNullOrWhiteSpace(su.Email))
                {
                    logger?.LogWarning("Skipping user with empty email");
                    continue;
                }

                var existing = await userManager.FindByEmailAsync(su.Email);
                if (existing == null)
                {
                    var user = new User
                    {
                        UserName = su.UserName ?? su.Email,
                        Email = su.Email,
                        FirstName = su.FirstName ?? string.Empty,
                        LastName = su.LastName ?? string.Empty,
                        EmailConfirmed = true
                    };

                    var createRes = await userManager.CreateAsync(user, su.Password);
                    if (!createRes.Succeeded)
                    {
                        logger?.LogWarning("Failed to create user {email}: {errors}", su.Email, string.Join(";", createRes.Errors.Select(e => e.Description).ToArray()));
                        continue;
                    }

                    if (su.Roles != null && su.Roles.Length > 0)
                    {
                        var rolesRes = await userManager.AddToRolesAsync(user, su.Roles);
                        if (!rolesRes.Succeeded)
                        {
                            logger?.LogWarning("Failed to add roles to user {email}: {errors}", su.Email, string.Join(";", rolesRes.Errors.Select(e => e.Description).ToArray()));
                        }
                    }

                    logger?.LogInformation("Seeded user {email}", su.Email);
                }
                else
                {
                    // Ensure roles assigned
                    if (su.Roles != null && su.Roles.Length > 0)
                    {
                        foreach (var r in su.Roles)
                        {
                            if (!await userManager.IsInRoleAsync(existing, r))
                            {
                                var addRes = await userManager.AddToRoleAsync(existing, r);
                                if (!addRes.Succeeded)
                                {
                                    logger?.LogWarning("Failed to add role {role} to existing user {email}: {errors}", r, su.Email, string.Join(";", addRes.Errors.Select(e => e.Description).ToArray()));
                                }
                            }
                        }
                    }
                }
            }

            // Optionally ensure changes persisted
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
    }
}
