// DSC TLink - a communications library for DSC Powerseries NEO alarm panels
// Copyright (C) 2024 Brian Humlicek
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
//
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.
//
// You should have received a copy of the GNU General Public License
// along with this program.  If not, see <https://www.gnu.org/licenses/>.

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using System.Net;
using Microsoft.AspNetCore.Connections;
using Microsoft.Extensions.DependencyInjection;
using DSC.TLink.ITv2;
using DSC.TLink.ITv2.MediatR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace DSC.TLink
{
	public static class StartupExtensions
	{
		/// <summary>
		/// Registers ITv2 services and configures Kestrel for panel connections.
		/// </summary>
		/// <param name="builder">The web application builder</param>
		public static WebApplicationBuilder UseITv2(this WebApplicationBuilder builder)
		{
            // Configuration
            builder.Services.Configure<ITv2Settings>(builder.Configuration.GetSection(ITv2Settings.SectionName));
            builder.Services.AddSingleton(sp => 
                sp.GetRequiredService<IOptions<ITv2Settings>>().Value);

            // MediatR - Register TLink assembly only
            builder.Services.AddMediatR(configuration =>
            {
                configuration.RegisterServicesFromAssembly(typeof(ITv2Session).Assembly);
            });

            // Singleton services (shared across all connections)
            builder.Services.AddSingleton<IITv2SessionManager, ITv2SessionManager>();
            builder.Services.AddSingleton<AppSequenceTransactionRegistry>();
            builder.Services.AddSingleton<InboundNotificationPublisher>();
            builder.Services.AddSingleton<ITv2ConnectionHandler>();

            // Scoped services (per-connection)
            builder.Services.AddScoped<TLinkClient>();
            builder.Services.AddScoped<ITv2Session>();

            // Configure Kestrel with ITv2 connection handler
            builder.WebHost.ConfigureKestrel((context, options) =>
			{
                var listenPort = context.Configuration.GetValue($"{ITv2Settings.SectionName}:{nameof(ITv2Settings.ListenPort)}", ITv2Settings.DefaultListenPort);
                
                // Configure ITv2 panel connection port
                options.ListenAnyIP(listenPort, listenOptions =>
				{
					listenOptions.UseConnectionHandler<ITv2ConnectionHandler>();
				});
                
                // Web UI ports - use environment variables if set, otherwise defaults
                var httpPort = context.Configuration.GetValue("HttpPort", 8080);
                var httpsPort = context.Configuration.GetValue("HttpsPort", 8443);
                var enableHttps = context.Configuration.GetValue("EnableHttps", false);
                
                options.ListenAnyIP(httpPort);
                
                // Only configure HTTPS if explicitly enabled (for production with proper certs)
                if (enableHttps)
                {
                    options.ListenAnyIP(httpsPort, listenOptions => listenOptions.UseHttps());
                }
			});

            builder.Services.AddLogging();
			return builder;
		}
	}
}
