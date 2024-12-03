// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;

namespace MQTTnet.AspNetCore
{
    public sealed class MqttBufferWriterPoolOptions
    {
        public bool Enable { get; set; } = true;

        /// <summary>
        /// When the life time of the MqttBufferWriter is less than this value, MqttBufferWriter is pooled.
        /// </summary>
        public TimeSpan PoolingItemMaxLifeTime { get; set; } = TimeSpan.FromMinutes(1d);

        /// <summary>
        /// Whether to pool MqttBufferWriter with BufferSize greater than the default buffer size.
        /// </summary>
        public bool PoolingLargeBufferSizeItem { get; set; } = true;
    }
}
