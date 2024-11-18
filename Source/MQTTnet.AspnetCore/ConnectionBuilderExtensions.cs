// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.AspNetCore.Connections;
using Microsoft.Extensions.DependencyInjection;
using MQTTnet.Server;
using System;

namespace MQTTnet.AspNetCore
{
    public static class ConnectionBuilderExtensions
    {
        /// <summary>
        /// Handle the connection using the specified MQTT protocols
        /// </summary>
        /// <param name="builder"></param>
        /// <param name="protocols"></param>
        /// <returns></returns>
        public static IConnectionBuilder UseMqtt(this IConnectionBuilder builder, MqttProtocols protocols = MqttProtocols.MqttAndWebSocket)
        {
            // check services.AddMqttServer()
            builder.ApplicationServices.GetRequiredService<MqttServer>();

            builder.ApplicationServices.GetRequiredService<MqttConnectionHandler>().UseFlag = true;
            if (protocols == MqttProtocols.Mqtt)
            {
                return builder.UseConnectionHandler<MqttConnectionHandler>();
            }
            else if (protocols == MqttProtocols.WebSocket)
            {
                return builder;
            }
            else if (protocols == MqttProtocols.MqttAndWebSocket)
            {
                var middleware = builder.ApplicationServices.GetRequiredService<MqttConnectionMiddleware>();
                return builder.Use(next => context => middleware.InvokeAsync(next, context));
            }

            throw new NotSupportedException(protocols.ToString());
        }
    }
}
