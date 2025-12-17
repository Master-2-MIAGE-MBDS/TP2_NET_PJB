using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace Gauniv.WebServer.Security
{
    internal sealed class BearerSecuritySchemeTransformer(IAuthenticationSchemeProvider authenticationSchemeProvider)
        : IOpenApiDocumentTransformer
    {
        public async Task TransformAsync(OpenApiDocument document, OpenApiDocumentTransformerContext context,
            CancellationToken cancellationToken)
        {
            IEnumerable<AuthenticationScheme> authenticationSchemes =
                await authenticationSchemeProvider.GetAllSchemesAsync();
            foreach (AuthenticationScheme authScheme in authenticationSchemes)
            {
                Console.WriteLine(authScheme.Name); // Affichez les noms pour vérifier
            }

            if (authenticationSchemes.Any(authScheme => authScheme.Name == "Identity.Bearer"))
            {
                // Add the security scheme at the document level
                Dictionary<string, IOpenApiSecurityScheme> securitySchemes = new()
                {
                    ["Bearer"] = new OpenApiSecurityScheme
                    {
                        Type = SecuritySchemeType.Http,
                        Scheme = "bearer", // "bearer" refers to the header name here
                        In = ParameterLocation.Header,
                        BearerFormat = "Json Web Token"
                    }
                };
                document.Components ??= new OpenApiComponents();
                document.Components.SecuritySchemes = securitySchemes;

                // Apply it as a requirement for all operations
                foreach (KeyValuePair<HttpMethod, OpenApiOperation> operation in
                         document.Paths.Values.SelectMany(path => path.Operations))
                {
                    operation.Value.Security ??= [];
                    operation.Value.Security.Add(new OpenApiSecurityRequirement
                    {
                        [new OpenApiSecuritySchemeReference("Bearer", document)] = []
                    });
                }
            }
        }
    }
}