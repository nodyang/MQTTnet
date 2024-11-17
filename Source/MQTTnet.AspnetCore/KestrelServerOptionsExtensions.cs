// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.AspNetCore.Server.Kestrel.Https;
using Microsoft.Extensions.DependencyInjection;
using MQTTnet.Exceptions;
using MQTTnet.Server;
using System;
using System.Net;
using System.Security.Cryptography.X509Certificates;

namespace MQTTnet.AspNetCore
{
    public static class KestrelServerOptionsExtensions
    {
        /// <summary>
        /// Listen all endponts in MqttServerOptions
        /// </summary>
        /// <param name="kestrel"></param>
        /// <exception cref="MqttConfigurationException"></exception>
        /// <returns></returns>
        public static KestrelServerOptions ListenMqtt(this KestrelServerOptions kestrel)
        {
            return kestrel.ListenMqtt(default(Action<HttpsConnectionAdapterOptions>));
        }

        /// <summary>
        /// Listen all endponts in MqttServerOptions
        /// </summary>
        /// <param name="kestrel"></param>
        /// <param name="serverCertificate"></param>
        /// <exception cref="MqttConfigurationException"></exception>
        /// <returns></returns>
        public static KestrelServerOptions ListenMqtt(this KestrelServerOptions kestrel, X509Certificate2? serverCertificate)
        {
            return kestrel.ListenMqtt(tls => tls.ServerCertificate = serverCertificate);
        }

        /// <summary>
        /// Listen all endponts in MqttServerOptions
        /// </summary>
        /// <param name="kestrel"></param>
        /// <param name="tlsConfigure"></param>
        /// <exception cref="MqttConfigurationException"></exception>
        /// <returns></returns>
        public static KestrelServerOptions ListenMqtt(this KestrelServerOptions kestrel, Action<HttpsConnectionAdapterOptions>? tlsConfigure)
        {
            var serverOptions = kestrel.ApplicationServices.GetRequiredService<MqttServerOptions>();
            var connectionHandler = kestrel.ApplicationServices.GetRequiredService<MqttConnectionHandler>();

            Listen(serverOptions.DefaultEndpointOptions);
            Listen(serverOptions.TlsEndpointOptions);

            return connectionHandler.ListenFlag
                ? kestrel
                : throw new MqttConfigurationException("None of the MqttServerOptions Endpoints are enabled.");

            void Listen(MqttServerTcpEndpointBaseOptions endpoint)
            {
                if (!endpoint.IsEnabled)
                {
                    return;
                }

                // No need to listen any IPv4 when has IPv6Any
                if (!IPAddress.IPv6Any.Equals(endpoint.BoundInterNetworkV6Address))
                {
                    kestrel.Listen(endpoint.BoundInterNetworkAddress, endpoint.Port, UseMiddleware);
                }
                kestrel.Listen(endpoint.BoundInterNetworkV6Address, endpoint.Port, UseMiddleware);
                connectionHandler.ListenFlag = true;


                void UseMiddleware(ListenOptions listenOptions)
                {
                    if (endpoint is MqttServerTlsTcpEndpointOptions tlsEndPoint)
                    {
                        var httpsOptions = CreateHttpsOptions(tlsEndPoint);
                        tlsConfigure?.Invoke(httpsOptions);
                        listenOptions.UseHttps(httpsOptions);
                    }
                    listenOptions.UseMqtt();
                }
            }
        }

        private static HttpsConnectionAdapterOptions CreateHttpsOptions(MqttServerTlsTcpEndpointOptions tlsEndPoint)
        {
            var options = new HttpsConnectionAdapterOptions
            {
                SslProtocols = tlsEndPoint.SslProtocol,
                CheckCertificateRevocation = tlsEndPoint.CheckCertificateRevocation,
            };

            if (tlsEndPoint.ClientCertificateRequired)
            {
                options.ClientCertificateMode = ClientCertificateMode.RequireCertificate;
            }

            if (tlsEndPoint.CertificateProvider != null)
            {
                options.ServerCertificateSelector = (context, host) => tlsEndPoint.CertificateProvider.GetCertificate();
            }

            if (tlsEndPoint.RemoteCertificateValidationCallback != null)
            {
                options.ClientCertificateValidation = (cert, chain, errors) => tlsEndPoint.RemoteCertificateValidationCallback(tlsEndPoint, cert, chain, errors);
            }

            return options;
        }
    }
}