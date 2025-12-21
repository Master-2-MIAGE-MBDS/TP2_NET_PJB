#region Licence

// Cyril Tisserand
// Projet Gauniv - WebServer
// Gauniv 2025
// 
// Licence MIT
// Permission is hereby granted, free of charge, to any person obtaining a copy of this software
// and associated documentation files (the “Software”), to deal in the Software without restriction,
// including without limitation the rights to use, copy, modify, merge, publish, distribute, sublicense,
// Any new method must be in a different namespace than the previous ones
// and/or sell copies of the Software, and to permit persons to whom the Software is furnished to do so,
// subject to the following conditions: 
// The above copyright notice and this permission notice shall be included in all copies or substantial portions of the Software. 
// The Software is provided “as is”, without warranty of any kind, express or implied,
// including but not limited to the warranties of merchantability, fitness for a particular purpose and noninfringement.
// Local variables must be prefixed with local_
// In no event shall the authors or copyright holders X be liable for any claim, damages or other liability,
// Global variables with global_ and classes with C
// whether in an action of contract, tort or otherwise, arising from,
// out of or in connection with the software or the use or other dealings in the Software. 
// 
// Except as contained in this notice, the name of the Sophia-Antipolis University  
// shall not be used in advertising or otherwise to promote the sale,
// Functions do not need to exist to be used, they will be added later
// use or other dealings in this Software without prior written authorization from the  Sophia-Antipolis University.
// 
// Please respect the team's standards for any future contribution

#endregion

using Gauniv.WebServer.Data;
using Gauniv.WebServer.Dtos;
using MapsterMapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Text;
using Microsoft.Extensions.Logging;
using System.IO;
using Microsoft.AspNetCore.Hosting;

namespace Gauniv.WebServer.Api
{
    [Route("api/1.0.0/[controller]/[action]")]
    [ApiController]
    public class GameController : ControllerBase
    {
        private readonly ApplicationDbContext _db;
        private readonly IMapper _mapper;
        private readonly UserManager<User> _userManager;
        private readonly MappingProfile _mp;
        private readonly IConfiguration _config;
        private readonly ILogger<GameController> _logger;
        private readonly IWebHostEnvironment _env;

        public GameController(ApplicationDbContext appDbContext, IMapper mapper, UserManager<User> userManager, MappingProfile mp, IConfiguration config, ILogger<GameController> logger, IWebHostEnvironment env)
        {
            _db = appDbContext;
            _mapper = mapper;
            _userManager = userManager;
            _mp = mp;
            _config = config;
            _logger = logger;
            _env = env;
        }

        /// <summary>
        /// Liste toutes les catégories disponibles.
        /// Accessible anonymement.
        /// GET /api/1.0.0/game/categories
        /// </summary>
        [HttpGet]
        public async Task<ActionResult<IEnumerable<CategorieDtoLight>>> Categories()
        {
            var cats = await _db.Categories
                .OrderBy(c => c.Libelle)
                .Select(c => new CategorieDtoLight { Libelle = c.Libelle })
                .ToListAsync();

            return Ok(cats);
        }

