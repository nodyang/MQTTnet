// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.Extensions.Options;
using MQTTnet.Server;
using System.Collections.Generic;

namespace MQTTnet.AspNetCore
{
    sealed class MqttServerStopOptionsFactory : MqttOptionsFactory<MqttServerStopOptions>
    {
        public MqttServerStopOptionsFactory(
            IOptions<MqttServerStopOptionsBuilder> optionsBuilderOptions,
            IEnumerable<IConfigureOptions<MqttServerStopOptions>> setups,
            IEnumerable<IPostConfigureOptions<MqttServerStopOptions>> postConfigures)
            : base(optionsBuilderOptions.Value.Build, setups, postConfigures)
        {
        }
    }
}
