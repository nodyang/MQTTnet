// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.AspNetCore.Connections;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using MQTTnet.Adapter;
using MQTTnet.Exceptions;
using MQTTnet.Formatter;
using MQTTnet.Internal;
using MQTTnet.Packets;
using System;
using System.Buffers;
using System.IO;
using System.IO.Pipelines;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;

namespace MQTTnet.AspNetCore;

class MqttChannel : IAspNetCoreMqttChannel, IDisposable
{
    readonly ConnectionContext _connection;
    readonly HttpContext? _httpContext;

    readonly AsyncLock _writerLock = new();
    readonly PipeReader _input;
    readonly PipeWriter _output;
    readonly MqttPacketInspector? _packetInspector;
    bool _allowPacketFragmentation = false;

    public MqttPacketFormatterAdapter PacketFormatterAdapter { get; }

    public long BytesReceived { get; private set; }

    public long BytesSent { get; private set; }

    public X509Certificate2? ClientCertificate { get; }

    public EndPoint? RemoteEndPoint { get; private set; }

    public bool IsSecureConnection { get; }

    public bool IsWebSocketConnection { get; }

    public HttpContext? HttpContext => _httpContext;

    public MqttChannel(
        MqttPacketFormatterAdapter packetFormatterAdapter,
        ConnectionContext connection,
        HttpContext? httpContext,
        MqttPacketInspector? packetInspector)
    {
        PacketFormatterAdapter = packetFormatterAdapter;
        _connection = connection;
        _httpContext = httpContext;
        _packetInspector = packetInspector;

        _input = connection.Transport.Input;
        _output = connection.Transport.Output;

        var tlsConnectionFeature = GetFeature<ITlsConnectionFeature>();
        var webSocketConnectionFeature = GetFeature<WebSocketConnectionFeature>();

        IsWebSocketConnection = webSocketConnectionFeature != null;
        IsSecureConnection = tlsConnectionFeature != null;
        ClientCertificate = tlsConnectionFeature?.ClientCertificate;
        RemoteEndPoint = GetRemoteEndPoint(connection.RemoteEndPoint, httpContext);
    }


    public TFeature? GetFeature<TFeature>()
    {
        var feature = _connection.Features.Get<TFeature>();
        if (feature != null)
        {
            return feature;
        }

        if (_httpContext != null)
        {
            return _httpContext.Features.Get<TFeature>();
        }

        return default;
    }

    private static EndPoint? GetRemoteEndPoint(EndPoint? remoteEndPoint, HttpContext? httpContext)
    {
        if (remoteEndPoint != null)
        {
            return remoteEndPoint;
        }

        if (httpContext != null)
        {
            var httpConnection = httpContext.Connection;
            var remoteAddress = httpConnection.RemoteIpAddress;
            if (remoteAddress != null)
            {
                return new IPEndPoint(remoteAddress, httpConnection.RemotePort);
            }
        }

        return null;
    }

    public void SetAllowPacketFragmentation(bool value)
    {
        _allowPacketFragmentation = value;
    }

    public async Task DisconnectAsync()
    {
        try
        {
            await _input.CompleteAsync().ConfigureAwait(false);
            await _output.CompleteAsync().ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            if (!WrapAndThrowException(exception))
            {
                throw;
            }
        }
    }

    public virtual void Dispose()
    {
        _writerLock.Dispose();
    }

    public async Task<MqttPacket?> ReceivePacketAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await ReceivePacketCoreAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        catch (ObjectDisposedException)
        {
        }
        catch (Exception exception)
        {
            if (!WrapAndThrowException(exception))
            {
                throw;
            }
        }