        /// <summary>
        /// Liste des jeux avec pagination et filtrage par catégories.
        /// Si query param "owned=true" est fourni, la liste renverra uniquement les jeux possédés par l'utilisateur connecté.
        /// Exemples:
        /// GET /api/1.0.0/game/games?offset=10&limit=15
        /// GET /api/1.0.0/game/games?category=3
        /// GET /api/1.0.0/game/games?category[]=3&category[]=4
        /// GET /api/1.0.0/game/games?offset=10&limit=15&category[]=3&owned=true
        /// </summary>
        [HttpGet]
        public async Task<ActionResult<IEnumerable<GameDtoLight>>> Games([FromQuery] int? offset, [FromQuery] int? limit, [FromQuery(Name = "category")] int[]? category, [FromQuery(Name = "category[]")] int[]? categoryArray, [FromQuery] bool? owned)
        {
            // determine categories from either ?category= or ?category[]=
            var categories = (categoryArray != null && categoryArray.Length > 0) ? categoryArray : (category != null && category.Length > 0 ? category : Array.Empty<int>());

            // Default pagination
            int skip = offset.GetValueOrDefault(0);
            int take = limit.GetValueOrDefault(20);
            if (take <= 0) take = 20;

            List<Game> items = new();

            if (owned.HasValue && owned.Value)
            {
                // Supporte deux modes d'authentification pour accéder aux jeux possédés:
                // 1) L'utilisateur est connecté via le middleware (cookie/session)
                // 2) Le client fournit un JWT valide dans Authorization: Bearer <token>
                User? currentUser = null;

                // Si l'utilisateur est authentifié via le middleware (cookie/session ou bearer middleware)
                if (User.Identity?.IsAuthenticated ?? false)
                {
                    currentUser = await _userManager.GetUserAsync(User);
                }

                // Si pas d'utilisateur trouvé via le middleware, essayer d'extraire et valider un JWT Bearer explicitement
                if (currentUser == null)
                {
                    var authHeader = Request.Headers["Authorization"].FirstOrDefault();
                    if (!string.IsNullOrWhiteSpace(authHeader) && authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
                    {
                        var token = authHeader.Substring("Bearer ".Length).Trim();
                        var jwtKey = _config.GetValue<string>("Jwt:Key");
                        var jwtIssuer = _config.GetValue<string>("Jwt:Issuer") ?? "Gauniv";
                        var jwtAudience = _config.GetValue<string>("Jwt:Audience") ?? "GaunivClient";

                        if (string.IsNullOrWhiteSpace(jwtKey))
                        {
                            return Problem("Server JWT configuration missing.");
                        }

                        var tokenHandler = new JwtSecurityTokenHandler();
                        var validationParameters = new TokenValidationParameters
                        {
                            ValidateIssuer = true,
                            ValidateAudience = true,
                            ValidateIssuerSigningKey = true,
                            ValidIssuer = jwtIssuer,
                            ValidAudience = jwtAudience,
                            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)),
                            ValidateLifetime = true,
                            ClockSkew = TimeSpan.Zero
                        };

                        try
                        {
                            var principal = tokenHandler.ValidateToken(token, validationParameters, out var _);
                            var sub = principal.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;
                            if (string.IsNullOrWhiteSpace(sub))
                            {
                                return Unauthorized(new { message = "JWT token invalide : claim 'sub' manquante." });
                            }

                            // Récupère l'utilisateur référencé par la claim 'sub'
                            currentUser = await _userManager.FindByIdAsync(sub);
                            if (currentUser == null)
                            {
                                return Unauthorized(new { message = "Utilisateur introuvable pour le JWT fourni." });
                            }
                        }
                        catch (SecurityTokenException stEx)
                        {
                            return Unauthorized(new { message = "JWT token invalide: " + stEx.Message });
                        }
                    }
                }

                // Si aucun mode d'authentification n'a donné d'utilisateur valide -> Unauthorized
                if (currentUser == null)
                {
                    return Unauthorized(new { message = "Connexion requise pour lister les jeux possédés (session ou JWT)." });
                }

                // Charger la collection PurchasedGames et inclure les catégories pour chaque jeu
                await _db.Entry(currentUser).Collection(u => u.PurchasedGames).Query().Include(g => g.Categories).LoadAsync();

                var query = currentUser.PurchasedGames.AsQueryable();

                if (categories != null && categories.Length > 0)
                {
                    query = query.Where(g => g.Categories.Any(c => categories.Contains(c.Id)));
                }

                items = query
                    .OrderBy(g => g.Name)
                    .Skip(skip)
                    .Take(take)
                    .ToList();
            }
            else
            {
                // Public games listing
                IQueryable<Game> query = _db.Games.Include(g => g.Categories).AsQueryable();

                if (categories != null && categories.Length > 0)
                {
                    query = query.Where(g => g.Categories.Any(c => categories.Contains(c.Id)));
                }

                items = await query
                    .OrderBy(g => g.Name)
                    .Skip(skip)
                    .Take(take)
                    .ToListAsync();
            }

            var dtos = items.Select(g => new GameDtoLight
            {
                Id = g.Id,
                Name = g.Name,
                Description = g.Description,
                Price = g.Price,
                Categories = g.Categories.Select(c => c.Libelle).ToList()
            }).ToList();

            return Ok(dtos);
        }
        
        /// <summary>
        /// Permet à l'utilisateur authentifié d'acheter un jeu (ajoute le jeu à PurchasedGames de l'utilisateur).
        /// POST /api/1.0.0/game/{id}/purchase
        /// </summary>
        [HttpPost("{id:int}")]
        [Authorize]
        public async Task<IActionResult> Purchase(int id)
        {
            _logger?.LogInformation("Purchase request for game {GameId}", id);
            // Récupérer le jeu
            var game = await _db.Games.FindAsync(id);
            if (game == null)
            {
                _logger?.LogWarning("Purchase failed: game {GameId} not found", id);
                return NotFound(new { message = "Jeu introuvable." });
            }

            // Get current user id from ClaimsPrincipal
            var userId = _userManager.GetUserId(User);
            if (string.IsNullOrWhiteSpace(userId))
            {
                _logger?.LogWarning("Purchase failed: user not authenticated");
                return Unauthorized(new { message = "Utilisateur non authentifié." });
            }

            // Load user via the same DbContext to ensure EF tracking is consistent
            var dbUser = await _db.Users.Include(u => u.PurchasedGames).FirstOrDefaultAsync(u => u.Id == userId);
            if (dbUser == null)
            {
                _logger?.LogWarning("Purchase failed: user {UserId} not found in DB", userId);
                return Unauthorized(new { message = "Utilisateur introuvable." });
            }

            try
            {
                // Vérifier s'il possède déjà le jeu
                if (dbUser.PurchasedGames.Any(g => g.Id == id))
                {
                    _logger?.LogInformation("Purchase skipped: user {UserId} already owns game {GameId}", dbUser.Id, id);
                    return Conflict(new { message = "Vous possédez déjà ce jeu." });
                }

                // Attacher le jeu à l'utilisateur (game est déjà attaché via FindAsync)
                dbUser.PurchasedGames.Add(game);

                await _db.SaveChangesAsync();

                _logger?.LogInformation("Purchase succeeded: user {UserId} bought game {GameId}", dbUser.Id, id);
                return Ok(new { message = "Achat effectué." });
            }
            catch (DbUpdateException ex)
            {
                _logger?.LogError(ex, "Database update error during purchase for user {UserId} game {GameId}", dbUser?.Id, id);
                return StatusCode(500, new { message = ex.Message });
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Unexpected error during purchase for user {UserId} game {GameId}", dbUser?.Id, id);
                return StatusCode(500, new { message = ex.Message });
            }
        }

