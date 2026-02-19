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
using Microsoft.AspNetCore.Connections;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using DSC.TLink.ITv2;
using DSC.TLink.ITv2.MediatR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace DSC.TLink;

public static class StartupExtensions
{
    public static WebApplicationBuilder UseITv2(this WebApplicationBuilder builder)
    {
        // Configuration
        builder.Services.Configure<ITv2Settings>(builder.Configuration.GetSection(ITv2Settings.SectionName));
        builder.Services.AddSingleton(sp =>
            sp.GetRequiredService<IOptions<ITv2Settings>>().Value);

        // MediatR — registers SessionCommandHandler + notification handlers
        builder.Services.AddMediatR(configuration =>
        {
            configuration.RegisterServicesFromAssembly(typeof(ITv2Session).Assembly);
        });

        // Singletons (shared across all connections)
        builder.Services.AddSingleton<IITv2SessionManager, ITv2SessionManager>();
        builder.Services.AddSingleton<ITv2ConnectionHandler>();

        // Kestrel
        builder.WebHost.ConfigureKestrel((context, options) =>
        {
            var listenPort = context.Configuration.GetValue(
                $"{ITv2Settings.SectionName}:{nameof(ITv2Settings.ListenPort)}",
                ITv2Settings.DefaultListenPort);

            options.ListenAnyIP(listenPort, lo => lo.UseConnectionHandler<ITv2ConnectionHandler>());

            var httpPort = context.Configuration.GetValue("HttpPort", 8080);
            options.ListenAnyIP(httpPort);

            if (context.Configuration.GetValue("EnableHttps", false))
            {
                var httpsPort = context.Configuration.GetValue("HttpsPort", 8443);
                options.ListenAnyIP(httpsPort, lo => lo.UseHttps());
            }
        });

        builder.Services.AddLogging();
        return builder;
    }
}
