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
using Microsoft.AspNetCore.Connections;
using Microsoft.Extensions.DependencyInjection;
using DSC.TLink.ITv2;
using DSC.TLink.ITv2.MediatR;

namespace DSC.TLink
{
	public static class StartupExtensions
	{
		/// <summary>
		/// Registers ITv2 services and configures Kestrel to listen for panel connections.
		/// The host application is responsible for providing an
		/// <see cref="IConnectionSettingsProvider"/> implementation.
		/// </summary>
		/// <param name="builder">The web application builder</param>
		/// <param name="listenPort">TCP port for panel connections (default: <see cref="ConnectionSettings.DefaultListenPort"/>)</param>
		public static WebApplicationBuilder UseITv2(this WebApplicationBuilder builder, int listenPort = ConnectionSettings.DefaultListenPort)
		{
			// MediatR - Register TLink assembly only
			builder.Services.AddMediatR(configuration =>
			{
				configuration.RegisterServicesFromAssembly(typeof(ITv2Session).Assembly);
			});

			// Singleton services (shared across all connections)
			builder.Services.AddSingleton<IITv2SessionManager, ITv2SessionManager>();
			builder.Services.AddSingleton<SessionMediator>();
			builder.Services.AddSingleton<ITv2ConnectionHandler>();
			builder.Services.AddHostedService<SessionShutdownService>();

			// Configure Kestrel with ITv2 connection handler
			builder.WebHost.ConfigureKestrel((context, options) =>
			{
				options.ListenAnyIP(listenPort, listenOptions =>
				{
					listenOptions.UseConnectionHandler<ITv2ConnectionHandler>();
				});
			});

			builder.Services.AddLogging();
			return builder;
		}
	}
}