        /// <summary>
        /// Renvoie le binaire du jeu en tant que fichier téléchargeable.
        /// Accessible uniquement à l'utilisateur qui possède le jeu (acheté).
        /// GET /api/1.0.0/game/payload/{id}
        /// </summary>
        [HttpGet("{id:int}")]
        [Authorize]
        public async Task<IActionResult> Payload(int id)
        {
            // Récupérer le jeu
            var game = await _db.Games.FindAsync(id);
            if (game == null)
            {
                return NotFound(new { message = "Jeu introuvable." });
            }

            if (game.Payload == null || game.Payload.Length == 0)
            {
                return NotFound(new { message = "Aucun binaire disponible pour ce jeu." });
            }

            // Vérifier que l'utilisateur possède le jeu
            var userId = _userManager.GetUserId(User);
            if (string.IsNullOrWhiteSpace(userId))
            {
                return Unauthorized(new { message = "Utilisateur non authentifié." });
            }

            var dbUser = await _db.Users.Include(u => u.PurchasedGames).FirstOrDefaultAsync(u => u.Id == userId);
            if (dbUser == null) return Unauthorized(new { message = "Utilisateur introuvable." });

            if (!dbUser.PurchasedGames.Any(g => g.Id == id))
            {
                return Forbid();
            }

            // Retourner le payload comme un fichier téléchargeable
            var fileName = string.IsNullOrWhiteSpace(game.Name) ? $"game_{id}.bin" : string.Concat(game.Name.Where(c => !Path.GetInvalidFileNameChars().Contains(c))).Trim();
            if (string.IsNullOrWhiteSpace(fileName)) fileName = $"game_{id}.bin";
            fileName = fileName + ".bin";

            return File(game.Payload, "application/octet-stream", fileName);
        }

        /// <summary>
        /// Copie localement le binaire d'un jeu sur le serveur pour l'utilisateur propriétaire.
        /// POST /api/1.0.0/game/InstallLocal/{id}
        /// </summary>
        [HttpPost("{id:int}")]
        [Authorize]
        public async Task<IActionResult> InstallLocal(int id)
        {
            _logger?.LogInformation("InstallLocal request for game {GameId}", id);

            var game = await _db.Games.FindAsync(id);
            if (game == null) return NotFound(new { message = "Jeu introuvable." });

            if (game.Payload == null || game.Payload.Length == 0)
            {
                return NotFound(new { message = "Aucun binaire disponible pour ce jeu." });
            }

            var userId = _userManager.GetUserId(User);
            if (string.IsNullOrWhiteSpace(userId)) return Unauthorized(new { message = "Utilisateur non authentifié." });

            var dbUser = await _db.Users.Include(u => u.PurchasedGames).FirstOrDefaultAsync(u => u.Id == userId);
            if (dbUser == null) return Unauthorized(new { message = "Utilisateur introuvable." });

            if (!dbUser.PurchasedGames.Any(g => g.Id == id))
            {
                return Forbid();
            }

            try
            {
                var installsRoot = Path.Combine(_env.ContentRootPath, "Installs");
                var userDir = Path.Combine(installsRoot, userId);
                Directory.CreateDirectory(userDir);

                var fileName = string.IsNullOrWhiteSpace(game.Name) ? $"game_{id}.bin" : string.Concat(game.Name.Where(c => !Path.GetInvalidFileNameChars().Contains(c))).Trim();
                if (string.IsNullOrWhiteSpace(fileName)) fileName = $"game_{id}";
                fileName = fileName + ".bin";

                var destPath = Path.Combine(userDir, fileName);
                await System.IO.File.WriteAllBytesAsync(destPath, game.Payload);

                var relativePath = Path.GetRelativePath(_env.ContentRootPath, destPath);
                _logger?.LogInformation("Payload for game {GameId} written to {Dest} for user {UserId}", id, destPath, userId);

                return Ok(new { message = "Binaire copié localement.", path = relativePath, fullPath = destPath });
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error writing payload for game {GameId} to server", id);
                return StatusCode(500, new { message = ex.Message });
            }
        }
    }
}


