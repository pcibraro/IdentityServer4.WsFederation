using System;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using IdentityServer4;
using Xunit;
using Microsoft.Extensions.DependencyInjection;
using IdentityServer4.WsFederation;
using IdentityServer4.Services;
using Microsoft.Extensions.DependencyInjection.Extensions;
using IdentityServer4.Stores;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.ViewComponents;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using System.Reflection;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using IdentityServer4.WsFederation.Validation;
using IdentityServer4.Configuration;
using IdentityServer4.WsFederation.Stores;
using Microsoft.IdentityModel.Protocols.WsFederation;
using System.Net;

namespace IdentityServer4.WsFederation.Tests
{
    public class WsFederationTests
    {
        private readonly TestServer _server;
        private readonly HttpClient _client;
        public WsFederationTests()
        {
            var builder = new WebHostBuilder()
                 .ConfigureServices(InitializeServices)
                 .Configure(app =>
                 {
                     app.UseIdentityServer();
                     app.UseMvc(routes =>
                        routes.MapRoute(
                            "default",
                            "{controller}/{action=index}/{id?}"
                        )
                     );
                 });
                // .UseStartup<Startup>();
            _server = new TestServer(builder);
            _client = _server.CreateClient();
        }

        protected virtual void InitializeServices(IServiceCollection services)
        {
            var startupAssembly = typeof(Startup).GetTypeInfo().Assembly;
            var wsFedController = typeof(WsFederationController).GetTypeInfo().Assembly;

            // Inject a custom application part manager. Overrides AddMvcCore() because that uses TryAdd().
            var manager = new ApplicationPartManager();
            manager.ApplicationParts.Add(new AssemblyPart(startupAssembly));
            manager.ApplicationParts.Add(new AssemblyPart(wsFedController));

            manager.FeatureProviders.Add(new ControllerFeatureProvider());
            manager.FeatureProviders.Add(new ViewComponentFeatureProvider());

            services.AddSingleton(manager);
            services.TryAddSingleton<IKeyMaterialService>(
                new DefaultKeyMaterialService(
                    new IValidationKeysStore[] { }, 
                    new DefaultSigningCredentialsStore(TestCert.LoadSigningCredentials()))); 
            // TestLogger.Create<DefaultTokenCreationService>()));

            services.AddIdentityServer()
                .AddSigningCredential(TestCert.LoadSigningCredentials())
                .AddInMemoryClients(Config.GetClients())
                .AddInMemoryIdentityResources(Config.GetIdentityResources())
                // .AddInMemoryRelyingParties(new RelyingParty[] {})
                .AddWsFederation();
            services.TryAddTransient<IHttpContextAccessor, HttpContextAccessor>();
            services.AddMvc();
        }

        [Fact]
        public async Task WsFederation_metadata_success()
        {
            var response = await _client.GetAsync("/wsfederation");
            var message = await response.Content.ReadAsStringAsync();
            Assert.NotEmpty(message);
            Assert.True(message.StartsWith("<?xml version=\"1.0\" encoding=\"utf-8\"?><EntityDescriptor entityID=\"http://localhost\""));
        }

        [Fact]
        public async Task WsFederation_signin_and_redirect_to_login_page_Success()
        {
            var wsMessage = new WsFederationMessage
            {
                IssuerAddress = "/wsfederation",
                Wtrealm = "urn:owinrp",
                Wreply = "http://localhost:10313/",
            };
            var singInUrl = wsMessage.CreateSignInUrl();
            var response = await _client.GetAsync(singInUrl);
            Assert.Equal(HttpStatusCode.Found, response.StatusCode);
            var expectedLocation = "/account/login?returnUrl=%2Fwsfederation%3Fwtrealm%3Durn%253Aowinrp%26wreply%3Dhttp%253A%252F%252Flocalhost%253A10313%252F%26wa%3Dwsignin1.0";
            Assert.Equal(expectedLocation, response.Headers.Location.OriginalString);
        }
    }
}
