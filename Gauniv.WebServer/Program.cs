#region Header

// Cyril Tisserand
// Projet Gauniv - WebServer
// Gauniv 2025
// 
// Licence MIT
// Permission is hereby granted, free of charge, to any person obtaining a copy of this software
// and associated documentation files (the "Software"), to deal in the Software without restriction,
// including without limitation the rights to use, copy, modify, merge, publish, distribute, sublicense,
// Any new method must be in a different namespace than the previous ones
// and/or sell copies of the Software, and to permit persons to whom the Software is furnished to do so,
// subject to the following conditions: 
// The above copyright notice and this permission notice shall be included in all copies or substantial portions of the Software. 
// The Software is provided "as is", without warranty of any kind, express or implied,
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
using Gauniv.WebServer.Security;
using Gauniv.WebServer.Services;
using Gauniv.WebServer.Websocket;
using Mapster;
using Microsoft.AspNetCore.Authentication.BearerToken;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Razor;
using Microsoft.EntityFrameworkCore;
using System.Globalization;
using SignInResult = Microsoft.AspNetCore.Identity.SignInResult;
using System.IO;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using System.Text;


// Set the culture so that the culture is the same between front and back
CultureInfo cultureInfo = new("en-US");
CultureInfo.DefaultThreadCurrentCulture = cultureInfo;
CultureInfo.DefaultThreadCurrentUICulture = cultureInfo;


WebApplicationBuilder builder = WebApplication.CreateBuilder(args);


builder.Services.AddLocalization(options => options.ResourcesPath = "Resources");

builder.Services.Configure<RequestLocalizationOptions>(s =>
{
    s.SupportedCultures =
    [
        cultureInfo
    ];
    s.SupportedUICultures =
    [
        cultureInfo
    ];
});
// Add services to the container.
string connectionString = builder.Configuration.GetConnectionString("DefaultConnection") ??
                          throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");
/*builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseNpgsql(connectionString));*/
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseInMemoryDatabase("Gauniv.db"));
builder.Services.AddDatabaseDeveloperPageExceptionFilter();

builder.Services.AddIdentityApiEndpoints<User>(options =>
{
    options.SignIn.RequireConfirmedAccount = false;
    options.Password.RequireDigit = false;
    options.Password.RequiredLength = 1;
    options.Password.RequireLowercase = false;
    options.Password.RequireNonAlphanumeric = false;
    options.Password.RequireUppercase = false;
}).AddRoles<IdentityRole>().AddEntityFrameworkStores<ApplicationDbContext>();

// Configure JWT authentication (reads config from appsettings: Jwt:Key, Jwt:Issuer, Jwt:Audience)
var jwtSection = builder.Configuration.GetSection("Jwt");
var jwtKey = jwtSection.GetValue<string>("Key") ?? throw new InvalidOperationException("Jwt:Key is not configured");
var jwtIssuer = jwtSection.GetValue<string>("Issuer") ?? "Gauniv";
var jwtAudience = jwtSection.GetValue<string>("Audience") ?? "GaunivClient";

// IMPORTANT: do not overwrite the default authentication scheme configured by Identity (cookies)
// Register JwtBearer handler without changing the defaults so the UI (cookie auth) keeps working.
builder.Services.AddAuthentication().AddJwtBearer(options =>
{
    options.RequireHttpsMetadata = false; // for dev
    options.SaveToken = true;
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateIssuerSigningKey = true,
        ValidIssuer = jwtIssuer,
        ValidAudience = jwtAudience,
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)),
        ValidateLifetime = true
    };
});

builder.Services.AddControllersWithViews().AddViewLocalization(LanguageViewLocationExpanderFormat.Suffix)
    .AddDataAnnotationsLocalization();
builder.Services.AddOpenApi(options =>
{
    options.AddDocumentTransformer<BearerSecuritySchemeTransformer>();
});
builder.Services.AddRazorPages();

builder.Services.AddMapster();
builder.Services.AddSignalR();
builder.Services.AddHostedService<OnlineService>();
builder.Services.AddHostedService<SetupService>();
builder.Services.AddScoped<MappingProfile, MappingProfile>();

WebApplication app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseMigrationsEndPoint();
}
else
{
    app.UseExceptionHandler("/Home/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

app.MapStaticAssets();

app.MapControllerRoute(
        "default",
        "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();

app.MapRazorPages()
    .WithStaticAssets();

app.MapOpenApi();
app.MapGroup("Bearer").MapPost("/login",
    async Task<Results<Ok<AccessTokenResponse>, EmptyHttpResult, ProblemHttpResult>>
    ([FromBody] LoginRequest login, [FromQuery] bool? useCookies, [FromQuery] bool? useSessionCookies,
        [FromServices] IServiceProvider sp) =>
    {
        SignInManager<User> signInManager = sp.GetRequiredService<SignInManager<User>>();

        bool useCookieScheme = useCookies == true || useSessionCookies == true;
        bool isPersistent = useCookies == true && useSessionCookies != true;
        signInManager.AuthenticationScheme =
            useCookieScheme ? IdentityConstants.ApplicationScheme : IdentityConstants.BearerScheme;

        SignInResult result = await signInManager.PasswordSignInAsync(login.Email, login.Password, isPersistent, true);

        if (result.RequiresTwoFactor)
        {
            if (!string.IsNullOrEmpty(login.TwoFactorCode))
            {
                result = await signInManager.TwoFactorAuthenticatorSignInAsync(login.TwoFactorCode, isPersistent,
                    isPersistent);
            }
            else if (!string.IsNullOrEmpty(login.TwoFactorRecoveryCode))
            {
                result = await signInManager.TwoFactorRecoveryCodeSignInAsync(login.TwoFactorRecoveryCode);
            }
        }

        if (!result.Succeeded)
        {
            return TypedResults.Problem(result.ToString(), statusCode: StatusCodes.Status401Unauthorized);
        }

        // The signInManager already produced the needed response in the form of a cookie or bearer token.
        return TypedResults.Empty;
    });

app.UseSwaggerUI(options =>
{
    options.SwaggerEndpoint("/openapi/v1.json", "v1");
});
app.MapHub<OnlineHub>("/online");

// Seed categories from ToDB_Categories.json
{
    using var scope = app.Services.CreateScope();
    var services = scope.ServiceProvider;
    try
    {
        var db = services.GetRequiredService<ApplicationDbContext>();
        var loggerFactory = services.GetService<ILoggerFactory>();
        var logger = loggerFactory?.CreateLogger("SeedDatabase");
        var categoriesJsonPath = Path.Combine(app.Environment.ContentRootPath ?? Directory.GetCurrentDirectory(), "ToDB_Categories.json");
        var gamesJsonPath = Path.Combine(app.Environment.ContentRootPath ?? Directory.GetCurrentDirectory(), "ToDB_Games.json");

        // call the aggregated seeder and wait for completion (categories then games)
        AddDataToDB_All.SeedAsync(db, categoriesJsonPath, gamesJsonPath, logger).GetAwaiter().GetResult();
    }
    catch (Exception ex)
    {
        var logger = scope.ServiceProvider.GetService<ILoggerFactory>()?.CreateLogger("SeedDatabase");
        logger?.LogError(ex, "An error occurred while seeding database on startup.");
    }
}

app.Run();