// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.AspNetCore.Connections;
using Microsoft.AspNetCore.Connections.Features;
using Microsoft.AspNetCore.Http.Connections;
using MQTTnet.Adapter;
using MQTTnet.Diagnostics.Logger;
using MQTTnet.Formatter;
using MQTTnet.Server;
using System;
using System.Threading.Tasks;

namespace MQTTnet.AspNetCore;

sealed class MqttConnectionHandler : ConnectionHandler
{
    readonly IMqttNetLogger _logger;
    readonly MqttBufferWriterPool _bufferWriterPool;

    public bool UseFlag { get; set; }

    public bool MapFlag { get; set; }

    public bool ListenFlag { get; set; }

    public Func<IMqttChannelAdapter, Task>? ClientHandler { get; set; }

    public MqttConnectionHandler(
        IMqttNetLogger logger,
        MqttBufferWriterPool bufferWriterPool)
    {
        _logger = logger;
        _bufferWriterPool = bufferWriterPool;
    }

    public override async Task OnConnectedAsync(ConnectionContext connection)
    {
        var clientHandler = ClientHandler;
        if (clientHandler == null)
        {
            connection.Abort();
            _logger.Publish(MqttNetLogLevel.Warning, nameof(MqttConnectionHandler), $"{nameof(MqttServer)} has not been started yet.", null, null);
            return;
        }

        // required for websocket transport to work
        var transferFormatFeature = connection.Features.Get<ITransferFormatFeature>();
        if (transferFormatFeature != null)
        {
            transferFormatFeature.ActiveFormat = TransferFormat.Binary;
        }

        // WebSocketConnectionFeature will be accessed in MqttChannel
        var httpContext = connection.GetHttpContext();
        if (httpContext != null && httpContext.WebSockets.IsWebSocketRequest)
        {
            var path = httpContext.Request.Path;
            connection.Features.Set(new WebSocketConnectionFeature(path));
        }

        var bufferWriter = _bufferWriterPool.Rent();
        try
        {
            var formatter = new MqttPacketFormatterAdapter(bufferWriter);
            using var adapter = new MqttServerChannelAdapter(formatter, connection, httpContext);
            await clientHandler(adapter).ConfigureAwait(false);
        }
        finally
        {
            _bufferWriterPool.Return(bufferWriter);
        }
    }
}