        return null;
    }

    private async Task<MqttPacket?> ReceivePacketCoreAsync(CancellationToken cancellationToken)
    {
        try
        {
            _packetInspector?.BeginReceivePacket();

            while (!cancellationToken.IsCancellationRequested)
            {
                ReadResult readResult;
                var readTask = _input.ReadAsync(cancellationToken);
                if (readTask.IsCompleted)
                {
                    readResult = readTask.Result;
                }
                else
                {
                    readResult = await readTask.ConfigureAwait(false);
                }

                var buffer = readResult.Buffer;

                var consumed = buffer.Start;
                var observed = buffer.Start;

                try
                {
                    if (!buffer.IsEmpty)
                    {
                        if (PacketFormatterAdapter.TryDecode(buffer, _packetInspector, out var packet, out consumed, out observed, out var received))
                        {
                            BytesReceived += received;

                            if (_packetInspector != null)
                            {
                                await _packetInspector.EndReceivePacket().ConfigureAwait(false);
                            }
                            return packet;
                        }
                    }
                    else if (readResult.IsCompleted)
                    {
                        throw new MqttCommunicationException("Connection Aborted");
                    }
                }
                finally
                {
                    // The buffer was sliced up to where it was consumed, so we can just advance to the start.
                    // We mark examined as buffer.End so that if we didn't receive a full frame, we'll wait for more data
                    // before yielding the read again.
                    _input.AdvanceTo(consumed, observed);
                }
            }
        }
        catch (Exception)
        {
            // completing the channel makes sure that there is no more data read after a protocol error
            await _input.CompleteAsync().ConfigureAwait(false);
            await _output.CompleteAsync().ConfigureAwait(false);

            throw;
        }

        cancellationToken.ThrowIfCancellationRequested();
        return null;
    }

    public void ResetStatistics()
    {
        BytesReceived = 0;
        BytesSent = 0;
    }

    public async Task SendPacketAsync(MqttPacket packet, CancellationToken cancellationToken)
    {
        try
        {
            await SendPacketCoreAsync(packet, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            if (!WrapAndThrowException(exception))
            {
                throw;
            }
        }
    }

    private async Task SendPacketCoreAsync(MqttPacket packet, CancellationToken cancellationToken)
    {
        using (await _writerLock.EnterAsync(cancellationToken).ConfigureAwait(false))
        {
            try
            {
                var buffer = PacketFormatterAdapter.Encode(packet);
                if (_packetInspector != null)
                {
                    await _packetInspector.BeginSendPacket(buffer).ConfigureAwait(false);
                }

                if (buffer.Payload.Length == 0)
                {
                    // zero copy
                    // https://github.com/dotnet/runtime/blob/e31ddfdc4f574b26231233dc10c9a9c402f40590/src/libraries/System.IO.Pipelines/src/System/IO/Pipelines/StreamPipeWriter.cs#L279
                    await _output.WriteAsync(buffer.Packet, cancellationToken).ConfigureAwait(false);
                }
                else if (_allowPacketFragmentation)
                {
                    await _output.WriteAsync(buffer.Packet, cancellationToken).ConfigureAwait(false);
                    foreach (var memory in buffer.Payload)
                    {
                        await _output.WriteAsync(memory, cancellationToken).ConfigureAwait(false);
                    }
                }
                else
                {
                    // Make sure the MQTT packet is in a WebSocket frame to be compatible with JavaScript WebSocket
                    WritePacketBuffer(_output, buffer);
                    await _output.FlushAsync(cancellationToken).ConfigureAwait(false);
                }

                BytesSent += buffer.Length;
            }
            finally
            {
                PacketFormatterAdapter.Cleanup();
            }
        }
    }

    static void WritePacketBuffer(PipeWriter output, MqttPacketBuffer buffer)
    {
        // copy MqttPacketBuffer's Packet and Payload to the same buffer block of PipeWriter
        // MqttPacket will be transmitted within the bounds of a WebSocket frame after PipeWriter.FlushAsync

        var span = output.GetSpan(buffer.Length);

        buffer.Packet.AsSpan().CopyTo(span);
        var offset = buffer.Packet.Count;
        buffer.Payload.CopyTo(destination: span.Slice(offset));
        output.Advance(buffer.Length);
    }

    public static bool WrapAndThrowException(Exception exception)
    {
        if (exception is OperationCanceledException ||
            exception is MqttCommunicationTimedOutException ||
            exception is MqttCommunicationException ||
            exception is MqttProtocolViolationException)
        {
            return false;
        }

        if (exception is IOException && exception.InnerException is SocketException innerException)
        {
            exception = innerException;
        }

        if (exception is SocketException socketException)
        {
            if (socketException.SocketErrorCode == SocketError.OperationAborted)
            {
                throw new OperationCanceledException();
            }

            if (socketException.SocketErrorCode == SocketError.ConnectionAborted)
            {
                throw new MqttCommunicationException(socketException);
            }
        }

        if (exception is COMException comException)
        {
            const uint ErrorOperationAborted = 0x800703E3;
            if ((uint)comException.HResult == ErrorOperationAborted)
            {
                throw new OperationCanceledException();
            }
        }

        throw new MqttCommunicationException(exception);
    }
}